import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadTransforms, applyTransforms, checkTransformTargets } from '../lib/transforms.mjs';
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
  assert.throws(() => loadTransforms({ rules: [{ id: 'x', Find: 'a', replace: '', files: ['a'] }] }), /"find" must be/);
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
