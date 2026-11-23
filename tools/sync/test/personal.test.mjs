import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scanText, checkPersonalData, decodeForScan, loadAllowlist, lineHash } from '../lib/personal.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

// Planted paths are built from parts, so this file doesn't itself trip the repo-wide scan in CLAUDE.md.
const B = '\\';
const USERS = 'Us' + 'ers';
const drive = (letter) => letter + ':';
const winUser = drive('C') + B + USERS + B + 'someone' + B + 'App.cs';
const kinds = (text) => scanText(text).map((h) => h.kind);

const EMAIL_LINE = '// Contact: a.person@contoso.com';
const PATH_LINE = 'class Plain { string p = @"' + winUser + '"; }';
const PLANTED = {
  ...SAMPLE,
  files: { ...SAMPLE.files, 'src/Lib/Yoga/Node.cs': 'namespace Y;\n' + EMAIL_LINE + '\nclass Node {}\n', 'src/Lib/Core/Plain.cs': PATH_LINE + '\n' },
};
const allowlist = (f, entries) => f.put('tools/sync/personal-data.json', JSON.stringify({ allow: entries }));
const entry = (file, line) => ({ file, sha256: lineHash(line), why: 'a test example' });

test('emails, user folders and drive-letter paths are found; documentation domains and ordinary paths are not', () => {
  assert.deepEqual(kinds('// mail a.person@contoso.com'), ['email']);
  assert.deepEqual(kinds('// ends a sentence: a.person@contoso.com.'), ['email']);
  assert.deepEqual(kinds('x@example.io'), ['email'], 'example.io can be registered');
  for (const ok of ['x@example.com', 'x@mail.example.org', 'x@host.invalid', 'x@corp.test', 'x@host.example', 'x@a.localhost', 'git push', 'a@b', 'Foo@1.2.3'])
    assert.deepEqual(kinds(ok), [], ok);
  assert.deepEqual(kinds('var p = @"' + winUser + '";'), ['user-folder path', 'drive-letter path']);
  assert.deepEqual(kinds('var p = "' + drive('C') + B + B + USERS.toLowerCase() + B + B + 'someone";'), ['user-folder path', 'drive-letter path'], 'escaped, any case');
  assert.deepEqual(kinds('// rooted: (' + B + USERS + B + 'someone' + B + 'App.cs)'), ['user-folder path']);
  assert.deepEqual(kinds('// share: ' + B + B + 'server' + B + USERS + B + 'someone'), ['user-folder path']);
  assert.deepEqual(kinds('"' + B + B + B + B + 'server' + B + B + USERS + B + B + 'someone"'), ['user-folder path'], 'an escaped share');
  assert.deepEqual(kinds('// see /home/someone/src'), ['user-folder path']);
  assert.deepEqual(kinds('<a href="file:///' + USERS + '/someone/x">'), ['user-folder path']);
  assert.deepEqual(kinds('file:///' + drive('C') + '/' + USERS + '/someone'), ['user-folder path', 'drive-letter path']);
  assert.deepEqual(kinds('"' + drive('d') + '/build/x"'), ['drive-letter path']);
  for (const ok of ['src/' + USERS + '/List.cs', 'relative: ' + USERS + '/someone/x', 'Pages/home/Index.cs', '// Home/End keys move focus', 'Route /Home/Index',
    '$HOME/.nuget', 'https://example.com/' + USERS + '/1', 'https://example.com/a', 'a ? b:c', 'file:///x'])
    assert.deepEqual(kinds(ok), [], ok);
  assert.deepEqual(scanText('a\r\nb a@corp.com\r\n').map((h) => [h.line, h.lineText]), [[2, 'b a@corp.com']], 'CRLF lines, 1-based');
});

test('files that are not UTF-8 are checked too: Latin-1 and UTF-16 with a byte-order mark', () => {
  const line = '\xe9 a.person@contoso.com';
  const bom = String.fromCharCode(0xfeff);
  const utf16be = Buffer.from(bom + line, 'utf16le').swap16();
  for (const [name, buf] of [['latin1.txt', Buffer.from(line, 'latin1')], ['utf16le.txt', Buffer.from(bom + line, 'utf16le')], ['utf16be.txt', utf16be]]) {
    const r = checkPersonalData(new Map([[name, buf]]));
    assert.equal(r.errors.length, 1, name);
    assert.ok(r.errors[0].startsWith('personal data in ' + name + ':1 (email)'), r.errors[0]);
    assert.equal(r.hits[0].sha256, lineHash(line), name);
  }
});

test('binary files and UTF-16 with no mark are read as printable strings, so random bytes rarely hit', () => {
  const email = 'a.person@contoso.com';
  const bin = Buffer.concat([Buffer.from([0, 1, 2]), Buffer.from(drive('k') + '/', 'latin1'), Buffer.from([0, 0xff]), Buffer.from('id ' + email, 'latin1'), Buffer.from([0])]);
  assert.deepEqual(decodeForScan(bin), { text: 'id ' + email, binary: true });
  const r = checkPersonalData(new Map([['icon.bin', bin]]));
  assert.equal(r.errors.length, 1);
  assert.ok(r.errors[0].startsWith('personal data in icon.bin (binary file, printable string 1) (email)'), r.errors[0]);
  for (const [name, buf] of [['le', Buffer.from(email, 'utf16le')], ['be', Buffer.from(email, 'utf16le').swap16()], ['odd', Buffer.concat([Buffer.from([7]), Buffer.from(email, 'utf16le')])]])
    assert.ok(decodeForScan(buf).text.split('\n').includes(email), name);
});

test('the scan stays linear on very long lines', () => {
  const start = Date.now();
  for (const line of ['A'.repeat(300000) + '@', 'a.'.repeat(150000) + '@x', 'x@' + 'aa.'.repeat(100000), '/'.repeat(300000), B.repeat(300000)]) scanText(line);
  assert.ok(Date.now() - start < 2000, 'took ' + (Date.now() - start) + ' ms');
});

test('a planted email and a planted user path fail the sync, and nothing is written', () => {
  const f = makeFixture(PLANTED);
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 1);
    // Line numbers are the vendored file's: the provenance header is line 1.
    assert.match(r.err, /personal data in src\/Lib\/Yoga\/Node\.cs:3 \(email\)/);
    assert.doesNotMatch(r.err, /contoso|someone/, 'the matched text is not repeated');
    assert.match(r.err, /personal data in src\/Lib\/Core\/Plain\.cs:2 \(user-folder path/);
    assert.equal(r.err.match(/personal data in/g).length, 2, 'one error per line');
    assert.match(r.err, /Fix it with a transform rule in tools\/sync\/transforms\.json, never by hand/);
    assert.ok(r.err.includes('"sha256": "' + lineHash(EMAIL_LINE) + '"'), 'the error gives the allowlist entry');
    assert.ok(!f.exists('src/Upstream'), 'nothing written');
  } finally { f.cleanup(); }
});

test('allowlisted examples pass the sync and --check; --check fails once an entry is gone', () => {
  const f = makeFixture(PLANTED);
  try {
    allowlist(f, [entry('src/Lib/Yoga/Node.cs', EMAIL_LINE), entry('src/Lib/Core/Plain.cs', '  ' + PATH_LINE + '  ')]);
    let r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
    assert.equal(f.sync('--check').code, 0);
    allowlist(f, [entry('src/Lib/Core/Plain.cs', PATH_LINE)]);
    r = f.sync('--check');
    assert.equal(r.code, 1);
    assert.match(r.err, /regeneration at \w+ failed:[^]*personal data in src\/Lib\/Yoga\/Node\.cs:3/);
    assert.doesNotMatch(r.err, /Plain\.cs:2/);
  } finally { f.cleanup(); }
});

test('an entry covers only its own file and line, and an entry that matches nothing fails', () => {
  const f = makeFixture({ ...PLANTED, files: { ...PLANTED.files, 'src/Lib/Core/Plain.cs': EMAIL_LINE + '\n' } });
  try {
    allowlist(f, [entry('src/Lib/Yoga/Node.cs', EMAIL_LINE), entry('src/Lib/Yoga/Node.cs', '// a line upstream removed')]);
    const r = f.sync(f.base);
    assert.equal(r.code, 1);
    assert.match(r.err, /personal data in src\/Lib\/Core\/Plain\.cs:2/);
    assert.doesNotMatch(r.err, /personal data in src\/Lib\/Yoga/);
    assert.match(r.err, /allowlist entry for src\/Lib\/Yoga\/Node\.cs \(a test example\) matches no line[^]*remove it/);
  } finally { f.cleanup(); }
});

test('the check runs after transforms: a rule that removes the hit makes the sync pass', () => {
  const f = makeFixture({
    ...SAMPLE,
    files: { ...SAMPLE.files, 'src/Lib/Core/Engine.cs': 'class Engine { Microsoft.UI.Xaml.UIElement e; }\n' + EMAIL_LINE + '\n' },
    transforms: { rules: [...SAMPLE.transforms.rules, { id: 'drop-contact', find: '\\n// Contact: [^\\n]*', replace: '', files: ['src/Lib/Core/Engine.cs'] }] },
  });
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
    assert.doesNotMatch(f.read('src/Upstream/src/Lib/Core/Engine.cs'), /contoso/);
  } finally { f.cleanup(); }
});

test('files that are not vendored are not checked', () => {
  const f = makeFixture({ ...SAMPLE, files: { ...SAMPLE.files, 'src/Lib/Elements/Button.cs': EMAIL_LINE + '\n', 'src/Other/Thing.cs': PATH_LINE + '\n' } });
  try {
    const r = f.sync(f.base);
    assert.equal(r.code, 0, r.err);
  } finally { f.cleanup(); }
});

test('a malformed allowlist is refused', () => {
  const ok = { file: 'a.cs', sha256: 'a'.repeat(64), why: 'x' };
  assert.throws(() => loadAllowlist({}), /expected \{"allow"/);
  assert.throws(() => loadAllowlist({ allow: [{ ...ok, file: '' }] }), /needs a "file"/);
  assert.throws(() => loadAllowlist({ allow: [{ ...ok, sha256: 'A'.repeat(64) }] }), /64 lowercase hex/);
  assert.throws(() => loadAllowlist({ allow: [{ ...ok, why: ' ' }] }), /needs a "why"/);
  assert.throws(() => loadAllowlist({ allow: [ok, ok] }), /duplicate/);
  const f = makeFixture(SAMPLE);
  try {
    f.put('tools/sync/personal-data.json', JSON.stringify({ allow: [{ ...ok, why: '' }] }));
    const r = f.sync(f.base);
    assert.equal(r.code, 1);
    assert.match(r.err, /personal-data allowlist entry #1: needs a "why"/);
  } finally { f.cleanup(); }
});
