#!/usr/bin/env node
// Packs the four packages and proves them (#52, #60): node tools/packaging/pack.mjs [--out <dir>] [--expect-version <v>]
// [--no-smoke]. CI runs it on every push, and the release workflow before it publishes; docs/releasing.md has the rest.
//
// 1. dotnet pack of each package (Release, ContinuousIntegrationBuild so no local path reaches a DLL or PDB), into
//    --out (default bin/packages under the repo root, which git ignores). Old builds of these packages there are
//    deleted first; a folder holding anything else is refused.
// 2. The checks in lib/checks.mjs: layout, metadata, dependencies, symbols and SourceLink, personal data, local paths.
// 3. The smoke test (tests/Isotope.UI.PackageSmoke): an app built against the packed packages alone, from a fresh
//    packages folder, run headless; the migration analyzer, from its package, run by dotnet format; and the MCP server,
//    installed from its package into a temporary tool folder, asked for its tools over stdio. Its nuget.config maps
//    Isotope.* to the packed folder alone, so nuget.org's copy of a version is never used instead.
// The version comes from MinVer (git tags); --expect-version fails the run if it is another.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, realpathSync } from 'node:fs';
import { homedir, tmpdir, userInfo } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PACKAGES, checkPackages, parseNuspec } from './lib/checks.mjs';
import { readZip } from './lib/zip.mjs';
import { initialize, startMcp } from './lib/mcp.mjs';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const SMOKE = path.join(REPO, 'tests', 'Isotope.UI.PackageSmoke');
const USAGE = 'usage: node tools/packaging/pack.mjs [--out <dir> [--clean]] [--expect-version <version>] [--no-smoke]';
const DEFAULT_OUT = path.join(REPO, 'bin', 'packages');

function parseArgs(argv) {
  const opts = { out: DEFAULT_OUT, smoke: true, clean: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out' && argv[i + 1]) opts.out = path.resolve(argv[++i]);
    else if (a === '--expect-version' && argv[i + 1]) opts.expectVersion = argv[++i].replace(/^v/, '');
    else if (a === '--no-smoke') opts.smoke = false;
    else if (a === '--clean') opts.clean = true;
    else if (a === '--help' || a === '-h') { console.log(USAGE); process.exit(0); }
    else { console.error(USAGE); process.exit(2); }
  }
  return opts;
}

// Runs a command from the repo root (never from upstream/, whose global.json selects another SDK). Output streams.
function run(cmd, args, { env, capture = false } = {}) {
  const started = Date.now();
  const r = spawnSync(cmd, args, { cwd: REPO, env: { ...process.env, ...env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' }, stdio: capture ? 'pipe' : 'inherit', encoding: 'utf8', shell: false });
  if (r.error) throw r.error;
  const out = capture ? (r.stdout ?? '') + (r.stderr ?? '') : '';
  if (capture) process.stdout.write(out);
  return { status: r.status, out, ms: Date.now() - started };
}

function fail(message) {
  console.error('pack: ' + message);
  process.exit(1);
}

function gitConfig(key) {
  const r = spawnSync('git', ['-C', REPO, 'config', '--get', key], { encoding: 'utf8' });
  return r.status === 0 ? r.stdout.trim() : '';
}

const opts = parseArgs(process.argv.slice(2));
const timings = [];

// 1. Pack, into an empty folder. The default folder (git-ignored) is emptied of earlier builds of these packages; another
// --out must be empty or new, or given --clean to empty it the same way. Anything else in it is refused, never deleted.
const ours = (name) => PACKAGES.some((s) => name.toLowerCase().startsWith(s.id.toLowerCase() + '.') && /^\d/.test(name.slice(s.id.length + 1)) && /\.s?nupkg$/i.test(name));
mkdirSync(opts.out, { recursive: true });
const existing = readdirSync(opts.out);
if (existing.length && opts.out !== DEFAULT_OUT && !opts.clean) fail(opts.out + ' is not empty; give an empty or new --out, or --clean to delete earlier builds of these packages in it');
const others = existing.filter((n) => !ours(n));
if (others.length) fail(opts.out + ' holds files other than these packages (' + others[0] + '); give an empty or new --out');
for (const name of existing) rmSync(path.join(opts.out, name));
for (const spec of PACKAGES) {
  const r = run('dotnet', ['pack', spec.project, '-c', 'Release', '-o', opts.out, '-nologo', '-v:q', '-p:ContinuousIntegrationBuild=true']);
  timings.push(['pack ' + spec.id, r.ms]);
  if (r.status !== 0) fail('dotnet pack ' + spec.project + ' failed (exit ' + r.status + ')');
}

// 2. Check.
const read = (pattern) => readdirSync(opts.out).filter((n) => pattern.test(n)).sort()
  .map((file) => ({ file, entries: readZip(readFileSync(path.join(opts.out, file))) }));
const packages = read(/\.nupkg$/i), symbols = read(/\.snupkg$/i);
const realRepo = realpathSync(REPO);
// This machine's identity, which no nuspec or README may hold: git's name (and each word of it) and email (and its local
// part), and the OS user. os.userInfo() throws where the uid has no passwd entry (some containers).
let osUser = process.env.USER || process.env.USERNAME || '';
try { osUser = userInfo().username || osUser; } catch { /* keep the environment's */ }
const gitName = gitConfig('user.name'), gitEmail = gitConfig('user.email');
const identities = [...new Set([gitName, ...gitName.split(/\s+/), gitEmail, ...gitEmail.split('@')[0].split('+'), osUser].filter((s) => s && s.length >= 3))];
const problems = checkPackages({
  packages,
  symbols,
  expectVersion: opts.expectVersion,
  localPaths: [...new Set([REPO, realRepo, homedir()])],
  identities,
});
const nuspec = (p) => parseNuspec([...p.entries].find(([n]) => n.endsWith('.nuspec'))[1].toString('utf8'));
const version = packages.length ? nuspec(packages[0]).version : undefined;
for (const p of [...packages, ...symbols]) {
  const size = readFileSync(path.join(opts.out, p.file)).length;
  console.log('packed ' + p.file + ' (' + p.entries.size + ' files, ' + size + ' bytes)');
}
if (problems.length) {
  for (const p of problems) console.error('problem: ' + p);
  fail(problems.length + ' problem(s) in the packages');
}
console.log('checks: ' + packages.length + ' packages and ' + symbols.length + ' symbol packages, version ' + version + ', no problems');

// 3. Smoke: a fresh packages folder, so nothing restored earlier (another build of the same version) is reused.
if (opts.smoke) {
  const nugetPackages = mkdtempSync(path.join(tmpdir(), 'isotope-smoke-'));
  // ISOTOPE_PACKAGES is the smoke nuget.config's only source for Isotope.*.
  const env = { IsotopeVersion: version, NUGET_PACKAGES: nugetPackages, ISOTOPE_PACKAGES: opts.out };
  let failure, toolPath;
  // A failing step throws, so the packages folder is deleted before the run fails.
  const check = (ok, message) => { if (!ok) throw new Error(message); };
  try {
    const app = path.join('tests', 'Isotope.UI.PackageSmoke', 'App');
    // No build servers: an MSBuild node kept alive would hold DLLs in the packages folder, which is deleted after.
    let r = run('dotnet', ['build', app, '-c', 'Release', '-nologo', '-v:q', '--no-incremental', '--disable-build-servers'], { env });
    timings.push(['smoke build', r.ms]);
    check(r.status === 0, 'the smoke app did not build against the packages (exit ' + r.status + ')');
    r = run('dotnet', ['run', '--project', app, '-c', 'Release', '--no-build'], { env });
    timings.push(['smoke run', r.ms]);
    check(r.status === 0, 'the smoke app failed (exit ' + r.status + ')');

    const migration = path.join('tests', 'Isotope.UI.PackageSmoke', 'Migration', 'Isotope.UI.PackageSmoke.Migration.csproj');
    r = run('dotnet', ['restore', migration, '-nologo', '-v:q', '--disable-build-servers'], { env });
    check(r.status === 0, 'the migration smoke project did not restore (exit ' + r.status + ')');
    // --verify-no-changes exits non-zero when a fix would change a file: the analyzer and its fix loaded and fired.
    r = run('dotnet', ['format', 'analyzers', migration, '--no-restore', '--verify-no-changes', '--severity', 'warn', '--diagnostics', 'ISOMIG001'], { env, capture: true });
    timings.push(['smoke migration', r.ms]);
    check(r.status !== 0 && r.out.includes('ISOMIG001'), 'the migration analyzer from its package reported no ISOMIG001 (exit ' + r.status + ')');
    console.log('smoke: the migration analyzer from its package reported ISOMIG001 and offered its fix');

    // The MCP server: installed from its package into a temporary tool folder (never globally), started as an MCP
    // client starts it, and asked for its tools and the docs of a hook, which come from the library and XML docs it carries.
    toolPath = mkdtempSync(path.join(tmpdir(), 'isotope-tool-'));
    r = run('dotnet', ['tool', 'install', 'Isotope.UI.Mcp', '--version', version, '--tool-path', toolPath, '--configfile', path.join(SMOKE, 'nuget.config')], { env });
    timings.push(['smoke tool install', r.ms]);
    check(r.status === 0, 'the MCP tool did not install from its package (exit ' + r.status + ')');
    const started = Date.now();
    const server = startMcp(path.join(toolPath, process.platform === 'win32' ? 'isotope-mcp.exe' : 'isotope-mcp'), [], { env });
    try {
      const info = await initialize(server);
      check(info?.serverInfo?.name === 'isotope-mcp', 'the MCP server named itself ' + info?.serverInfo?.name);
      check(info.serverInfo.version === version, 'the MCP server says version ' + info.serverInfo.version + ', not ' + version);
      const tools = (await server.request('tools/list')).tools.map((t) => t.name).sort();
      const expected = ['get_api', 'get_guide', 'list_guides', 'map_reactor_name', 'migrate_code', 'search_api'];
      check(JSON.stringify(tools) === JSON.stringify(expected), 'the MCP server lists ' + tools.join(', ') + ', not ' + expected.join(', '));
      const doc = (await server.request('tools/call', { name: 'get_api', arguments: { name: 'UseState' } })).content.map((c) => c.text).join('\n');
      check(doc.includes('RenderContext.UseState') && doc.includes('Declares a piece of state'), 'get_api UseState did not give the hook and its doc comment:\n' + doc.slice(0, 500));
      const resources = (await server.request('resources/list')).resources;
      check(resources.length > 0 && resources.every((x) => x.uri.startsWith('isotope://guides/')), 'the MCP server lists no guides as resources');
    } finally {
      await server.close();
    }
    timings.push(['smoke tool run', Date.now() - started]);
    console.log('smoke: the MCP tool from its package listed its 6 tools, gave the docs of UseState and ' + 'listed the guides');
  } catch (e) {
    failure = e;
  } finally {
    for (const [dir, what] of [[nugetPackages, 'packages'], [toolPath, 'tool']]) {
      if (!dir) continue;
      try { rmSync(dir, { recursive: true, force: true }); }
      catch (e) { console.warn('pack: could not delete the smoke ' + what + ' folder (' + e.code + '); the OS temp folder keeps it'); }
    }
  }
  if (failure) fail(failure.message);
}

for (const [what, ms] of timings) console.log('time ' + what + ': ' + (ms / 1000).toFixed(1) + ' s');
console.log('pack: ok, version ' + version + (opts.smoke ? ', smoke passed' : ', smoke skipped'));
