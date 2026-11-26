// Tests of the samples job's docs-only skip (#71): node --test tools/migration/test/. The decision on made-up facts,
// then should-measure.mjs on throwaway git repositories (no network).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { decide, isDocs } from '../lib/changes.mjs';

const script = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'should-measure.mjs');
// Built at run time, so this file holds no email for the repo-wide personal-data scan.
const EMAIL = 'test' + String.fromCharCode(64) + 'example.invalid';
const branch = { event: 'push', ref: 'refs/heads/docs/x', shallow: false, mergeBase: 'a'.repeat(40) };

test('docs are files under docs/ and .md files anywhere, nothing else', () => {
  for (const f of ['docs/guide/x.md', 'docs/spikes/B-scripts/run.py', 'README.md', 'src/Isotope.UI.Avalonia/README.md', 'spikes/A-engine/notes.md'])
    assert.equal(isDocs(f), true, f);
  for (const f of ['tools/migration/samples-baseline.json', 'spikes/A-engine/run-spike.mjs', 'samples/App.cs', '.github/workflows/ci.yml',
    'UPSTREAM.lock', 'README.MD', 'x.md.cs', 'notes.markdown', 'mydocs/x.txt', 'src/docs/x.cs'])
    assert.equal(isDocs(f), false, f);
});

test('a branch push changing only docs skips the measurement', () => {
  assert.equal(decide({ ...branch, files: ['docs/guide/a.md', 'README.md'] }).measure, false);
});

test('one file that is not docs measures', () => {
  assert.equal(decide({ ...branch, files: ['docs/guide/a.md', 'tools/migration/measure-samples.mjs'] }).measure, true);
});

test('what cannot be decided measures: main, other events, tags, shallow history, no merge base, a git error, no files', () => {
  const docs = ['docs/a.md'];
  for (const facts of [
    { ...branch, ref: 'refs/heads/main', files: docs },
    { ...branch, event: 'workflow_dispatch', files: docs },
    { ...branch, event: 'pull_request', files: docs },
    { ...branch, event: undefined, files: docs },
    { ...branch, ref: 'refs/tags/v1.0.0', files: docs },
    { ...branch, ref: undefined, files: docs },
    { ...branch, shallow: true, files: docs },
    { ...branch, mergeBase: null, files: docs },
    { ...branch, error: 'git diff exited 128', files: docs },
    { ...branch, files: [] },
    { ...branch, files: undefined },
  ]) assert.equal(decide(facts).measure, true, JSON.stringify(facts));
});

test('the reason never holds a file name', () => {
  const name = 'docs/$(touch pwned)::warning::x.md';
  assert.equal(decide({ ...branch, files: [name] }).reason.includes('pwned'), false);
  assert.equal(decide({ ...branch, files: [name, 'a;b.cs'] }).reason.includes('a;b'), false);
});

// ── should-measure.mjs on a throwaway repository ──

function repo(setup) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'isotope-changes-'));
  const git = (...a) => {
    const r = spawnSync('git', ['-c', 'user.name=test', '-c', 'user.email=' + EMAIL, '-c', 'commit.gpgsign=false', ...a], { cwd: dir, encoding: 'utf8' });
    assert.equal(r.status, 0, r.stderr);
    return r.stdout.trim();
  };
  const commit = (files, message) => {
    for (const [f, text] of Object.entries(files)) {
      fs.mkdirSync(path.dirname(path.join(dir, f)), { recursive: true });
      if (text === null) fs.rmSync(path.join(dir, f)); else fs.writeFileSync(path.join(dir, f), text);
    }
    git('add', '-A');
    git('commit', '-q', '-m', message);
  };
  git('init', '-q', '-b', 'main');
  commit({ 'src/A.cs': 'class A {}\n', 'docs/a.md': '# A\n' }, 'base');
  setup({ git, commit, dir });
  return dir;
}

function run(dir, env, args = []) {
  try {
    const r = spawnSync(process.execPath, [script, ...args], { cwd: dir, encoding: 'utf8', env: { ...process.env, GITHUB_EVENT_NAME: 'push', GITHUB_REF: 'refs/heads/x', ...env } });
    assert.equal(r.status, 0, r.stderr);
    return { out: r.stdout, reason: r.stderr };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// origin/main at the base commit, and a branch x on top of it.
const onBranch = (files) => ({ git, commit }) => {
  git('update-ref', 'refs/remotes/origin/main', 'HEAD');
  git('checkout', '-q', '-b', 'x');
  commit(files, 'change');
};

test('a docs-only branch prints measure=false, and nothing but that on stdout', () => {
  const r = run(repo(onBranch({ 'docs/a.md': '# A, edited\n', 'README.md': 'new\n' })));
  assert.equal(r.out, 'measure=false\n');
  assert.match(r.reason, /all 2 changed files are docs/);
});

test('a branch changing code prints measure=true', () => {
  assert.equal(run(repo(onBranch({ 'docs/a.md': '# A, edited\n', 'src/A.cs': 'class A { }\n' }))).out, 'measure=true\n');
});

test('code renamed into docs/ measures: the old path counts too', () => {
  assert.equal(run(repo(({ git }) => {
    git('update-ref', 'refs/remotes/origin/main', 'HEAD');
    git('checkout', '-q', '-b', 'x');
    git('mv', 'src/A.cs', 'docs/A.cs');
    git('commit', '-q', '-m', 'move');
  })).out, 'measure=true\n');
});

test('main, and workflow_dispatch, measure even with docs-only changes', () => {
  assert.equal(run(repo(onBranch({ 'docs/a.md': 'x\n' })), { GITHUB_REF: 'refs/heads/main' }).out, 'measure=true\n');
  assert.equal(run(repo(onBranch({ 'docs/a.md': 'x\n' })), { GITHUB_EVENT_NAME: 'workflow_dispatch' }).out, 'measure=true\n');
});

test('no origin/main and nothing to fetch it from measures', () => {
  const r = run(repo(({ git, commit }) => { git('checkout', '-q', '-b', 'x'); commit({ 'docs/a.md': 'x\n' }, 'docs'); }));
  assert.equal(r.out, 'measure=true\n');
  assert.match(r.reason, /could not be listed/);
});

test('a branch with no history in common with origin/main measures', () => {
  const r = run(repo(({ git, commit, dir }) => {
    git('update-ref', 'refs/remotes/origin/main', 'HEAD');
    git('checkout', '-q', '--orphan', 'x');
    git('rm', '-q', '-r', '--cached', '.');
    for (const f of ['src/A.cs', 'docs/a.md']) fs.rmSync(path.join(dir, f));
    commit({ 'docs/b.md': 'x\n' }, 'orphan');
  }));
  assert.equal(r.out, 'measure=true\n');
  assert.match(r.reason, /no merge base/);
});

test('outside a git repository it measures', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'isotope-changes-'));
  assert.equal(run(dir).out, 'measure=true\n');
});

test('--base-branch compares with that branch, and a bad name or argument measures', () => {
  const based = ({ git, commit }) => {
    git('update-ref', 'refs/remotes/origin/main', 'HEAD');
    commit({ 'src/A.cs': 'class A { int x; }\n' }, 'code on the base branch');
    git('update-ref', 'refs/remotes/origin/base', 'HEAD');
    git('checkout', '-q', '-b', 'x');
    commit({ 'docs/a.md': 'x\n' }, 'docs');
  };
  assert.equal(run(repo(based)).out, 'measure=true\n');
  assert.equal(run(repo(based), {}, ['--base-branch', 'base']).out, 'measure=false\n');
  const self = run(repo(based), { GITHUB_REF: 'refs/heads/base' }, ['--base-branch', 'base']);
  assert.equal(self.out, 'measure=true\n');
  assert.match(self.reason, /the base branch itself/);
  for (const args of [['--base-branch', '-x'], ['--base-branch', 'a..b'], ['--base-branch'], ['--other', 'x']]) {
    const r = run(repo(based), {}, args);
    assert.equal(r.out, 'measure=true\n', args.join(' '));
    assert.match(r.reason, /usage/);
  }
});
