// Tests of the package checks (#52): node --test tools/packaging/test/. Made-up packages, so no dotnet is needed;
// pack.mjs runs the same checks on the real ones.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deflateRawSync } from 'node:zlib';
import { PACKAGES, checkPackages, embeddedPdb, findLocalPaths, parseNuspec, pdbDocuments } from '../lib/checks.mjs';
import { readZip } from '../lib/zip.mjs';

const VERSION = '0.1.0-preview.1';
const COMMIT = 'a'.repeat(40);
const URL = 'https://github.com/example-org/example-repo';
const LOCAL = '/work/checkout/isotope';
// Built at run time, so this file holds no drive-letter path or email for the repo-wide personal-data scan.
const DRIVE = String.fromCharCode(67) + ':';
const EMAIL = 'dev' + String.fromCharCode(64) + 'mail.org';

// A ZIP writer for the tests (stored and deflated entries; the reader ignores CRCs).
function makeZip(files) {
  const locals = [], centrals = [];
  let offset = 0;
  for (const [name, content, deflate] of files) {
    const data = Buffer.isBuffer(content) ? content : Buffer.from(content);
    const body = deflate ? deflateRawSync(data) : data;
    const nameBuf = Buffer.from(name);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(deflate ? 8 : 0, 8);
    local.writeUInt32LE(body.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(nameBuf.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(deflate ? 8 : 0, 10);
    central.writeUInt32LE(body.length, 20); central.writeUInt32LE(data.length, 24); central.writeUInt16LE(nameBuf.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBuf, body);
    centrals.push(central, nameBuf);
    offset += 30 + nameBuf.length + body.length;
  }
  const cd = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, end]);
}

// A minimal portable PDB, as compilers write it: a metadata root, a #~ stream holding only the Document table, and a
// #Blob heap with each document name in parts (a separator, then a blob per part) and the SourceLink JSON.
const enc = (n) => (n < 0x80 ? Buffer.from([n]) : n < 0x4000 ? Buffer.from([0x80 | (n >> 8), n & 0xff]) : Buffer.from([0xc0 | ((n >>> 24) & 0x1f), (n >> 16) & 0xff, (n >> 8) & 0xff, n & 0xff]));
function makePdb({ docs = ['/_/src/A.cs'], sourceLink = '' } = {}) {
  const heap = [Buffer.from([0])];
  let size = 1;
  const add = (bytes) => { const at = size, b = Buffer.concat([enc(bytes.length), bytes]); heap.push(b); size += b.length; return at; };
  const names = docs.map((d) => add(Buffer.concat([Buffer.from('/'), ...d.split('/').map((p) => enc(p ? add(Buffer.from(p)) : 0))])));
  if (sourceLink) add(Buffer.from(sourceLink));
  let blobs = Buffer.concat(heap);
  blobs = Buffer.concat([blobs, Buffer.alloc((4 - (blobs.length % 4)) % 4)]);
  const tables = Buffer.alloc(28 + 8 * docs.length);
  tables[4] = 2; tables[7] = 1;
  tables.writeBigUInt64LE(1n << 48n, 8);
  tables.writeUInt32LE(docs.length, 24);
  names.forEach((n, i) => tables.writeUInt16LE(n, 28 + 8 * i));
  const header = Buffer.alloc(60);
  header.write('BSJB', 0, 'latin1'); header.writeUInt16LE(1, 4); header.writeUInt16LE(1, 6); header.writeUInt32LE(12, 12);
  header.write('PDB v1.0', 16, 'latin1'); header.writeUInt16LE(2, 30);
  header.writeUInt32LE(60, 32); header.writeUInt32LE(tables.length, 36); header.write('#~', 40, 'latin1');
  header.writeUInt32LE(60 + tables.length, 44); header.writeUInt32LE(blobs.length, 48); header.write('#Blob', 52, 'latin1');
  return Buffer.concat([header, tables, blobs]);
}
const pdb = (commit = COMMIT, root = '/_/', docs) => makePdb({ docs, sourceLink: '{"documents":{"' + root + '*":"https://raw.githubusercontent.com/example-org/example-repo/' + commit + '/*"}}' });
const dllWithEmbeddedPdb = (p = pdb()) => {
  const size = Buffer.alloc(4); size.writeUInt32LE(p.length);
  return Buffer.concat([Buffer.from('MZ....RSDS'), Buffer.from('MPDB'), size, deflateRawSync(p), Buffer.from('tail')]);
};

function nuspec(spec, over = {}) {
  const m = { authors: 'The Isotope.UI.Avalonia authors', tags: 'avalonia ui react hooks declarative mvu', projectUrl: URL,
    repository: '<repository type="git" url="' + URL + '.git" commit="' + COMMIT + '" />', version: VERSION, extra: '', ...over };
  const deps = [...spec.dependsOn, ...(over.extraDeps ?? [])].map((id) => '<dependency id="' + id + '" version="' + (over.depVersion ?? m.version) + '"' + (over.depExclude ? ' exclude="' + over.depExclude + '"' : '') + ' />').join('\n');
  return '<?xml version="1.0" encoding="utf-8"?>\n<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n  <metadata>\n' +
    '    <id>' + spec.id + '</id>\n    <version>' + m.version + '</version>\n    <authors>' + m.authors + '</authors>\n' +
    (spec.analyzerOnly ? '    <developmentDependency>true</developmentDependency>\n' : '') +
    (spec.tool && !over.noToolType ? '    <packageTypes>\n      <packageType name="DotnetTool" />\n    </packageTypes>\n' : '') +
    '    <license type="expression">MIT</license>\n    <readme>README.md</readme>\n    <projectUrl>' + m.projectUrl + '</projectUrl>\n' +
    '    <description>A package.</description>\n    <tags>' + m.tags + '</tags>\n    ' + m.repository + '\n' + m.extra +
    (deps ? '    <dependencies>\n      <group targetFramework="net10.0">\n' + deps + '\n      </group>\n    </dependencies>\n' : '') +
    '  </metadata>\n</package>';
}

const toolSettings = (spec, command = spec.tool.command) => '<?xml version="1.0" encoding="utf-8"?>\n<DotNetCliTool Version="1">\n  <Commands>\n' +
  '    <Command Name="' + command + '" EntryPoint="' + spec.tool.entryPoint + '" Runner="dotnet" />\n  </Commands>\n</DotNetCliTool>';

// The packages as they must ship; change(id, entries, spec) edits one before it is zipped.
function fixture({ change = () => {}, readme = '# Package\n\nA package.\n', nuspecOver = {} } = {}) {
  const packages = [], symbols = [];
  for (const spec of PACKAGES) {
    const entries = new Map([
      [spec.id + '.nuspec', nuspec(spec, nuspecOver[spec.id] ?? {})],
      ['README.md', readme], ['LICENSE', 'MIT'], ['licenses/microsoft-ui-reactor/LICENSE', 'MIT'],
      ['licenses/microsoft-ui-reactor/ThirdPartyNoticeText.txt', 'Yoga, MIT'],
    ]);
    const embedded = spec.symbols ? [] : (spec.embedded ?? spec.files.filter((f) => f.endsWith('.dll')));
    for (const f of spec.files) entries.set(f, embedded.includes(f) ? dllWithEmbeddedPdb() : Buffer.from('MZ binary'));
    for (const p of spec.pdbs ?? []) entries.set(p, pdb());
    if (spec.tool) entries.set(spec.tool.settings, toolSettings(spec));
    const sym = spec.symbols ? new Map([[spec.id + '.nuspec', nuspec(spec)], ...spec.symbols.map((p) => [p, pdb()])]) : null;
    change(spec.id, entries, sym);
    const file = spec.id + '.' + VERSION + '.nupkg';
    packages.push({ file, entries: readZip(makeZip([...entries].map(([n, c]) => [n, c, true]))) });
    if (sym) symbols.push({ file: spec.id + '.' + VERSION + '.snupkg', entries: readZip(makeZip([...sym].map(([n, c]) => [n, c, false]))) });
  }
  return { packages, symbols, localPaths: [LOCAL], identities: ['some-login'] };
}

const problems = (opts) => checkPackages(fixture(opts));
const has = (list, re) => assert.ok(list.some((p) => re.test(p)), 'expected a problem matching ' + re + ', got:\n' + list.join('\n'));

test('the packages as they must ship have no problems', () => {
  assert.deepEqual(problems(), []);
});

test('the ZIP reader reads stored and deflated entries', () => {
  const zip = readZip(makeZip([['a.txt', 'stored', false], ['dir/b.txt', 'deflated '.repeat(50), true]]));
  assert.deepEqual([...zip.keys()], ['a.txt', 'dir/b.txt']);
  assert.equal(zip.get('a.txt').toString(), 'stored');
  assert.equal(zip.get('dir/b.txt').toString(), 'deflated '.repeat(50));
  assert.throws(() => readZip(Buffer.from('not a zip at all, just some bytes')), /not a ZIP/);
  assert.throws(() => readZip(Buffer.alloc(0)), /not a ZIP/);
});

test('the nuspec parser reads the metadata the checks use', () => {
  const m = parseNuspec(nuspec(PACKAGES[1]));
  assert.equal(m.id, 'Isotope.UI.Avalonia.Desktop');
  assert.equal(m.authors, 'The Isotope.UI.Avalonia authors');
  assert.deepEqual(m.license, { type: 'expression', value: 'MIT' });
  assert.equal(m.repository.commit, COMMIT);
  assert.deepEqual(m.dependencies, [{ id: 'Isotope.UI.Avalonia', version: VERSION }]);
});

test('a missing generator, notice or README is reported', () => {
  has(problems({ change: (id, e) => id === 'Isotope.UI.Avalonia' && e.delete('analyzers/dotnet/cs/Isotope.UI.Avalonia.Generators.dll') }), /Isotope\.UI\.Avalonia\.0.*missing analyzers\/dotnet\/cs\/Isotope\.UI\.Avalonia\.Generators\.dll/);
  has(problems({ change: (id, e) => id === 'Isotope.UI.Migration' && e.delete('licenses/microsoft-ui-reactor/ThirdPartyNoticeText.txt') }), /Migration.*missing licenses\/microsoft-ui-reactor\/ThirdPartyNoticeText\.txt/);
  has(problems({ change: (id, e) => id === 'Isotope.UI.Avalonia.Desktop' && e.delete('README.md') }), /Desktop.*missing README\.md/);
});

test('a missing or unexpected package is reported', () => {
  const f = fixture();
  has(checkPackages({ ...f, packages: f.packages.slice(1) }), /missing package Isotope\.UI\.Avalonia$/);
  const extra = { file: 'Isotope.UI.Avalonia.Generators.' + VERSION + '.nupkg', entries: new Map([['Isotope.UI.Avalonia.Generators.nuspec', Buffer.from(nuspec({ id: 'Isotope.UI.Avalonia.Generators', dependsOn: [] }))]]) };
  has(checkPackages({ ...f, packages: [...f.packages, extra] }), /unexpected package Isotope\.UI\.Avalonia\.Generators/);
});

test('authors, licence, tags and owners are checked', () => {
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { authors: 'Someone' } } }), /authors must be/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { tags: 'avalonia ui' } } }), /missing tag react/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { extra: '    <owners>someone</owners>\n' } } }), /owners is set/);
});

test('personal data in a nuspec or README is reported', () => {
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { authors: 'The Isotope.UI.Avalonia authors <' + EMAIL + '>' } } }), /nuspec:\d+: email/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { extra: '    <releaseNotes>built in ' + DRIVE + '\\work</releaseNotes>\n' } } }), /nuspec:\d+: drive-letter path/);
  has(problems({ readme: '# Package\n\nBuilt by some-login.\n' }), /README\.md: contains this machine's user or git identity/);
});

test('an identity matches as a whole word only', () => {
  // A user named "dev" is not in <developmentDependency>, which the analyzer package's nuspec has.
  assert.deepEqual(checkPackages({ ...fixture(), identities: ['dev'] }), []);
  has(checkPackages({ ...fixture({ readme: '# Package\n\nBuilt by dev.\n' }), identities: ['dev'] }), /contains this machine's user or git identity/);
});

test("the repository URL may hold the owner's name, but no user info, and needs a commit", () => {
  // The identity check skips the repository URL: it is the public repository's, read from git.
  const owned = { projectUrl: 'https://github.com/some-login/repo', repository: '<repository type="git" url="https://github.com/some-login/repo.git" commit="' + COMMIT + '" />' };
  assert.deepEqual(checkPackages({ ...fixture({ nuspecOver: { 'Isotope.UI.Avalonia': owned } }) }), []);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { repository: '<repository type="git" url="https://user:token' + String.fromCharCode(64) + 'github.com/o/r.git" commit="' + COMMIT + '" />' } } }), /no user info/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { repository: '<repository type="git" url="' + URL + '.git" />' } } }), /repository has no commit/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { projectUrl: 'https://example.com/elsewhere' } } }), /projectUrl must be the repository URL/);
});

test('the desktop package must depend on the library at its own version, with every asset', () => {
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia.Desktop': { depExclude: 'Build,Analyzers' } } }), /excludes Build,Analyzers/);
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia.Desktop': { depVersion: '0.0.9' } } }), /depends on Isotope\.UI\.Avalonia 0\.0\.9/);
});

test('versions must agree, and match --expect-version', () => {
  has(problems({ nuspecOver: { 'Isotope.UI.Migration': { version: '0.1.0-preview.2' } } }), /different versions/);
  has(checkPackages({ ...fixture(), expectVersion: '0.2.0' }), /expected version 0\.2\.0/);
});

test('the analyzer package has no lib/ and no dependencies', () => {
  has(problems({ change: (id, e) => id === 'Isotope.UI.Migration' && e.set('lib/netstandard2.0/Isotope.UI.Migration.dll', 'MZ') }), /no lib\/ or ref\//);
});

test('symbols: a missing PDB, or one without SourceLink at the commit, is reported', () => {
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia' && s.delete('lib/net10.0/Isotope.UI.Avalonia.pdb') }), /snupkg: missing lib\/net10\.0\/Isotope\.UI\.Avalonia\.pdb/);
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia' && s.set('lib/net10.0/Isotope.UI.Avalonia.pdb', Buffer.from('BSJB no source link')) }), /no SourceLink/);
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia.Desktop' && s.set('lib/net10.0/Isotope.UI.Avalonia.Desktop.pdb', pdb('b'.repeat(40))) }), /does not point at the package commit/);
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia' && s.set('lib/net10.0/Isotope.UI.Avalonia.pdb', pdb(COMMIT, DRIVE + '\\\\work\\\\')) }), /not for the deterministic \/_\/ root/);
  has(problems({ change: (id, e) => id === 'Isotope.UI.Migration' && e.set('analyzers/dotnet/cs/Isotope.UI.Migration.dll', Buffer.from('MZ no pdb')) }), /no embedded PDB/);
});

test('a local path in a DLL or PDB is reported, embedded PDBs included', () => {
  has(problems({ change: (id, e) => id === 'Isotope.UI.Avalonia' && e.set('lib/net10.0/Isotope.UI.Avalonia.dll', Buffer.from('MZ ' + LOCAL + '/src/x.cs')) }), /Isotope\.UI\.Avalonia\.dll: contains a folder path/);
  has(problems({ change: (id, e) => id === 'Isotope.UI.Migration' && e.set('analyzers/dotnet/cs/Isotope.UI.Migration.dll', dllWithEmbeddedPdb(Buffer.concat([pdb(), Buffer.from(LOCAL)]))) }), /Migration\.dll: contains a folder path/);
  assert.deepEqual(findLocalPaths(Buffer.from(DRIVE + '\\Work\\Checkout\\file.cs', 'utf16le'), [DRIVE.toLowerCase() + '/work/checkout']), [DRIVE.toLowerCase() + '/work/checkout']);
  assert.deepEqual(findLocalPaths(Buffer.from('/_/src/file.cs'), [LOCAL]), []);
});

test("a PDB's source documents must be under the /_/ root, in the symbols and in an embedded PDB", () => {
  const local = ['/_/src/A.cs', LOCAL + '/obj/Generated.cs'];
  assert.deepEqual(pdbDocuments(pdb(COMMIT, '/_/', local)), local);
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia' && s.set('lib/net10.0/Isotope.UI.Avalonia.pdb', pdb(COMMIT, '/_/', local)) }), /Isotope\.UI\.Avalonia\.pdb: 1 of 2 source documents are outside/);
  has(problems({ change: (id, e) => id === 'Isotope.UI.Migration' && e.set('analyzers/dotnet/cs/Isotope.UI.Migration.dll', dllWithEmbeddedPdb(pdb(COMMIT, '/_/', local))) }), /Migration\.dll \(embedded PDB\): 1 of 2 source documents are outside/);
  has(problems({ change: (id, e, s) => id === 'Isotope.UI.Avalonia.Desktop' && s.set('lib/net10.0/Isotope.UI.Avalonia.Desktop.pdb', pdb(COMMIT, '/_/', [])) }), /lists no source documents/);
});

test('the embedded PDB is found in a DLL', () => {
  assert.ok(embeddedPdb(dllWithEmbeddedPdb()).equals(pdb()));
  assert.equal(embeddedPdb(Buffer.from('MZ MPDB garbage')), null);
});

const TOOL = PACKAGES.find((s) => s.tool);

test('the tool package is a dotnet tool with its command, the library it reads, and nothing it does not run', () => {
  const tool = (change) => problems({ change: (id, e) => id === TOOL.id && change(e) });
  has(problems({ nuspecOver: { [TOOL.id]: { noToolType: true } } }), /Mcp.*must have the DotnetTool package type/);
  has(tool((e) => e.set(TOOL.tool.settings, toolSettings(TOOL, 'isotope'))), /the tool command must be isotope-mcp, not isotope/);
  has(tool((e) => e.delete(TOOL.tool.settings)), /missing tools\/net10\.0\/any\/DotnetToolSettings\.xml/);
  has(tool((e) => e.delete('tools/net10.0/any/Isotope.UI.Avalonia.xml')), /Mcp.*missing tools\/net10\.0\/any\/Isotope\.UI\.Avalonia\.xml/);
  has(tool((e) => e.set('tools/net10.0/any/runtimes/win-x64/native/libSkiaSharp.dll', 'MZ')), /1 native libraries in the tool, e\.g\. tools\/net10\.0\/any\/runtimes\/win-x64\/native\/libSkiaSharp\.dll/);
  has(tool((e) => e.set('tools/net10.0/any/runtimes/linux-x64/native/libHarfBuzzSharp.so', 'ELF')), /native libraries in the tool/);
  has(tool((e) => e.set('lib/net10.0/Isotope.UI.Mcp.dll', 'MZ')), /a tool package must have no lib\/ or ref\//);
  has(problems({ nuspecOver: { [TOOL.id]: { extraDeps: ['Isotope.UI.Avalonia'] } } }), /a tool package must have no dependencies/);
  // Only the tool is a tool.
  has(problems({ nuspecOver: { 'Isotope.UI.Avalonia': { extra: '    <packageTypes><packageType name="DotnetTool" /></packageTypes>\n' } } }), /Isotope\.UI\.Avalonia\.0.*only Isotope\.UI\.Mcp is a dotnet tool/);
});

test("the tool's PDBs, embedded or next to their DLLs, carry SourceLink at the commit", () => {
  const tool = (change) => problems({ change: (id, e) => id === TOOL.id && change(e) });
  has(tool((e) => e.set('tools/net10.0/any/Isotope.UI.Avalonia.pdb', pdb('b'.repeat(40)))), /Mcp.*Isotope\.UI\.Avalonia\.pdb: the SourceLink map does not point at the package commit/);
  has(tool((e) => e.delete('tools/net10.0/any/Isotope.UI.Avalonia.Desktop.pdb')), /Mcp.*missing tools\/net10\.0\/any\/Isotope\.UI\.Avalonia\.Desktop\.pdb/);
  has(tool((e) => e.set('tools/net10.0/any/Isotope.UI.Mcp.dll', Buffer.from('MZ no pdb'))), /Isotope\.UI\.Mcp\.dll: no embedded PDB/);
  has(tool((e) => e.set('tools/net10.0/any/Isotope.UI.Avalonia.pdb', pdb(COMMIT, '/_/', ['/_/src/A.cs', LOCAL + '/obj/G.cs']))), /Mcp.*Isotope\.UI\.Avalonia\.pdb: 1 of 2 source documents are outside/);
});
