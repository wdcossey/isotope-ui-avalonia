// The Roslyn step (issue #4): tools/sync/roslyn run through lib/roslyn.mjs, and "roslyn" rules in a sync. Needs dotnet.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { runRoslyn } from '../lib/roslyn.mjs';
import { loadTransforms, applyRoslyn } from '../lib/transforms.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

const one = (text, ...ops) => runRoslyn([{ path: 'f.cs', text, ops: ops.map((o, i) => ({ id: 'r' + i, ...o })) }])[0];

const CLASS = [
  'namespace N;',
  'public class Host',
  '{',
  '    // a plain comment stays',
  '    /// <summary>Gone.</summary>',
  '    [Obsolete("x")]',
  '    public void Hook(Microsoft.UI.Xaml.Window w)',
  '    {',
  '        Use("}");',
  '    }',
  '',
  '    public void Hook() => Hook(null);',
  '    public int Kept { get; } = 1;',
  '    private sealed class Inner { public void Hook() {} }',
  '}',
  '',
].join('\r\n');

test('drop-member blanks one member, its doc comment and attributes, keeping every line', () => {
  const r = one(CLASS, { op: 'drop-member', type: 'Host', member: 'Hook', parameters: '(Microsoft.UI.Xaml.Window)' });
  assert.deepEqual(r.results, [{ id: 'r0', hits: 1, error: null, info: null, changed: true }]);
  assert.deepEqual(r.errors, []);
  const lines = r.text.split('\r\n');
  assert.equal(lines.length, CLASS.split('\r\n').length);
  assert.deepEqual(lines.slice(3, 10), ['    // a plain comment stays', '', '', '', '', '', '']);
  assert.equal(lines[11], '    public void Hook() => Hook(null);');
  assert.match(r.text, /private sealed class Inner \{ public void Hook\(\) \{\} \}/);
});

test('drop-member fails loudly on a missing member, an ambiguous name, and code sharing its lines', () => {
  const r = one(CLASS,
    { op: 'drop-member', type: 'Host', member: 'Gone' },
    { op: 'drop-member', type: 'Host', member: 'Hook' },
    { op: 'drop-member', type: 'Inner', member: 'Hook' },
    { op: 'drop-member', type: 'Host.Inner', member: 'Hook', parameters: '()' });
  assert.match(r.results[0].error, /matched 0 members, expected 1 \(Host\.Gone\)/);
  assert.match(r.results[1].error, /matched 2 members, expected 1/);
  assert.match(r.results[2].error, /code precedes the member on its first line/);
  assert.match(r.results[3].error, /code precedes the member/);
  assert.equal(r.text, CLASS, 'a failed op leaves the text as it was');
});

test('retarget-type rewrites references in code, never in comments or strings', () => {
  const text = [
    'using WinUI = Microsoft.UI.Xaml.Controls;',
    '/// <see cref="WinUI.Border"/>',
    'class C {',
    '    // WinUI.Border in a comment',
    '    string s = "WinUI.Border";',
    '    bool A(object o) => o is WinUI.Border b && typeof(global::WinUI.Border) != null && Is<WinUI.Border>(o);',
    '    object P => WinUI.Border.BackgroundProperty;',
    '    object Q => x.WinUI.Border;',
    '}',
  ].join('\n');
  const r = one(text, { op: 'retarget-type', from: 'WinUI.Border', to: 'Avalonia.Controls.Border' });
  assert.equal(r.results[0].hits, 4);
  assert.equal(r.text, text
    .replace('o is WinUI.Border b && typeof(global::WinUI.Border) != null && Is<WinUI.Border>(o)',
      'o is Avalonia.Controls.Border b && typeof(Avalonia.Controls.Border) != null && Is<Avalonia.Controls.Border>(o)')
    .replace('WinUI.Border.BackgroundProperty', 'Avalonia.Controls.Border.BackgroundProperty'));
  const miss = one(text, { op: 'retarget-type', from: 'WinUI.Grid', to: 'Grid' });
  assert.match(miss.results[0].error, /no longer matches: no reference to WinUI\.Grid in code/);
});

test('drop-attribute removes an attribute or its whole list, and fails when there is none', () => {
  const text = [
    '[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(int))]',
    'class C {',
    '    [Obsolete, WinRT.GeneratedWinRTExposedExternalType]',
    '    void M() {}',
    '    [WinRT.GeneratedWinRTExposedExternalTypeAttribute] void N() {}',
    '}',
  ].join('\n');
  const r = one(text, { op: 'drop-attribute', attribute: 'GeneratedWinRTExposedExternalType' });
  assert.equal(r.results[0].hits, 3);
  assert.equal(r.text, ['', 'class C {', '    [Obsolete]', '    void M() {}', '    void N() {}', '}'].join('\n'));
  const miss = one(text, { op: 'drop-attribute', attribute: 'Serializable' });
  assert.match(miss.results[0].error, /no longer matches: no attribute Serializable in code/);
});

test('a transform that leaves a new syntax error is reported', () => {
  const r = runRoslyn([{ path: 'f.cs', text: 'class C { void M() { } }', original: 'class C { void M() { } }\n}', ops: [] }])[0];
  assert.deepEqual(r.errors, [], 'an error upstream already had is not new');
  const bad = runRoslyn([{ path: 'f.cs', text: 'class C { void M() { }', original: 'class C { void M() { } }', ops: [] }])[0];
  assert.match(bad.errors[0], /the transforms leave 1 new syntax error\(s\), the first: line 1: CS1513/);
});

test('roslyn rules list their files, take known ops and fields, and come before every other rule', () => {
  const ok = { id: 'r', roslyn: { op: 'drop-member', type: 'T', member: 'M' }, files: ['a.cs'] };
  assert.equal(loadTransforms({ rules: [ok] })[0].kind, 'roslyn');
  const bad = (r, re) => assert.throws(() => loadTransforms({ rules: [r] }), re);
  bad({ ...ok, files: undefined, glob: '**/*.cs' }, /a "roslyn" rule lists the files it changes/);
  bad({ ...ok, roslyn: { op: 'rename' } }, /"roslyn" op must be one of drop-member, retarget-type, drop-attribute/);
  bad({ ...ok, roslyn: { op: 'drop-member' } }, /drop-member needs "member"/);
  bad({ ...ok, roslyn: { op: 'drop-member', member: 'M', from: 'x' } }, /drop-member takes no "from"/);
  bad({ ...ok, find: 'x', replace: 'y' }, /exactly one of "find" or "drop" \(or "roslyn"\)/);
  assert.throws(() => loadTransforms({ rules: [{ id: 'f', find: 'a', replace: 'b', files: ['a.cs'] }, ok] }),
    /transform r: "roslyn" rules come before every other rule \(f precedes it\)/);
});

// A sync whose tier 2 file takes Roslyn rules, with a text rule after them.
const ROSLYN = {
  ...SAMPLE,
  files: { ...SAMPLE.files, 'src/Lib/Core/Engine.cs': 'class Engine\n{\n    Microsoft.UI.Xaml.UIElement e;\n    /// <summary>WinUI only.</summary>\n    public void Window() {}\n}\n' },
  transforms: {
    rules: [
      { id: 'drop-window', roslyn: { op: 'drop-member', type: 'Engine', member: 'Window' }, files: ['src/Lib/Core/Engine.cs'] },
      { id: 'retarget-element', roslyn: { op: 'retarget-type', from: 'Microsoft.UI.Xaml.UIElement', to: 'Avalonia.Controls.Control' }, files: ['src/Lib/Core/Engine.cs'] },
      { id: 'rename', find: '\\bEngine\\b', replace: 'Motor', files: ['src/Lib/Core/Engine.cs'] },
    ],
  },
};

test('a sync runs the Roslyn rules first, then the text rules, and names them all in the header', () => {
  const f = makeFixture(ROSLYN);
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
    const text = f.read('src/Upstream/src/Lib/Core/Engine.cs');
    assert.match(text, /; transforms: drop-window, retarget-element, rename\. Do not edit/);
    assert.equal(text.slice(text.indexOf('\n') + 1), 'class Motor\n{\n    Avalonia.Controls.Control e;\n\n\n}\n');
    assert.equal(f.sync('--check').code, 0);
  } finally { f.cleanup(); }
});

test('a sync fails loudly, writing nothing, when upstream removes a Roslyn rule\'s target', () => {
  const f = makeFixture(ROSLYN);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const before = f.read('src/Upstream/src/Lib/Core/Engine.cs');
    const next = f.commit({ 'src/Lib/Core/Engine.cs': 'class Engine\n{\n    Microsoft.UI.Xaml.UIElement e;\n}\n' }, 'Drop the window (#9)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /transform drop-window \(roslyn drop-member\) matched 0 members, expected 1 \(Engine\.Window\) in src\/Lib\/Core\/Engine\.cs: fix the rule/);
    assert.equal(f.read('src/Upstream/src/Lib/Core/Engine.cs'), before);
  } finally { f.cleanup(); }
});

test('with syntaxCheck, a text rule that breaks a C# file fails the sync', () => {
  const broken = { ...SAMPLE, transforms: { rules: [{ id: 'eat-brace', find: 'e; \\}', replace: 'e;', files: ['src/Lib/Core/Engine.cs'] }] } };
  const f = makeFixture(broken);
  try {
    assert.equal(f.sync(f.base).code, 0, 'off by default');
    f.put('tools/sync/sync.json', JSON.stringify({ vendorDir: 'src/Upstream', reportDir: 'docs/sync', watch: ['src/'], verify: [], syntaxCheck: true }));
    const r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /src\/Lib\/Core\/Engine\.cs: the transforms leave 1 new syntax error\(s\), the first: line 1: CS1513/);
  } finally { f.cleanup(); }
});

test('drop-attribute removes adjacent matches in one list with their separators', () => {
  const r = one('class C {\n    [Obsolete, Foo, Bar.Foo] void M() {}\n    [Foo, Foo, Obsolete, FooAttribute] void N() {}\n    [Obsolete, Foo, Serializable] void P() {}\n}\n',
    { op: 'drop-attribute', attribute: 'Foo' });
  assert.equal(r.results[0].error, null);
  assert.equal(r.results[0].hits, 6);
  assert.equal(r.text, 'class C {\n    [Obsolete] void M() {}\n    [Obsolete] void N() {}\n    [Obsolete, Serializable] void P() {}\n}\n');
});

test('code under #if DEBUG is transformed; a target in code an #if leaves out fails the op', () => {
  const text = 'class C {\n#if DEBUG\n    WinUI.Border a;\n#else\n    WinUI.Border b;\n#endif\n    WinUI.Border c;\n}\n';
  const r = one(text, { op: 'retarget-type', from: 'WinUI.Border', to: 'Border' });
  assert.match(r.results[0].error, /Border also appears in code an #if leaves out \(line 5\), which retarget-type can't see: use a text rule/);
  const ok = one(text.replace('    WinUI.Border b;\n', '    int b;\n'), { op: 'retarget-type', from: 'WinUI.Border', to: 'Border' });
  assert.equal(ok.results[0].hits, 2);
  assert.equal(ok.text, 'class C {\n#if DEBUG\n    Border a;\n#else\n    int b;\n#endif\n    Border c;\n}\n');
});

test('drop-member names a field declared with others, rather than reporting it missing', () => {
  const r = one('class Host {\n    int a, b;\n}\n', { op: 'drop-member', type: 'Host', member: 'b' });
  assert.match(r.results[0].error, /b is declared with other fields in one declaration/);
});

test('a Roslyn rule that changes nothing is not named as applied', () => {
  const rules = loadTransforms({ rules: [
    { id: 'same', roslyn: { op: 'retarget-type', from: 'A.B', to: 'A.B' }, files: ['f.cs'] },
    { id: 'real', roslyn: { op: 'retarget-type', from: 'A.B', to: 'C' }, files: ['f.cs'] },
  ] });
  const out = applyRoslyn(rules, [{ p: 'f.cs', tier: 2, text: 'class X { A.B y; }' }]).get('f.cs');
  assert.deepEqual(out.errors, []);
  assert.deepEqual(out.applied.map((r) => r.id), ['real']);
  assert.equal(out.text, 'class X { C y; }');
});

test('the syntax check compares error codes, so an upstream error that moves a line is not new', () => {
  const original = 'class C { void M() { } }\n}\n';
  const r = runRoslyn([{ path: 'f.cs', text: '\n\n' + original, original, ops: [] }])[0];
  assert.deepEqual(r.errors, []);
});
