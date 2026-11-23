import { test } from 'node:test';
import assert from 'node:assert/strict';
import { appendFileSync, rmSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { normUrl } from '../lib/run.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

function synced() {
  const f = makeFixture(SAMPLE);
  const r = f.sync(f.base);
  assert.equal(r.code, 0, r.err);
  return f;
}

test('--check passes on a fresh sync', () => {
  const f = synced();
  try {
    const r = f.sync('--check');
    assert.equal(r.code, 0, r.err);
    assert.match(r.out, /check passed: 4 vendored files match upstream/);
  } finally { f.cleanup(); }
});

test('--check fails after a hand edit to a vendored file', () => {
  const f = synced();
  try {
    appendFileSync(f.vendored('src/Lib/Yoga/Node.cs'), '// a hand edit\n');
    const r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /differs: src\/Lib\/Yoga\/Node\.cs/);
    assert.match(r.err, /never edited by hand/);
  } finally { f.cleanup(); }
});

test('--check fails on a line-ending change alone', () => {
  const f = synced();
  try {
    writeFileSync(f.vendored('LICENSE'), 'MIT\r\n');
    const r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /differs: LICENSE/);
  } finally { f.cleanup(); }
});

test('--check fails on a deleted or an added file under the vendor dir', () => {
  const f = synced();
  try {
    rmSync(f.vendored('src/Lib/Core/Plain.cs'));
    writeFileSync(f.vendored('src/Lib/Core/Mine.cs'), 'class Mine {}\n');
    const r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /missing: src\/Lib\/Core\/Plain\.cs/);
    assert.match(r.err, /not generated .*: src\/Lib\/Core\/Mine\.cs/);
  } finally { f.cleanup(); }
});

test('--check fails when the tier map changed but the sync was not re-run', () => {
  const f = synced();
  try {
    f.settings({ rules: SAMPLE.tiers.rules.map((r) => (r.path === 'src/Lib/Core/Plain.cs' ? { ...r, tier: 3 } : r)) });
    const r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /not generated .*: src\/Lib\/Core\/Plain\.cs/);
  } finally { f.cleanup(); }
});

test('a sync to a newer commit bumps the lock, writes the report, and --check passes on the result', () => {
  const f = synced();
  try {
    const next = f.commit({ 'src/Lib/Yoga/Node.cs': 'namespace Y;\nclass Node { int depth; }\n' }, 'Track depth (#7)');
    const r = f.sync(next);
    assert.equal(r.code, 0, r.err);
    const lock = f.read('UPSTREAM.lock');
    assert.match(lock, new RegExp('^baseline=' + next + '$', 'm'));
    assert.match(lock, /^baseline_note=PR #7$/m, 'no checkout claim: the fixture runs with --no-checkout');
    assert.match(lock, /^# fixture$/m, 'comments are kept');
    assert.match(f.read('src/Upstream/src/Lib/Yoga/Node.cs'), new RegExp('last changed upstream in ' + next.slice(0, 12) + '[^]*int depth'));
    assert.match(f.read('src/Upstream/src/Lib/Core/Plain.cs'), new RegExp('last changed upstream in ' + f.base.slice(0, 12)), 'untouched files keep their header');
    assert.ok(f.exists('docs/sync/' + f.base.slice(0, 8) + '-' + next.slice(0, 8) + '.md'));
    assert.equal(f.sync('--check').code, 0);
  } finally { f.cleanup(); }
});

test('the remote is found from https, ssh and scp-style URLs alike', () => {
  const want = normUrl('https://github.com/Org/Repo');
  for (const u of ['https://github.com/org/repo.git', 'git@github.com:org/repo.git', 'ssh://git@github.com/org/repo', 'ssh://git@github.com:22/org/repo/'])
    assert.equal(normUrl(u), want, u);
  assert.notEqual(normUrl('https://github.com/org/other'), want);
});

test('a vendorDir that is not a dedicated folder inside the repo is refused before anything is deleted', () => {
  const f = makeFixture(SAMPLE);
  try {
    for (const bad of ['', '.', 'src', '../outside', 'src/..']) {
      f.put('tools/sync/sync.json', JSON.stringify({ vendorDir: bad, reportDir: 'docs/sync', watch: ['src/'], verify: [] }));
      const r = f.sync(f.base);
      assert.equal(r.code, 1, JSON.stringify(bad));
      assert.match(r.err, /vendorDir must be a folder/);
    }
    assert.ok(f.exists('tools/sync/tiers.json'), 'the repo is intact');
    f.put('tools/sync/sync.json', JSON.stringify({ vendorDir: 'src/Upstream', reportDir: 'docs/sync', verify: [] }));
    assert.match(f.sync(f.base).err, /watch must be/);
    f.put('tools/sync/sync.json', JSON.stringify({ vendorDir: 'src/Upstream', watch: ['src/'], verify: [] }));
    assert.match(f.sync(f.base).err, /reportDir must be/);
  } finally { f.cleanup(); }
});

test('--commit commits only what the sync wrote, refuses a dirty index, and skips an empty commit', () => {
  const f = makeFixture(SAMPLE);
  const g = (...a) => execFileSync('git', ['-c', 'core.autocrlf=false', '-c', 'commit.gpgsign=false', '-c', 'user.name=t', '-c', 'user.email=t@example.invalid', ...a],
    { cwd: f.repo, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    g('init', '-q');
    // The harness's own `git commit` (--commit) uses the repo's config: give the fixture repo an identity, so the test
    // doesn't depend on the machine's (a fresh CI runner has none).
    g('config', 'user.name', 't');
    g('config', 'user.email', 't@example.invalid');
    g('config', 'commit.gpgsign', 'false');
    g('config', 'core.autocrlf', 'false');
    g('add', '-A');
    g('commit', '-q', '-m', 'settings');
    f.put('notes.txt', 'staged work\n');
    g('add', 'notes.txt');
    let r = f.sync(f.base, '--commit');
    assert.equal(r.code, 1);
    assert.match(r.err, /--commit needs an empty index[^]*notes\.txt/);
    assert.ok(!f.exists('src/Upstream'), 'nothing written');
    g('reset', '-q');
    r = f.sync(f.base, '--commit');
    assert.equal(r.code, 0, r.err);
    assert.match(r.out, /\(committed\)/);
    assert.equal(g('log', '-1', '--format=%s'), 'sync upstream: regenerate at ' + f.base.slice(0, 8));
    assert.deepEqual(g('show', '--name-only', '--format=', 'HEAD').split('\n').filter((p) => !p.startsWith('src/Upstream/')), []);
    assert.equal(g('status', '--porcelain', '--', 'notes.txt'), '?? notes.txt', 'the unrelated file is not committed');
    r = f.sync(f.base, '--commit');
    assert.equal(r.code, 0, r.err);
    assert.match(r.out, /nothing changed, so nothing was committed/);
  } finally { f.cleanup(); }
});

test('--dry-run writes nothing', () => {
  const f = makeFixture(SAMPLE);
  try {
    const next = f.commit({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const r = f.sync(next, '--dry-run');
    assert.equal(r.code, 0, r.err);
    assert.match(r.out, /# Upstream sync/);
    assert.ok(!f.exists('src/Upstream'));
    assert.ok(!f.exists('docs/sync'));
    assert.match(f.read('UPSTREAM.lock'), new RegExp('baseline=' + f.base));
  } finally { f.cleanup(); }
});
