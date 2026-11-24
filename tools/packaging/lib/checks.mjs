// The checks tools/packaging/pack.mjs runs on the packed packages (#52), kept free of I/O so the tests can feed them
// made-up packages. Each check returns problems as strings; none means the packages are as they must ship.
import { inflateRawSync } from 'node:zlib';
import { scanText } from '../../sync/lib/personal.mjs';

export const AUTHORS = 'The Isotope.UI.Avalonia authors';
export const LICENSE_EXPRESSION = 'MIT';
export const TAGS = ['avalonia', 'ui', 'react', 'hooks', 'declarative', 'mvu'];
/** Files every package carries: its README, our licence, and upstream's licence and third-party notices. */
export const COMMON_FILES = ['README.md', 'LICENSE', 'licenses/microsoft-ui-reactor/LICENSE', 'licenses/microsoft-ui-reactor/ThirdPartyNoticeText.txt'];

/**
 * The packages, in pack order. files: what the package must hold besides COMMON_FILES. symbols: the PDBs its .snupkg
 * must hold (null: the PDB is embedded in its DLL). dependsOn: the Isotope packages it depends on, at its own version
 * and with every asset (the library's generator included, for an app that references only the desktop package).
 */
export const PACKAGES = [
  {
    id: 'Isotope.UI.Avalonia',
    project: 'src/Isotope.UI.Avalonia/Isotope.UI.Avalonia.csproj',
    files: ['lib/net10.0/Isotope.UI.Avalonia.dll', 'lib/net10.0/Isotope.UI.Avalonia.xml', 'analyzers/dotnet/cs/Isotope.UI.Avalonia.Generators.dll'],
    symbols: ['lib/net10.0/Isotope.UI.Avalonia.pdb'],
    dependsOn: [],
  },
  {
    id: 'Isotope.UI.Avalonia.Desktop',
    project: 'src/Isotope.UI.Avalonia.Desktop/Isotope.UI.Avalonia.Desktop.csproj',
    files: ['lib/net10.0/Isotope.UI.Avalonia.Desktop.dll', 'lib/net10.0/Isotope.UI.Avalonia.Desktop.xml'],
    symbols: ['lib/net10.0/Isotope.UI.Avalonia.Desktop.pdb'],
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
    const isotopeDeps = m.dependencies.filter((d) => /^Isotope\./i.test(d.id));
    for (const d of isotopeDeps) {
      if (!spec.dependsOn.includes(d.id)) problems.push(where + ': unexpected dependency on ' + d.id);
      else {
        if (d.version !== m.version) problems.push(where + ': depends on ' + d.id + ' ' + d.version + ', not its own version ' + m.version);
        if (d.exclude) problems.push(where + ': its dependency on ' + d.id + ' excludes ' + d.exclude + ' (it must flow every asset, the generator included)');
      }
    }
    for (const id of spec.dependsOn) if (!isotopeDeps.some((d) => d.id === id)) problems.push(where + ': no dependency on ' + id);

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
        else problems.push(...checkSourceLink(sym.entries.get(pdb), repo?.commit, sym.file + ' ' + pdb));
      }
    } else {
      for (const dll of spec.files.filter((f) => f.endsWith('.dll') && pkg.entries.has(f))) {
        const pdb = embeddedPdb(pkg.entries.get(dll));
        if (!pdb) problems.push(where + ' ' + dll + ': no embedded PDB');
        else problems.push(...checkSourceLink(pdb, repo?.commit, where + ' ' + dll + ' (embedded PDB)'));
      }
    }
  }

  // No local path in any binary, including embedded PDBs (compressed, so a plain scan would miss them).
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
