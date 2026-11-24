// Tests for tools/rewrite-upstream.mjs (Spike A's build-time rewrite). Run: node --test tools/rewrite-upstream.test.mjs
// The C# lexer it shares with tools/sync is tested in tools/sync/test/csharp.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { applyRules } from './rewrite-upstream.mjs';

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
    const out = path.join(dir, 'out', 'A.cs');
    return { status: 0, manifest: readFileSync(path.join(dir, 'out', 'manifest.txt'), 'utf8'), text: existsSync(out) ? readFileSync(out, 'utf8') : null };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

const rule = (r) => ({ hits: 0, ...r, re: new RegExp(r.drop ?? r.find, 'g' + (r.flags ?? '')) });

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

test('roslyn rules run first, through tools/sync/roslyn, and one that matches nothing fails the import', () => {
  const source = 'class A\n{\n    public void WinOnly() {}\n    Microsoft.UI.Xaml.UIElement e;\n}\n';
  const r = runTool(source, [
    { roslyn: { op: 'drop-member', type: 'A', member: 'WinOnly' }, files: ['A.cs'], why: 't' },
    { find: 'Microsoft\\.UI\\.Xaml\\.UIElement', replace: 'UIElement', why: 't' },
  ]);
  assert.equal(r.status, 0, r.out);
  assert.match(r.manifest, /\|2\n$/);
  assert.equal(r.text.split('\n').slice(2).join('\n'), 'class A\n{\n\n    UIElement e;\n}\n');
  const miss = runTool(source, [{ roslyn: { op: 'drop-member', type: 'A', member: 'Gone' }, files: ['A.cs'], why: 't' }]);
  assert.equal(miss.status, 1);
  assert.match(miss.out, /A\.cs: roslyn rule .*"Gone".*: matched 0 members, expected 1/);
});
