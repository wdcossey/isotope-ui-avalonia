// Tests of the samples baseline (#71): node --test tools/migration/test/. Made-up counts, so no dotnet is needed, and
// checks that the committed baseline, UPSTREAM.lock and the migration guide agree.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { GUIDE_END, GUIDE_START, baselineOf, compare, countsOf, withGuideTable } from '../lib/baseline.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const UPSTREAM = 'a'.repeat(40);

// A summary as measure-samples.mjs writes it (the fields the baseline reads).
function summary(over = {}) {
  return {
    projects: 36, files: 358, projectsBuildBefore: 1, projectsBuild: 2, filesBuildBefore: 51, filesBuildAfter: 108,
    errorsBefore: 13699, errorsAfter: 2936, brokenByMigration: [], formatFailures: 0,
    migrationLeft: { ISOMIG013: 401, ISOMIG002: 199, ISOMIG006: 0 },
    migrationBefore: { ISOMIG001: 900 }, migrationMessagesLeft: { 'a message': 3 }, errorsLeft: { 'CS0103 Brush': 91 },
    ...over,
  };
}
const baseline = (over) => baselineOf(countsOf(summary(over)), UPSTREAM);

test('the counts: the summary totals, broken files counted, migration diagnostics per ID without zeros, sorted', () => {
  const c = countsOf(summary({ brokenByMigration: ['apps/a/A.cs', 'apps/b/B.cs'], formatFailures: undefined }));
  assert.equal(c.brokenByMigration, 2);
  assert.equal(c.formatFailures, 0);
  assert.deepEqual(Object.entries(c.migrationLeft), [['ISOMIG002', 199], ['ISOMIG013', 401]]);
  assert.equal(c.migrationLeftTotal, 600);
  assert.equal(c.errorsAfter, 2936);
  // Per-message counts and compile errors per ID are not gated.
  assert.equal(JSON.stringify(c).includes('a message'), false);
  assert.equal(JSON.stringify(c).includes('CS0103'), false);
});

test('the same counts: no difference', () => {
  assert.deepEqual(compare(baseline(), countsOf(summary()), UPSTREAM), { worse: [], better: [], changed: [] });
});

test('worse: fewer files or projects that build, more errors, broken files, failed format runs, more diagnostics of an ID', () => {
  const { worse, better, changed } = compare(baseline(), countsOf(summary({
    filesBuildAfter: 107, projectsBuild: 1, errorsAfter: 2940, brokenByMigration: ['apps/a/A.cs'], formatFailures: 1,
    migrationLeft: { ISOMIG013: 402, ISOMIG002: 199 },
  })), UPSTREAM);
  assert.deepEqual(worse, [
    'projects that build after the fixes: 2 -> 1 (-1)',
    'files that build after the fixes: 108 -> 107 (-1)',
    'compile errors after the fixes: 2,936 -> 2,940 (+4)',
    'files that built before the fixes and not after: 0 -> 1 (+1)',
    'dotnet format runs that failed: 0 -> 1 (+1)',
    'migration diagnostics left: 600 -> 601 (+1)',
    'ISOMIG013 left: 401 -> 402 (+1)',
  ]);
  assert.deepEqual(better, []);
  assert.deepEqual(changed, []);
});

test('an ID that was not left before is worse, and one no longer left is better', () => {
  const { worse, better } = compare(baseline(), countsOf(summary({ migrationLeft: { ISOMIG013: 401, ISOMIG007: 199 } })), UPSTREAM);
  assert.deepEqual(worse, ['ISOMIG007 left: 0 -> 199 (+199)']);
  assert.deepEqual(better, ['ISOMIG002 left: 199 -> 0 (-199)']);
});

test('better: more files that build, fewer errors before and after; each is reported, so the baseline is updated', () => {
  const { worse, better } = compare(baseline(), countsOf(summary({ filesBuildAfter: 110, errorsAfter: 2900, errorsBefore: 13000, filesBuildBefore: 52 })), UPSTREAM);
  assert.deepEqual(worse, []);
  assert.deepEqual(better, [
    'files that build before the fixes: 51 -> 52 (+1)',
    'files that build after the fixes: 108 -> 110 (+2)',
    'compile errors before the fixes: 13,699 -> 13,000 (-699)',
    'compile errors after the fixes: 2,936 -> 2,900 (-36)',
  ]);
});

test('another sample set or upstream commit is a change, neither worse nor better', () => {
  const { worse, better, changed } = compare(baseline(), countsOf(summary({ files: 360, projects: 37 })), 'b'.repeat(40));
  assert.deepEqual(worse, []);
  assert.deepEqual(better, []);
  assert.deepEqual(changed, [
    'the baseline was measured on upstream aaaaaaaa, and UPSTREAM.lock is bbbbbbbb',
    'sample projects measured: 36 -> 37',
    'C# files measured: 358 -> 360',
  ]);
});

test('a baseline missing a count is a change, not a pass', () => {
  const b = baseline();
  delete b.counts.errorsAfter;
  assert.deepEqual(compare(b, countsOf(summary()), UPSTREAM).changed, ['compile errors after the fixes: undefined -> 2,936']);
});

test('the guide block is replaced between its markers, keeping CRLF line endings', () => {
  const guide = ['# Guide', GUIDE_START, 'old', GUIDE_END, 'after', ''].join('\r\n');
  const result = withGuideTable(guide, countsOf(summary()));
  assert.ok(result.startsWith('# Guide\r\n' + GUIDE_START + '\r\n| | Before the fixes | After |\r\n'));
  assert.ok(result.endsWith('| All | 600 |\r\n' + GUIDE_END + '\r\nafter\r\n'));
  assert.ok(result.includes('| Files that build | 51 of 358 | 108 of 358 |\r\n'));
  assert.ok(result.includes('| Compile errors | 13,699 | 2,936 |\r\n'));
  assert.ok(result.includes('| ISOMIG002 | 199 |\r\n| ISOMIG013 | 401 |\r\n'));
  assert.equal(withGuideTable(result, countsOf(summary())), result);
});

test('a guide without both markers, in order and once each, is refused', () => {
  for (const guide of ['no markers', GUIDE_START + '\nx', GUIDE_END + '\n' + GUIDE_START, GUIDE_START + GUIDE_END + GUIDE_START + GUIDE_END])
    assert.throws(() => withGuideTable(guide, countsOf(summary())), /the guide needs one/);
});

// ── the committed files ──

const read = (...p) => fs.readFileSync(path.join(repo, ...p), 'utf8');
const committed = JSON.parse(read('tools', 'migration', 'samples-baseline.json'));
const lock = read('UPSTREAM.lock').match(/^baseline=([0-9a-f]{40})\s*$/m)?.[1];

test('the baseline is for the upstream commit in UPSTREAM.lock', () => {
  assert.equal(committed.upstream, lock, 'UPSTREAM.lock moved: measure again and write the baseline (docs/guide/migrating-from-reactor.md)');
});

test('the migration guide shows the baseline counts', () => {
  const guide = read('docs', 'guide', 'migrating-from-reactor.md');
  assert.equal(guide.replace(/\r\n/g, '\n'), withGuideTable(guide, committed.counts).replace(/\r\n/g, '\n'),
    "the guide's counts are not the baseline's: run node tools/migration/measure-samples.mjs --out <empty folder> --write-baseline");
});

test('the baseline file is what --write-baseline writes for its counts', () => {
  assert.equal(read('tools', 'migration', 'samples-baseline.json').replace(/\r\n/g, '\n'), JSON.stringify(baselineOf(committed.counts, committed.upstream), null, 2) + '\n');
});

// ── the command line, on a summary file (no measuring) ──

function cli(counts, ...flags) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'isotope-baseline-'));
  try {
    const file = path.join(dir, 'summary.json');
    const s = { ...counts, brokenByMigration: Array.from({ length: counts.brokenByMigration }, (_, i) => 'f' + i + '.cs') };
    fs.writeFileSync(file, JSON.stringify({ results: [], summary: s }));
    const r = spawnSync(process.execPath, [path.join(repo, 'tools', 'migration', 'measure-samples.mjs'), '--summary', file, ...flags], { encoding: 'utf8' });
    return { status: r.status, out: r.stdout + r.stderr };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('--check on a summary with the baseline counts passes', () => {
  const r = cli(committed.counts, '--check');
  assert.equal(r.status, 0, r.out);
  assert.match(r.out, /match/);
});

test('--check fails naming a count that got worse, and asks for an update when one got better', () => {
  let r = cli({ ...committed.counts, errorsAfter: committed.counts.errorsAfter + 5 }, '--check');
  assert.equal(r.status, 1, r.out);
  assert.match(r.out, /worse: compile errors after the fixes: [\d,]+ -> [\d,]+ \(\+5\)/);
  r = cli({ ...committed.counts, filesBuildAfter: committed.counts.filesBuildAfter + 1 }, '--check');
  assert.equal(r.status, 1, r.out);
  assert.match(r.out, /better: files that build after the fixes/);
  assert.match(r.out, /--write-baseline/);
});

test('--write-baseline with the baseline counts writes nothing and passes', () => {
  const before = [read('tools', 'migration', 'samples-baseline.json'), read('docs', 'guide', 'migrating-from-reactor.md')];
  const r = cli(committed.counts, '--write-baseline');
  assert.equal(r.status, 0, r.out);
  assert.match(r.out, /up to date/);
  assert.deepEqual([read('tools', 'migration', 'samples-baseline.json'), read('docs', 'guide', 'migrating-from-reactor.md')], before);
});
