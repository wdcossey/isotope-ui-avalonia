// Tests for tools/sync/lib/csharp.mjs: the C# lexer and member drop shared by tools/sync and tools/rewrite-upstream.mjs.
// Run with the other sync tests: node tools/sync/test/all.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { codeMask, dropMember } from '../lib/csharp.mjs';

const codeText = (s) => { const m = codeMask(s); return [...s].filter((_, i) => m[i]).join(''); };
// Drops each member in turn, as a sync drop rule would; throws on the first error.
const drop = (src, ...res) => res.reduce((t, re) => {
  const d = dropMember(t, new RegExp(re, 'g'));
  if (d.error) throw new Error(d.error);
  return d.text;
}, src);

test('codeMask skips comments, strings, chars and verbatim strings', () => {
  const src = 'a // x {\n b /* { */ c "{\\"" d @"{""}" e \'{\' f @"\\" g';
  assert.equal(codeText(src).replace(/\s+/g, ''), 'abcdefg');
});

test('codeMask keeps interpolation holes as code, nested strings inside them as strings', () => {
  const src = '$"x{(y ? "}" : z)}w" q';
  assert.equal(codeText(src).replace(/\s+/g, ''), '(y?:z)q');
});

test('codeMask skips preprocessor lines', () => {
  assert.equal(codeText("#region Users' settings\na\n  #error \"b\nc").replace(/\s+/g, ''), 'ac');
});

test('codeMask: a format clause is not code, a raw string is not code', () => {
  assert.equal(codeText('$"{v:N2}" + """a " b { c""" + d').replace(/\s+/g, ''), 'v++d');
});

test('drop removes the member with its doc comment and attributes, keeping line count', () => {
  const src = [
    'class C',
    '{',
    '    /// <summary>doc</summary>',
    '    [Obsolete]',
    '    public void Gone(int a)',
    '    {',
    '        var s = "}";',
    '    }',
    '    public int Kept => 1;',
    '}',
  ].join('\r\n');
  const text = drop(src, 'public void Gone\\(');
  const lines = text.split('\r\n');
  assert.equal(lines.length, 10);
  assert.deepEqual(lines.slice(2, 8), ['', '', '', '', '', '']);
  assert.equal(lines[8], '    public int Kept => 1;');
});

test('drop handles expression bodies and field initializers with lambdas', () => {
  const src = 'class C {\n  int A() => B(() => { return 1; });\n  static readonly Lazy<int> L = new(() =>\n  {\n    return 2;\n  });\n  int Z;\n}';
  const text = drop(src, 'int A\\(', 'static readonly Lazy<int> L\\b');
  assert.equal(text.split('\n').length, 8);
  assert.match(text, /int Z;/);
  assert.doesNotMatch(text, /return 1|return 2/);
});

test('drop fails when the match starts mid-line', () => {
  assert.throws(() => drop('class C {\n  static int A() => 1;\n}', 'int A\\('), /code precedes/);
});

test('drop ignores a match inside a comment or string, and fails unless it matches exactly once', () => {
  const src = 'class C {\n  // void Gone() {}\n  string s = "void Gone()";\n}';
  assert.throws(() => drop(src, 'void Gone\\('), /matched 0 times/);
  assert.equal(dropMember(src, /void Gone\(/g).text, src, 'a failed drop leaves the text unchanged');
  const twice = 'class C {\n  void Gone() {}\n  void Gone() {}\n}';
  assert.throws(() => drop(twice, 'void Gone\\('), /matched 2 times/);
});

test('drop allows a trailing comment after the member, and skips operator names when finding the body', () => {
  const src = [
    'class C',
    '{',
    '    public int Gone() => 1; // tier 3',
    '    public static bool operator ==(C a, C b) { return true; }',
    '    public static bool operator !=(C a, C b) => false;',
    '    int Kept;',
    '}',
  ].join('\n');
  const text = drop(src, 'public int Gone\\(', 'public static bool operator ==', 'public static bool operator !=');
  assert.deepEqual(text.split('\n'), ['class C', '{', '', '', '', '    int Kept;', '}']);
});
