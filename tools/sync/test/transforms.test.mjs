import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadTransforms, applyTransforms, checkTransformTargets, transformFile, rulesFor } from '../lib/transforms.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

test('a transform rewrites with its groups and counts hits', () => {
  const [rule] = loadTransforms(SAMPLE.transforms);
  const r = applyTransforms([rule], 'f.cs', 'a Microsoft.UI.Xaml.UIElement b Microsoft.UI.Xaml.UIElement');
  assert.equal(r.text, 'a UIElement b UIElement');
  assert.deepEqual(r.hits, { 'drop-ns': 2 });
  assert.deepEqual(r.errors, []);
});

test('a transform that no longer matches a listed file is an error', () => {
  const [rule] = loadTransforms(SAMPLE.transforms);
  const r = applyTransforms([rule], 'src/Lib/Core/Engine.cs', 'class Engine { UIElement e; }');
  assert.equal(r.errors.length, 1);
  assert.match(r.errors[0], /transform drop-ns no longer matches src\/Lib\/Core\/Engine\.cs/);
});

test('transform targets must exist and be tier 2; bad rules are rejected', () => {
  const rules = loadTransforms({ rules: [{ id: 'x', find: 'a', replace: 'b', files: ['one.cs', 'two.cs'] }] });
  const errors = checkTransformTargets(rules, new Map([['one.cs', { tier: 1 }]]), new Set(['one.cs']));
  assert.equal(errors.length, 2);
  assert.match(errors[0], /one\.cs, which is tier 1/);
  assert.match(errors[1], /two\.cs, which is not in upstream/);
  assert.match(checkTransformTargets(rules, new Map(), new Set(['one.cs']))[0], /one\.cs, which matches no tier map rule/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', find: '(', replace: '', files: ['a'] }] }), /bad regex/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', find: 'a', replace: '', files: [] }] }), /must list/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', Find: 'a', replace: '', files: ['a'] }] }), /exactly one of "find" or "drop"/);
});

test('replacements: $$ is a literal $, and $0 or a group "find" lacks is rejected', () => {
  const [r] = loadTransforms({ rules: [{ id: 'x', find: '(a)', replace: '$$$1', files: ['f'] }] });
  assert.equal(applyTransforms([r], 'f', 'bab').text, 'b$ab');
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', find: '(a)', replace: '$0', files: ['f'] }] }), /uses \$0/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', find: '(a)', replace: '$2', files: ['f'] }] }), /uses \$2, but "find" has 1 groups/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', find: '(a)', replace: '$10', files: ['f'] }] }), /uses \$10/);
});

test('the tracked notice transform keeps Yoga and drops the WinUIEx section', () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const rules = loadTransforms(JSON.parse(readFileSync(path.join(here, '..', 'transforms.json'), 'utf8')));
  const rule = rules.find((r) => r.id === 'drop-winuiex-notice');
  const dash = '-'.repeat(54);
  const text = 'Head\n\n\n--- Yoga ---\nMIT\nCopyright (c) Meta\n' + dash + '\n\n\n------------------- WinUIEx -------------------------\nMIT\nCopyright (c) A Person\n' + dash;
  const r = applyTransforms([rule], 'n.txt', text);
  assert.deepEqual(r.errors, []);
  assert.equal(r.text, 'Head\n\n\n--- Yoga ---\nMIT\nCopyright (c) Meta\n' + dash);
});

test('sync fails when a rule now matches a vendored file it does not list', () => {
  const f = makeFixture(SAMPLE);
  try {
    const next = f.commit({ 'src/Lib/Core/Plain.cs': 'class Plain { Microsoft.UI.Xaml.UIElement e; }\n' }, 'Use UIElement (#5)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /transform drop-ns matches src\/Lib\/Core\/Plain\.cs, which it does not list/);
  } finally { f.cleanup(); }
});

test('a transform that matches an empty string is an error, not a silent rewrite', () => {
  const rules = loadTransforms({ rules: [{ id: 'x', find: 'z*', replace: 'Q', files: ['f.cs'] }] });
  const r = applyTransforms(rules, 'f.cs', 'abc');
  assert.match(r.errors[0], /transform x matches an empty string in f\.cs/);
});

test('sync applies transforms to tier 2 files and names them in the header', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const text = f.read('src/Upstream/src/Lib/Core/Engine.cs');
    assert.match(text, /; transforms: drop-ns\. Do not edit/);
    assert.match(text, /\nclass Engine \{ UIElement e; \}\n$/);
  } finally { f.cleanup(); }
});

test('sync fails loudly, writing nothing, when upstream changes so a transform stops matching', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const before = f.read('src/Upstream/src/Lib/Core/Engine.cs');
    const next = f.commit({ 'src/Lib/Core/Engine.cs': 'class Engine { Avalonia.Controls.Control e; }\n' }, 'Rework the engine (#2)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /transform drop-ns no longer matches src\/Lib\/Core\/Engine\.cs: fix the rule/);
    assert.equal(f.read('src/Upstream/src/Lib/Core/Engine.cs'), before, 'the vendored file is untouched');
    assert.match(f.read('UPSTREAM.lock'), new RegExp('baseline=' + f.base), 'the lock is not bumped');
  } finally { f.cleanup(); }
});

test('a drop rule blanks one member of a listed file, and fails unless it finds exactly one', () => {
  const rules = loadTransforms({ rules: [{ id: 'd', drop: 'public void Gone\\(', files: ['a.cs'] }] });
  const src = 'class C\r\n{\r\n    /// doc\r\n    public void Gone() { }\r\n    int Kept;\r\n}\r\n';
  const r = applyTransforms(rules, 'a.cs', src);
  assert.deepEqual(r.errors, []);
  assert.deepEqual(r.hits, { d: 1 });
  assert.equal(r.text, 'class C\r\n{\r\n\r\n\r\n    int Kept;\r\n}\r\n');
  const none = applyTransforms(rules, 'a.cs', 'class C { }');
  assert.match(none.errors[0], /transform d \(drop\) matched 0 times in code, expected 1 in a\.cs/);
  assert.equal(none.text, 'class C { }');
  const twice = applyTransforms(rules, 'a.cs', 'class C\n{\n    public void Gone() { }\n    public void Gone(int a) { }\n}\n');
  assert.match(twice.errors[0], /matched 2 times/);
});

test('rule shapes are checked: "find" or "drop", "files" or "glob", and "flags"', () => {
  const bad = (rule, re) => assert.throws(() => loadTransforms({ rules: [{ id: 'x', ...rule }] }), re);
  bad({ find: 'a', drop: 'b', files: ['f'] }, /exactly one of "find" or "drop"/);
  bad({ drop: 'a', replace: '', files: ['f'] }, /"drop" rule takes no "replace"/);
  bad({ drop: 'a', glob: '**/*.cs' }, /"drop" rule lists its files/);
  bad({ find: 'a', replace: '', files: ['f'], glob: '**' }, /exactly one of "files" or "glob"/);
  bad({ find: 'a', replace: '' }, /exactly one of "files" or "glob"/);
  bad({ find: 'a', replace: '', glob: 'a**' }, /"\*\*" must be a whole path segment/);
  bad({ find: 'a', replace: '', flags: 'g', files: ['f'] }, /"flags" may hold i, m, s and u/);
  bad({ find: 'a', replace: '', flags: 'mm', files: ['f'] }, /"flags" may hold i, m, s and u, each once/);
});

test('"flags" reach the regex: with "m", ^ and $ anchor at each line', () => {
  const rule = { id: 'u', find: '^using X;\\r?$', replace: '', files: ['f.cs'] };
  const [m] = loadTransforms({ rules: [{ ...rule, flags: 'm' }] });
  assert.equal(applyTransforms([m], 'f.cs', 'class A;\r\nusing X;\r\nclass C {}').text, 'class A;\r\n\nclass C {}');
  const [plain] = loadTransforms({ rules: [rule] });
  assert.match(applyTransforms([plain], 'f.cs', 'class A;\r\nusing X;\r\nclass C {}').errors[0], /no longer matches f\.cs/);
});

test('the unlisted-file guard sees the text as transformed so far, so a more specific earlier rule satisfies it', () => {
  const rules = loadTransforms({ rules: [
    { id: 'specific', find: 'A\\.B\\.C', replace: 'X', files: ['f.cs'] },
    { id: 'general', find: 'A\\.B', replace: 'Y', files: ['other.cs'] },
  ] });
  assert.deepEqual(transformFile(rules, 'f.cs', 2, 'A.B.C').errors, []);
  assert.equal(transformFile(rules, 'f.cs', 2, 'A.B.C').text, 'X');
  assert.match(transformFile(rules, 'f.cs', 2, 'A.B.C A.B').errors[0], /transform general matches f\.cs, which it does not list/);
  // Listed rules apply to tier 2 files only; glob rules to any vendored tier.
  const g = loadTransforms({ rules: [{ id: 'l', find: 'a', replace: 'b', files: ['f.cs'] }, { id: 'g', find: 'a', replace: 'b', glob: '*.cs' }] });
  assert.deepEqual(rulesFor(g, 'f.cs', 1).map((r) => r.id), ['g']);
  assert.deepEqual(rulesFor(g, 'f.cs', 2).map((r) => r.id), ['l', 'g']);
});

test('a glob rule changes tier 1 and 2 files wherever it matches, and only they name it in their header', () => {
  const transforms = { rules: [...SAMPLE.transforms.rules, { id: 'ns', find: '\\bnamespace Y\\b', replace: 'namespace Z', glob: 'src/Lib/**/*.cs' }] };
  const f = makeFixture({ ...SAMPLE, transforms });
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
    const node = f.read('src/Upstream/src/Lib/Yoga/Node.cs');
    assert.match(node, /; transforms: ns\. Do not edit/);
    assert.match(node, /\nnamespace Z;\nclass Node \{\}\n$/);
    assert.doesNotMatch(f.read('src/Upstream/src/Lib/Core/Plain.cs'), /transforms:/);
    assert.match(f.read('src/Upstream/src/Lib/Core/Engine.cs'), /; transforms: drop-ns\. Do not edit/);
    const manifest = JSON.parse(f.read('src/Upstream/vendored.json'));
    assert.deepEqual(manifest.files.find((x) => x.path === 'src/Lib/Yoga/Node.cs').transforms, ['ns']);
    assert.deepEqual(manifest.files.find((x) => x.path === 'src/Lib/Core/Plain.cs').transforms, []);
  } finally { f.cleanup(); }
});

test('a glob rule that changes no vendored file fails the sync, even if it matches files that are not vendored', () => {
  const transforms = { rules: [...SAMPLE.transforms.rules, { id: 'idle', find: 'class Button', replace: 'class B', glob: 'src/Lib/**/*.cs' }] };
  const f = makeFixture({ ...SAMPLE, transforms });
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 1);
    assert.match(r.err, /transform idle \(glob src\/Lib\/\*\*\/\*\.cs\) changes no vendored file/);
  } finally { f.cleanup(); }
});

test('the tracked namespace and partial rules rename Microsoft.UI.Reactor and make RenderContext partial, nothing else', () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const rules = loadTransforms(JSON.parse(readFileSync(path.join(here, '..', 'transforms.json'), 'utf8')));
  const ns = rules.find((r) => r.id === 'namespace-isotope');
  const partial = rules.find((r) => r.id === 'partial-render-context');
  assert.equal(rules.at(-1), ns, 'the rename runs last, so every other rule is written against upstream text');
  const src = 'using Microsoft.UI.Reactor.Core;\nnamespace Microsoft.UI.Reactor.Hooks;\n/// <see cref="Microsoft.UI.Reactor.ReactorWindow"/>\n' +
    'public sealed class RenderContext { global::Microsoft.UI.Reactor.X x; Microsoft.UI.Xaml.Window w; Microsoft.UI.ReactorX y; Microsoft.UI.Reactor z; }';
  const r = applyTransforms([partial, ns], 'src/Reactor/Core/RenderContext.cs', src);
  assert.deepEqual(r.errors, []);
  assert.equal(r.text, 'using Isotope.UI.Core;\nnamespace Isotope.UI.Hooks;\n/// <see cref="Isotope.UI.ReactorWindow"/>\n' +
    'public sealed partial class RenderContext { global::Isotope.UI.X x; Microsoft.UI.Xaml.Window w; Microsoft.UI.ReactorX y; Isotope.UI z; }');
  for (const p of ['src/Reactor/Core/RenderContext.cs', 'tests/Reactor.Tests/YogaCacheTests.cs']) assert.ok(ns.globRe.test(p), p);
  for (const p of ['LICENSE', 'src/Reactor/ThirdPartyNoticeText.txt']) assert.ok(!ns.globRe.test(p), p);
});

test('glob rules come after every listed rule, so none can hide new upstream code from the unlisted-file guard', () => {
  const listed = { id: 'l', find: 'a', replace: 'b', files: ['f.cs'] };
  const glob = { id: 'g', find: 'c', replace: 'd', glob: '**/*.cs' };
  assert.throws(() => loadTransforms({ rules: [glob, listed] }), /transform g: glob rules come after every listed rule \(l follows it\)/);
  assert.deepEqual(loadTransforms({ rules: [listed, glob] }).map((r) => r.id), ['l', 'g']);
});

test('a rule whose replacement equals its match changes nothing, so it is not named as applied', () => {
  const rules = loadTransforms({ rules: [{ id: 'same', find: 'Isotope\\.UI', replace: 'Isotope.UI', glob: '*.cs' }, { id: 'real', find: 'x', replace: 'y', glob: '*.cs' }] });
  const r = transformFile(rules, 'f.cs', 1, 'Isotope.UI x');
  assert.equal(r.text, 'Isotope.UI y');
  assert.deepEqual(r.applied.map((x) => x.id), ['real']);
});
