import { test } from 'node:test';
import assert from 'node:assert/strict';
import { diffNameStatus, lastChanged, commitsBetween } from '../lib/git.mjs';
import { loadTierMap } from '../lib/tiers.mjs';
import { pathChanges } from '../lib/changes.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

// One upstream commit that adds, renames, deletes and modifies files.
function churn(f) {
  return f.commit({
    'src/Lib/Yoga/Edge.cs': 'class Edge {}\n',                  // added, tier 1 by glob
    'src/Lib/Core/Fresh.cs': 'class Fresh {}\n',                // added, no rule matches
    'src/Lib/Elements/Text.cs': null,                           // deleted, tier 3
    'src/Lib/Elements/Label.cs': 'class Text {}\n',             // ... and this is its rename
    'src/Lib/Elements/Button.cs': null,                         // deleted, tier 3
    'src/Other/Thing.cs': 'class Thing { int x; }\n',           // modified: not listed
    'docs/readme.md': null,                                     // deleted outside the watched roots: not listed
  }, 'Reshuffle (#2)');
}

test('added, renamed and deleted paths are detected and classified', () => {
  const f = makeFixture(SAMPLE);
  try {
    const next = churn(f);
    const changes = pathChanges(loadTierMap(SAMPLE.tiers), diffNameStatus(f.up, f.base, next), ['src/']);
    const key = (c) => c.change + ' ' + (c.from ? c.from + ' -> ' : '') + c.path + ' ' + (c.fromTier ? c.fromTier + ' -> ' : '') + c.tier;
    assert.deepEqual(changes.map(key).sort(), [
      'added src/Lib/Core/Fresh.cs unclassified',
      'added src/Lib/Yoga/Edge.cs 1',
      'deleted src/Lib/Elements/Button.cs 3',
      'renamed src/Lib/Elements/Text.cs -> src/Lib/Elements/Label.cs 3 -> 3',
    ]);
  } finally { f.cleanup(); }
});

// Adds an entry to the fixture's upstream index without a working-tree file, so names Windows can't hold work too.
function addEntry(f, mode, sha, p) {
  f.git('-c', 'core.protectNTFS=false', 'update-index', '--add', '--cacheinfo', mode + ',' + sha + ',' + p);
}

test('paths git would quote (a double quote in the name) are read unquoted from git log', () => {
  const f = makeFixture(SAMPLE);
  try {
    const blob = f.git('hash-object', '-w', '--stdin', '--path', 'x.cs');
    addEntry(f, '100644', blob, 'src/Lib/Yoga/Odd"Name.cs');
    f.git('commit', '-q', '-m', 'Odd name (#6)');
    const next = f.git('rev-parse', 'HEAD');
    assert.equal(lastChanged(f.up, next, ['src/Lib/Yoga/Odd"Name.cs', 'src/Lib/Core/Plain.cs']).get('src/Lib/Yoga/Odd"Name.cs'), next);
    assert.deepEqual(commitsBetween(f.up, f.base, next).map((c) => [c.subject, c.paths]), [['Odd name (#6)', ['src/Lib/Yoga/Odd"Name.cs']]]);
  } finally { f.cleanup(); }
});

test('a submodule or link under a vendored glob is an error, not a crash', () => {
  const f = makeFixture(SAMPLE);
  try {
    addEntry(f, '160000', f.base, 'src/Lib/Yoga/Sub');
    const blob = f.git('hash-object', '-w', '--stdin', '--path', 'x');
    addEntry(f, '120000', blob, 'src/Lib/Yoga/Link.cs');
    f.git('commit', '-q', '-m', 'Odd entries (#7)');
    const r = f.sync(f.git('rev-parse', 'HEAD'));
    assert.equal(r.code, 1);
    assert.match(r.err, /src\/Lib\/Yoga\/Link\.cs is not a regular file \(git mode 120000\)/);
    assert.doesNotMatch(r.err, /Sub is not/, 'Sub matches no *.cs glob, so it is not vendored');
  } finally { f.cleanup(); }
});

test('a vendored file renamed upstream fails the sync with the rename named', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const next = f.commit({ 'src/Lib/Core/Plain.cs': null, 'src/Lib/Core/Simple.cs': 'class Plain {}\n' }, 'Rename Plain (#3)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /src\/Lib\/Core\/Plain\.cs, tier 1\) matches no file/);
    assert.match(r.err, /src\/Lib\/Core\/Plain\.cs was renamed upstream to src\/Lib\/Core\/Simple\.cs/);
  } finally { f.cleanup(); }
});

test('a vendored file deleted upstream fails the sync with the deletion named', () => {
  const f = makeFixture(SAMPLE);
  try {
    const next = f.commit({ 'src/Lib/Core/Plain.cs': null }, 'Remove Plain (#4)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /src\/Lib\/Core\/Plain\.cs was deleted upstream/);
  } finally { f.cleanup(); }
});

test('the report lists the path changes, and new files under a tier 1 glob are vendored', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const next = churn(f);
    const r = f.sync(next);
    assert.equal(r.code, 0, r.err);
    const report = f.read('docs/sync/' + f.base.slice(0, 8) + '-' + next.slice(0, 8) + '.md');
    assert.match(report, /\| added \| \x60src\/Lib\/Core\/Fresh\.cs\x60 \| \*\*unclassified\*\* \|/);
    assert.match(report, /\| added \| \x60src\/Lib\/Yoga\/Edge\.cs\x60 \| tier 1 \|/);
    assert.match(report, /\| renamed \| \x60src\/Lib\/Elements\/Text\.cs\x60 → \x60src\/Lib\/Elements\/Label\.cs\x60 \| tier 3 → tier 3 \|/);
    assert.match(report, /\| deleted \| \x60src\/Lib\/Elements\/Button\.cs\x60 \| tier 3 \|/);
    assert.match(report, /Unclassified at [0-9a-f]{8}:\*\* 1 files/);
    assert.ok(f.exists('src/Upstream/src/Lib/Yoga/Edge.cs'));
  } finally { f.cleanup(); }
});
