// The checks tools/packaging/pack.mjs runs on the packed packages (#52), kept free of I/O so the tests can feed them
// made-up packages. Each check returns problems as strings; none means the packages are as they must ship.
import { inflateRawSync } from 'node:zlib';
import { scanText } from '../../sync/lib/personal.mjs';

export const AUTHORS = 'The Isotope.UI.Avalonia authors';
export const LICENSE_EXPRESSION = 'MIT';
export const TAGS = ['avalonia', 'ui', 'react', 'hooks', 'declarative', 'mvu'];
/** Files every package carries: its README, our licence, and upstream's licence and third-party notices. */
export const COMMON_FILES = ['README.md', 'LICENSE', 'licenses/microsoft-ui-reactor/LICENSE', 'licenses/microsoft-ui-reactor/ThirdPartyNoticeText.txt'];

/** The frameworks the library and desktop packages ship for (#67): lib/<framework>/ in each, and a dependency group. */
export const FRAMEWORKS = ['net9.0', 'net10.0', 'net11.0'];
const lib = (...names) => FRAMEWORKS.flatMap((f) => names.map((n) => 'lib/' + f + '/' + n));

/**
 * The packages, in pack order. files: what the package must hold besides COMMON_FILES. symbols: the PDBs its .snupkg
 * must hold (null: no symbols package). embedded: with symbols null, the DLLs whose PDB is embedded (default: every DLL
 * in files). pdbs: PDBs shipped in the package itself, next to their DLLs. Every PDB carries SourceLink at the package's
 * commit. dependsOn: the Isotope packages it depends on, at its own version and with every asset (the library's
 * generator included, for an app that references only the desktop package). tool: a dotnet tool's command and entry
 * point; a tool carries its dependencies itself, so it has none, and it carries no native library.
 */
export const PACKAGES = [
  {
    id: 'Isotope.UI.Avalonia',
    project: 'src/Isotope.UI.Avalonia/Isotope.UI.Avalonia.csproj',
    files: [...lib('Isotope.UI.Avalonia.dll', 'Isotope.UI.Avalonia.xml'), 'analyzers/dotnet/cs/Isotope.UI.Avalonia.Generators.dll'],
    symbols: lib('Isotope.UI.Avalonia.pdb'),
    dependsOn: [],
  },
  {
    id: 'Isotope.UI.Avalonia.Desktop',
    project: 'src/Isotope.UI.Avalonia.Desktop/Isotope.UI.Avalonia.Desktop.csproj',
    files: lib('Isotope.UI.Avalonia.Desktop.dll', 'Isotope.UI.Avalonia.Desktop.xml'),
    symbols: lib('Isotope.UI.Avalonia.Desktop.pdb'),
    dependsOn: ['Isotope.UI.Avalonia'],
  },
  {
    id: 'Isotope.UI.Migration',
    project: 'src/Isotope.UI.Migration/Isotope.UI.Migration.csproj',
    files: ['analyzers/dotnet/cs/Isotope.UI.Migration.dll'],
    symbols: null,
    dependsOn: [],
    analyzerOnly: true,
  },
  {
    id: 'Isotope.UI.Mcp',
    project: 'src/Isotope.UI.Mcp/Isotope.UI.Mcp.csproj',
    // The tool and what it reads: the library's and the desktop startup's DLLs and XML docs, and the migration.
    files: ['tools/net10.0/any/Isotope.UI.Mcp.dll', 'tools/net10.0/any/DotnetToolSettings.xml', 'tools/net10.0/any/Isotope.UI.Avalonia.dll', 'tools/net10.0/any/Isotope.UI.Avalonia.xml',
      'tools/net10.0/any/Isotope.UI.Avalonia.Desktop.dll', 'tools/net10.0/any/Isotope.UI.Avalonia.Desktop.xml', 'tools/net10.0/any/Isotope.UI.Migration.dll'],
    symbols: null,
    embedded: ['tools/net10.0/any/Isotope.UI.Mcp.dll', 'tools/net10.0/any/Isotope.UI.Migration.dll'],
    pdbs: ['tools/net10.0/any/Isotope.UI.Avalonia.pdb', 'tools/net10.0/any/Isotope.UI.Avalonia.Desktop.pdb'],
    dependsOn: [],
    tool: { command: 'isotope-mcp', entryPoint: 'Isotope.UI.Mcp.dll', settings: 'tools/net10.0/any/DotnetToolSettings.xml' },
  },
];

const unescapeXml = (s) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
const attrs = (tag) => Object.fromEntries([...tag.matchAll(/([\w:]+)="([^"]*)"/g)].map((m) => [m[1], unescapeXml(m[2])]));

/** The metadata of a .nuspec the SDK wrote (one element per line, no CDATA), enough for the checks below. */
export function parseNuspec(text) {
  const meta = text.match(/<metadata[^>]*>([\s\S]*?)<\/metadata>/)?.[1] ?? '';
  const el = (name) => { const m = meta.match(new RegExp('<' + name + '(\\s[^>]*)?>([\\s\\S]*?)</' + name + '>')); return m ? unescapeXml(m[2].trim()) : undefined; };
  const licenseTag = meta.match(/<license\s[^>]*>/)?.[0];
  const repoTag = meta.match(/<repository\s[^>]*\/?>/)?.[0];
  return {
    id: el('id'),
    version: el('version'),
    authors: el('authors'),
    owners: el('owners'),
    description: el('description'),
    copyright: el('copyright'),
    readme: el('readme'),
    projectUrl: el('projectUrl'),
    license: licenseTag ? { type: attrs(licenseTag).type, value: el('license') } : undefined,
    tags: (el('tags') ?? '').split(/\s+/).filter(Boolean),
    developmentDependency: el('developmentDependency') === 'true',
    repository: repoTag ? attrs(repoTag) : undefined,
    dependencies: [...meta.matchAll(/<dependency\s[^>]*\/?>/g)].map((m) => attrs(m[0])),
    // One per target framework (an empty one is self-closing): the framework and the ids it depends on.
    dependencyGroups: [...meta.matchAll(/<group\s([^>]*?)(?:\/>|>([\s\S]*?)<\/group>)/g)].map((g) => ({
      targetFramework: attrs(g[1]).targetFramework,
      ids: [...(g[2] ?? '').matchAll(/<dependency\s[^>]*\/?>/g)].map((d) => attrs(d[0]).id),
    })),
    packageTypes: [...meta.matchAll(/<packageType\s[^>]*\/?>/g)].map((m) => attrs(m[0]).name),
  };
}

/** The portable PDB embedded in a DLL (an "MPDB" debug entry: a 4-byte size, then deflate), or null. */
export function embeddedPdb(dll) {
  const sig = Buffer.from('MPDB');
  for (let at = dll.indexOf(sig); at >= 0; at = dll.indexOf(sig, at + 1)) {
    try {
      const size = dll.readUInt32LE(at + 4);
      const pdb = inflateRawSync(dll.subarray(at + 8), { info: false });
      if (pdb.length === size && pdb.subarray(0, 4).toString('latin1') === 'BSJB') return pdb;
    } catch { /* not this one */ }
  }
  return null;
}

// Portable PDB compressed unsigned integer (ECMA-335 II.23.2): [value, bytes read].
function compressed(buf, at) {
  const b = buf[at];
  if ((b & 0x80) === 0) return [b, 1];
  if ((b & 0xc0) === 0x80) return [((b & 0x3f) << 8) | buf[at + 1], 2];
  return [(((b & 0x1f) << 24) | (buf[at + 1] << 16) | (buf[at + 2] << 8) | buf[at + 3]) >>> 0, 4];
}

/**
 * The source document names of a portable PDB (its Document table). A name is stored as a separator and parts on the
 * blob heap, never as one string, so a plain byte search can't find a local path in it. Throws if the PDB can't be read.
 */
export function pdbDocuments(pdb) {
  if (pdb.toString('latin1', 0, 4) !== 'BSJB') throw new Error('not a portable PDB');
  const versionLength = pdb.readUInt32LE(12);
  let at = 16 + versionLength + 2;
  const streams = {};
  for (let n = pdb.readUInt16LE(at), i = (at += 2, 0); i < n; i++) {
    const offset = pdb.readUInt32LE(at), size = pdb.readUInt32LE(at + 4);
    const end = pdb.indexOf(0, at + 8);
    streams[pdb.toString('latin1', at + 8, end)] = pdb.subarray(offset, offset + size);
    at = end + 1 + ((4 - ((end + 1 - (at + 8)) % 4)) % 4);
  }
  const tables = streams['#~'] ?? streams['#-'], blobs = streams['#Blob'];
  if (!tables || !blobs) throw new Error('a portable PDB with no tables or blob heap');
  const heapSizes = tables[6];
  const valid = tables.readBigUInt64LE(8);
  const present = [...Array(64).keys()].filter((t) => (valid >> BigInt(t)) & 1n);
  const DOCUMENT = 0x30;
  if (!present.includes(DOCUMENT)) return [];
  if (present[0] !== DOCUMENT) throw new Error('unexpected tables before Document');
  const rows = tables.readUInt32LE(24);
  const blobIndex = heapSizes & 4 ? 4 : 2, guidIndex = heapSizes & 2 ? 4 : 2;
  const rowSize = 2 * blobIndex + 2 * guidIndex;
  const readIndex = (buf, o, size) => (size === 4 ? buf.readUInt32LE(o) : buf.readUInt16LE(o));
  const blob = (index) => { const [len, n] = compressed(blobs, index); return blobs.subarray(index + n, index + n + len); };
  const names = [];
  let row = 24 + 4 * present.length;
  for (let r = 0; r < rows; r++, row += rowSize) {
    const name = blob(readIndex(tables, row, blobIndex));
    const separator = name[0] ? String.fromCharCode(name[0]) : '';
    const parts = [];
    for (let o = 1; o < name.length;) { const [part, n] = compressed(name, o); o += n; parts.push(part ? blob(part).toString('utf8') : ''); }
    names.push(parts.join(separator));
  }
  return names;
}

/** Problems with a PDB's SourceLink: it must map the deterministic /_/ root to the repository at the package's commit. */
export function checkSourceLink(pdb, commit, where) {
  const text = pdb.toString('latin1');
  const json = text.match(/\{"documents":\{[^}]*\}\}/)?.[0];
  if (!json) return [where + ': no SourceLink in the PDB'];
  const problems = [];
  if (!json.includes('"/_/')) problems.push(where + ': the SourceLink map is not for the deterministic /_/ root (packed without ContinuousIntegrationBuild?)');
  if (!commit || !json.includes(commit)) problems.push(where + ': the SourceLink map does not point at the package commit ' + commit);
  if (!/"https:\/\//.test(json)) problems.push(where + ': the SourceLink map has no https URL');
  return problems;
}

/** Problems with a PDB's documents: each must be under the deterministic /_/ root, so none names a local folder. */
export function checkDocuments(pdb, where) {
  let names;
  try { names = pdbDocuments(pdb); } catch (e) { return [where + ': the PDB could not be read (' + e.message + ')']; }
  if (!names.length) return [where + ': the PDB lists no source documents'];
  const outside = names.filter((n) => !n.startsWith('/_/'));
  return outside.length ? [where + ': ' + outside.length + ' of ' + names.length + ' source documents are outside the deterministic /_/ root, so they name build machine folders'] : [];
}

/** Where a local path appears in a binary, as UTF-8 or UTF-16, either slash, any case. */
export function findLocalPaths(buf, localPaths) {
  const hay = buf.toString('latin1').toLowerCase();
  const found = [];
  for (const p of localPaths) {
    if (!p || p.length < 4) continue;
    const forms = new Set([p, p.replace(/\\/g, '/'), p.replace(/\//g, '\\')].map((s) => s.toLowerCase().replace(/[\\/]+$/, '')));
    for (const f of forms) {
      const utf16 = Buffer.from(f, 'utf16le').toString('latin1');
      if (hay.includes(f) || hay.includes(utf16)) { found.push(p); break; }
    }
  }
  return found;
}

// Identity strings (the machine's git name and email, the OS user), as whole words, anywhere but the repository URL,
// which is the public repository's and comes from git.
function checkIdentities(text, identities, where) {
  const scrubbed = text.replace(/<repository\s[^>]*\/?>/g, '').replace(/<projectUrl>[^<]*<\/projectUrl>/g, '').toLowerCase();
  const word = (s) => new RegExp('(?<![a-z0-9])' + s.toLowerCase().replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '(?![a-z0-9])');
  return identities.filter((s) => s && s.length >= 3 && word(s).test(scrubbed)).map(() => where + ': contains this machine\'s user or git identity');
}

// A dotnet tool: the DotnetTool package type, its command, everything it runs inside it (no dependencies, no lib/), and
// no native library (the desktop startup brings Skia, HarfBuzz and ANGLE for every platform, which the tool never loads).
function checkTool(spec, pkg, m, names, where) {
  const problems = [];
  if (!m.packageTypes.includes('DotnetTool')) problems.push(where + ': a tool package must have the DotnetTool package type');
  if (m.dependencies.length) problems.push(where + ': a tool package must have no dependencies (it carries them)');
  if (m.developmentDependency) problems.push(where + ': a tool package must not be a developmentDependency');
  if (names.some((n) => /^(lib|ref)\//.test(n))) problems.push(where + ': a tool package must have no lib/ or ref/');
  const natives = names.filter((n) => /(^|\/)native\//.test(n) || /\.(so|dylib)$/i.test(n));
  if (natives.length) problems.push(where + ': ' + natives.length + ' native libraries in the tool, e.g. ' + natives[0]);
  const settings = pkg.entries.get(spec.tool.settings)?.toString('utf8') ?? '';
  const command = settings.match(/<Command\s[^>]*\/?>/)?.[0];
  const a = command ? attrs(command) : {};
  if (a.Name !== spec.tool.command) problems.push(where + ': the tool command must be ' + spec.tool.command + ', not ' + a.Name);
  if (a.EntryPoint !== spec.tool.entryPoint) problems.push(where + ': the tool entry point must be ' + spec.tool.entryPoint + ', not ' + a.EntryPoint);
  if (a.Runner !== 'dotnet') problems.push(where + ': the tool runner must be dotnet, not ' + a.Runner);
  return problems;
}

const isDll = (name) => /\.(dll|pdb)$/i.test(name);

/**
 * Every check. input: {packages: [{file, entries: Map}], symbols: [{file, entries: Map}], expectVersion?,
 * localPaths: [], identities: []}. Returns problems (strings). The READMEs' C# blocks are DocSnippetTests' (the library tests).
 */
export function checkPackages({ packages, symbols = [], expectVersion, localPaths = [], identities = [] }) {
  const problems = [];
  const byId = new Map();
  for (const pkg of packages) {
    const nuspecName = [...pkg.entries.keys()].find((n) => !n.includes('/') && n.endsWith('.nuspec'));
    if (!nuspecName) { problems.push(pkg.file + ': no .nuspec'); continue; }
    const text = pkg.entries.get(nuspecName).toString('utf8');
    byId.set(parseNuspec(text).id, { ...pkg, text, meta: parseNuspec(text) });
  }
  for (const id of byId.keys()) if (!PACKAGES.some((s) => s.id === id)) problems.push(byId.get(id).file + ': unexpected package ' + id);

  const versions = new Set([...byId.values()].map((p) => p.meta.version));
  if (versions.size > 1) problems.push('the packages have different versions: ' + [...versions].join(', '));
  if (expectVersion && [...versions].some((v) => v !== expectVersion)) problems.push('expected version ' + expectVersion + ', packed ' + [...versions].join(', '));

  for (const spec of PACKAGES) {
    const pkg = byId.get(spec.id);
    if (!pkg) { problems.push('missing package ' + spec.id); continue; }
    const where = pkg.file, m = pkg.meta, names = [...pkg.entries.keys()];

    for (const f of [...COMMON_FILES, ...spec.files]) if (!pkg.entries.has(f)) problems.push(where + ': missing ' + f);
    if (spec.analyzerOnly) {
      if (names.some((n) => /^(lib|ref)\//.test(n))) problems.push(where + ': an analyzer package must have no lib/ or ref/');
      if (!m.developmentDependency) problems.push(where + ': an analyzer package must be a developmentDependency');
      if (m.dependencies.length) problems.push(where + ': an analyzer package must have no dependencies');
    }
    for (const n of names) if (n.startsWith('analyzers/') && !n.startsWith('analyzers/dotnet/cs/')) problems.push(where + ': analyzer outside analyzers/dotnet/cs: ' + n);
    if (spec.tool) problems.push(...checkTool(spec, pkg, m, names, where));
    else if (m.packageTypes.includes('DotnetTool')) problems.push(where + ': only ' + PACKAGES.filter((s) => s.tool).map((s) => s.id).join(', ') + ' is a dotnet tool');

    // Metadata.
    if (m.authors !== AUTHORS) problems.push(where + ': authors must be "' + AUTHORS + '", not "' + m.authors + '"');
    if (m.owners) problems.push(where + ': owners is set');
    if (m.license?.type !== 'expression' || m.license.value !== LICENSE_EXPRESSION) problems.push(where + ': licence must be the expression ' + LICENSE_EXPRESSION);
    if (m.readme !== 'README.md') problems.push(where + ': readme must be README.md');
    if (!m.description) problems.push(where + ': no description');
    for (const t of TAGS) if (!m.tags.includes(t)) problems.push(where + ': missing tag ' + t);
    const repo = m.repository;
    if (!repo) problems.push(where + ': no repository (SourceLink)');
    else {
      if (repo.type !== 'git') problems.push(where + ': repository type must be git');
      if (!/^https:\/\/[^/@\s]+\/\S+$/.test(repo.url ?? '')) problems.push(where + ': repository url must be https with no user info: ' + repo.url);
      if (!/^[0-9a-f]{40}$/.test(repo.commit ?? '')) problems.push(where + ': repository has no commit');
      if (m.projectUrl !== (repo.url ?? '').replace(/\.git$/, '')) problems.push(where + ': projectUrl must be the repository URL');
    }

    // Dependencies on Isotope packages: exactly dependsOn, at this version, every asset.
    // Each framework's group repeats the dependencies, so a problem is reported once, not once per group (#67).
    const isotopeDeps = m.dependencies.filter((d) => /^Isotope\./i.test(d.id));
    const depProblems = new Set();
    for (const d of isotopeDeps) {
      if (!spec.dependsOn.includes(d.id)) depProblems.add(where + ': unexpected dependency on ' + d.id);
      else {
        if (d.version !== m.version) depProblems.add(where + ': depends on ' + d.id + ' ' + d.version + ', not its own version ' + m.version);
        if (d.exclude) depProblems.add(where + ': its dependency on ' + d.id + ' excludes ' + d.exclude + ' (it must flow every asset, the generator included)');
      }
    }
    problems.push(...depProblems);
    for (const id of spec.dependsOn) if (!isotopeDeps.some((d) => d.id === id)) problems.push(where + ': no dependency on ' + id);
    // A library package: a dependency group for each framework, each with the Isotope dependencies (#67).
    if (!spec.tool && !spec.analyzerOnly) {
      for (const f of FRAMEWORKS) {
        const group = m.dependencyGroups.find((g) => g.targetFramework === f);
        if (!group) problems.push(where + ': no dependency group for ' + f);
        else for (const id of spec.dependsOn) if (!group.ids.includes(id)) problems.push(where + ': no dependency on ' + id + ' for ' + f);
      }
      const extra = m.dependencyGroups.map((g) => g.targetFramework).filter((f) => !FRAMEWORKS.includes(f));
      if (extra.length) problems.push(where + ': dependency groups for frameworks it does not ship for: ' + extra.join(', '));
    }

    // Personal data in what nuget.org shows.
    for (const [name, text] of [['nuspec', pkg.text], ['README.md', pkg.entries.get('README.md')?.toString('utf8') ?? '']]) {
      for (const h of scanText(text)) problems.push(where + ' ' + name + ':' + h.line + ': ' + h.kind);
      problems.push(...checkIdentities(text, identities, where + ' ' + name));
    }

    // Symbols: the .snupkg's PDBs, or the PDB embedded in the DLL, carry SourceLink.
    if (spec.symbols) {
      const sym = symbols.find((s) => s.file.toLowerCase() === (spec.id + '.' + m.version + '.snupkg').toLowerCase());
      if (!sym) problems.push(where + ': no symbols package');
      else for (const pdb of spec.symbols) {
        if (!sym.entries.has(pdb)) problems.push(sym.file + ': missing ' + pdb);
        else problems.push(...checkSourceLink(sym.entries.get(pdb), repo?.commit, sym.file + ' ' + pdb), ...checkDocuments(sym.entries.get(pdb), sym.file + ' ' + pdb));
      }
    } else {
      for (const dll of (spec.embedded ?? spec.files.filter((f) => f.endsWith('.dll'))).filter((f) => pkg.entries.has(f))) {
        const pdb = embeddedPdb(pkg.entries.get(dll));
        if (!pdb) problems.push(where + ' ' + dll + ': no embedded PDB');
        else problems.push(...checkSourceLink(pdb, repo?.commit, where + ' ' + dll + ' (embedded PDB)'), ...checkDocuments(pdb, where + ' ' + dll + ' (embedded PDB)'));
      }
    }
    for (const p of spec.pdbs ?? []) {
      if (!pkg.entries.has(p)) problems.push(where + ': missing ' + p);
      else problems.push(...checkSourceLink(pkg.entries.get(p), repo?.commit, where + ' ' + p), ...checkDocuments(pkg.entries.get(p), where + ' ' + p));
    }
  }

  // No local path in any binary, including embedded PDBs (compressed, so a plain scan would miss them). A PDB's
  // document names are checked above (checkDocuments): they are stored in parts, which this scan can't see.
  for (const pkg of [...packages, ...symbols]) {
    for (const [name, data] of pkg.entries) {
      if (!isDll(name)) continue;
      const parts = [data];
      const embedded = name.endsWith('.dll') ? embeddedPdb(data) : null;
      if (embedded) parts.push(embedded);
      for (const part of parts) for (const p of findLocalPaths(part, localPaths)) problems.push(pkg.file + ' ' + name + ': contains a folder path of the build machine');
    }
  }
  return problems;
}
