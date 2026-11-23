import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { globToRegExp } from '../lib/glob.mjs';
import { loadTierMap, classify, classifyTree } from '../lib/tiers.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));

test('glob: * stays in one segment, ** spans segments', () => {
  assert.ok(globToRegExp('a/*.cs').test('a/x.cs'));
  assert.ok(!globToRegExp('a/*.cs').test('a/b/x.cs'));
  assert.ok(globToRegExp('a/**').test('a/b/c/x.cs'));
  assert.ok(globToRegExp('a/**/x.cs').test('a/x.cs'));
  assert.ok(globToRegExp('a/**/x.cs').test('a/b/c/x.cs'));
  assert.ok(!globToRegExp('a/**').test('ab/x.cs'));
  assert.ok(globToRegExp('a/Yoga*.cs').test('a/YogaNode.cs'));
  assert.ok(!globToRegExp('a.cs').test('aXcs'), 'a dot is literal');
  assert.throws(() => globToRegExp('a/b**'));
});

test('classify: first matching rule wins, and no rule means unclassified', () => {
  const map = loadTierMap(SAMPLE.tiers);
  assert.equal(classify(map, 'src/Lib/Yoga/Panel.cs').tier, 3, 'the exact tier 3 rule comes before the tier 1 glob');
  assert.equal(classify(map, 'src/Lib/Yoga/Node.cs').tier, 1);
  assert.equal(classify(map, 'src/Lib/Core/Engine.cs').tier, 2);
  assert.equal(classify(map, 'src/Lib/Elements/Deep/Grid.cs').tier, 3);
  assert.equal(classify(map, 'src/Other/Thing.cs').tier, 'none');
  assert.equal(classify(map, 'src/Lib/Core/New.cs'), null);
});

test('classifyTree: a rule that matches no file is an error (empty include)', () => {
  const map = loadTierMap({ rules: [...SAMPLE.tiers.rules, { path: 'src/Lib/Core/Gone.cs', tier: 1 }, { glob: 'src/Lib/Nope/*.cs', tier: 2 }] });
  const r = classifyTree(map, Object.keys(SAMPLE.files));
  assert.equal(r.errors.length, 2);
  assert.match(r.errors[0], /src\/Lib\/Core\/Gone\.cs.*matches no file/);
  assert.match(r.errors[1], /src\/Lib\/Nope\/\*\.cs.*matches no file/);
  assert.deepEqual(r.unclassified, ['docs/readme.md']);
});

test('classifyTree: an exact rule shadowed by an earlier rule is reported', () => {
  const map = loadTierMap({ rules: [{ glob: 'src/**', tier: 'none' }, { path: 'src/Lib/Core/Plain.cs', tier: 1 }] });
  const r = classifyTree(map, Object.keys(SAMPLE.files));
  assert.equal(r.errors.length, 1);
  assert.match(r.errors[0], /an earlier rule takes it/);
});

test('loadTierMap rejects bad rules', () => {
  assert.throws(() => loadTierMap({ rules: [{ path: 'a', glob: 'b', tier: 1 }] }), /exactly one/);
  assert.throws(() => loadTierMap({ rules: [{ path: 'a', tier: 4 }] }), /tier must be/);
  assert.throws(() => loadTierMap({ rules: [{ path: 'a\\b', tier: 1 }] }), /forward slashes/);
});

test('the tracked tier map classifies the Spike A and D files as seeded', () => {
  const map = loadTierMap(JSON.parse(readFileSync(path.join(here, '..', 'tiers.json'), 'utf8')));
  const tier = (p) => classify(map, p)?.tier ?? null;
  assert.equal(tier('src/Reactor/Yoga/YogaNode.cs'), 1);
  assert.equal(tier('src/Reactor/Yoga/FlexPanel.cs'), 3);
  assert.equal(tier('src/Reactor/Core/RenderContext.cs'), 2);
  assert.equal(tier('src/Reactor/Core/ChildReconciler.cs'), 2);
  assert.equal(tier('src/Reactor/Core/Internal/KeyedListDiff.cs'), 2);
  assert.equal(tier('src/Reactor/Core/Internal/ReactorListState.cs'), 2, 'compiles only after the mechanical rewrite');
  assert.equal(tier('src/Reactor/Data/RowKey.cs'), 1);
  assert.equal(tier('src/Reactor/Hooks/UseAnnounce.cs'), 3);
  assert.equal(tier('src/Reactor/Core/Element.cs'), 3);
  assert.equal(tier('src/Reactor/Hosting/Anything/New.cs'), 3);
  assert.equal(tier('tests/Reactor.Tests/YogaGenerated/YGAlignContentTest.cs'), 1);
  assert.equal(tier('tests/Reactor.Tests/YogaValueExtensionsTests.cs'), 'none');
  assert.equal(tier('src/Reactor/Core/SomethingNew.cs'), 'none');
  assert.equal(tier('docs/guide/x.md'), null);
});

test('sync fails, writing nothing, when an include matches nothing', () => {
  const f = makeFixture({ ...SAMPLE, tiers: { rules: [...SAMPLE.tiers.rules, { path: 'src/Lib/Core/Missing.cs', tier: 1 }] } });
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 1);
    assert.match(r.err, /src\/Lib\/Core\/Missing\.cs.*matches no file/);
    assert.ok(!f.exists('src/Upstream'), 'nothing vendored');
  } finally { f.cleanup(); }
});

test('sync vendors tier 1 and 2 files only, at their upstream paths', () => {
  const f = makeFixture(SAMPLE);
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
    for (const p of ['LICENSE', 'src/Lib/Yoga/Node.cs', 'src/Lib/Core/Engine.cs', 'src/Lib/Core/Plain.cs']) assert.ok(f.exists('src/Upstream/' + p), p);
    for (const p of ['src/Lib/Yoga/Panel.cs', 'src/Lib/Elements/Button.cs', 'src/Other/Thing.cs', 'docs/readme.md']) assert.ok(!f.exists('src/Upstream/' + p), p);
    const manifest = JSON.parse(f.read('src/Upstream/vendored.json'));
    assert.deepEqual(manifest.files.map((x) => [x.path, x.tier]),
      [['LICENSE', 1], ['src/Lib/Core/Engine.cs', 2], ['src/Lib/Core/Plain.cs', 1], ['src/Lib/Yoga/Node.cs', 1]]);
    assert.equal(f.read('src/Upstream/LICENSE'), 'MIT\n', 'non-C# files are copied byte for byte, with no header');
    assert.match(f.read('src/Upstream/src/Lib/Yoga/Node.cs'), /^\/\/ Vendored by tools\/sync from upstream \(MIT\): src\/Lib\/Yoga\/Node\.cs, last changed upstream in [0-9a-f]{12}\. Do not edit/);
  } finally { f.cleanup(); }
});
