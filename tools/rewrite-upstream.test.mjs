// Tests for tools/rewrite-upstream.mjs. Run: node --test tools/rewrite-upstream.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { codeMask, applyRules } from './rewrite-upstream.mjs';

const tool = path.join(path.dirname(fileURLToPath(import.meta.url)), 'rewrite-upstream.mjs');

// Runs the tool on one upstream file A.cs with the given rules; returns { status, out, manifest }.
function runTool(source, rules) {
  const dir = mkdtempSync(path.join(tmpdir(), 'rewrite-upstream-'));
  try {
    mkdirSync(path.join(dir, 'src'));
    writeFileSync(path.join(dir, 'src', 'A.cs'), source);
    writeFileSync(path.join(dir, 'files.props'), '<Compile Include="$(UpstreamSrc)A.cs" />');
    writeFileSync(path.join(dir, 'rules.json'), JSON.stringify(rules));
    const args = [tool, path.join(dir, 'files.props'), path.join(dir, 'rules.json'), path.join(dir, 'src'), path.join(dir, 'out')];
    try {
      execFileSync(process.execPath, args, { stdio: 'pipe' });
    } catch (e) {
      return { status: e.status, out: String(e.stderr) };
    }
    return { status: 0, manifest: readFileSync(path.join(dir, 'out', 'manifest.txt'), 'utf8') };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

const rule = (r) => ({ hits: 0, ...r, re: new RegExp(r.drop ?? r.find, 'g' + (r.flags ?? '')) });
const codeText = (s) => { const m = codeMask(s); return [...s].filter((_, i) => m[i]).join(''); };

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
  assert.equal(codeText('$"{v:N2}" + """a { b""" + c').replace(/\s+/g, ''), 'v++c');
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
  const { text, hits } = applyRules('A.cs', src, [rule({ drop: 'public void Gone\\(' })]);
  assert.equal(hits, 1);
  const lines = text.split('\r\n');
  assert.equal(lines.length, 10);
  assert.deepEqual(lines.slice(2, 8), ['', '', '', '', '', '']);
  assert.equal(lines[8], '    public int Kept => 1;');
});

test('drop handles expression bodies and field initializers with lambdas', () => {
  const src = 'class C {\n  int A() => B(() => { return 1; });\n  static readonly Lazy<int> L = new(() =>\n  {\n    return 2;\n  });\n  int Z;\n}';
  let { text } = applyRules('A.cs', src, [rule({ drop: 'int A\\(' }), rule({ drop: 'static readonly Lazy<int> L\\b' })]);
  assert.equal(text.split('\n').length, 8);
  assert.match(text, /int Z;/);
  assert.doesNotMatch(text, /return 1|return 2/);
});

test('drop fails when the match starts mid-line', () => {
  assert.throws(() => applyRules('A.cs', 'class C {\n  static int A() => 1;\n}', [rule({ drop: 'int A\\(' })]), /code precedes/);
});

test('drop ignores a match inside a comment or string, and fails unless it matches exactly once', () => {
  const src = 'class C {\n  // void Gone() {}\n  string s = "void Gone()";\n}';
  assert.throws(() => applyRules('A.cs', src, [rule({ drop: 'void Gone\\(' })]), /matched 0 times/);
  const twice = 'class C {\n  void Gone() {}\n  void Gone() {}\n}';
  assert.throws(() => applyRules('A.cs', twice, [rule({ drop: 'void Gone\\(' })]), /matched 2 times/);
});

test('a rule scoped by files leaves other files alone', () => {
  const r = rule({ find: 'X', replace: 'Y', files: ['B.cs'] });
  assert.equal(applyRules('A.cs', 'X', [r]).text, 'X');
  assert.equal(applyRules('B.cs', 'X', [r]).text, 'Y');
  assert.equal(r.hits, 1);
});

test('the import fails loudly when a rule matches nothing, and succeeds when every rule matches', () => {
  const bad = runTool('class C {}', [{ find: 'NotThere', replace: 'X', why: 't' }]);
  assert.equal(bad.status, 1);
  assert.match(bad.out, /rule did not match: NotThere/);
  const good = runTool('class C {}', [{ find: 'class C', replace: 'class D', why: 't' }]);
  assert.equal(good.status, 0);
  assert.match(good.manifest, /A\.cs\|.*A\.cs\|1/);
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
  const { text } = applyRules('A.cs', src, [
    rule({ drop: 'public int Gone\\(' }),
    rule({ drop: 'public static bool operator ==' }),
    rule({ drop: 'public static bool operator !=' }),
  ]);
  assert.deepEqual(text.split('\n'), ['class C', '{', '', '', '', '    int Kept;', '}']);
});

test('an unchanged rewrite keeps its file, and a failed import leaves no manifest', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'rewrite-upstream-'));
  try {
    mkdirSync(path.join(dir, 'src'));
    writeFileSync(path.join(dir, 'src', 'A.cs'), 'class C {}');
    writeFileSync(path.join(dir, 'files.props'), '<Compile Include="$(UpstreamSrc)A.cs" />');
    const run = (rules) => {
      writeFileSync(path.join(dir, 'rules.json'), JSON.stringify(rules));
      try {
        execFileSync(process.execPath, [tool, path.join(dir, 'files.props'), path.join(dir, 'rules.json'), path.join(dir, 'src'), path.join(dir, 'out')], { stdio: 'pipe' });
        return 0;
      } catch (e) {
        return e.status;
      }
    };
    const good = [{ find: 'class C', replace: 'class D', why: 't' }];
    assert.equal(run(good), 0);
    const out = path.join(dir, 'out', 'A.cs');
    const stamp = new Date(2000, 0, 1);
    utimesSync(out, stamp, stamp);
    assert.equal(run(good), 0);
    assert.equal(statSync(out).mtime.getTime(), stamp.getTime());
    assert.equal(run([{ find: 'NotThere', replace: 'X', why: 't' }]), 1);
    assert.equal(existsSync(path.join(dir, 'out', 'manifest.txt')), false);
    assert.equal(run(good), 0);
    assert.equal(run([{ drop: 'void Missing\\(', files: ['A.cs'], why: 't' }]), 1);
    assert.equal(existsSync(path.join(dir, 'out', 'manifest.txt')), false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
