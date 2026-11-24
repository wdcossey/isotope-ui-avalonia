#!/usr/bin/env node
// Packs the three packages and proves them (#52): node tools/packaging/pack.mjs [--out <dir>] [--expect-version <v>]
// [--no-smoke]. CI runs it on every push, and the release workflow before it publishes; docs/releasing.md has the rest.
//
// 1. dotnet pack of each package (Release, ContinuousIntegrationBuild so no local path reaches a DLL or PDB), into
//    --out (default bin/packages under the repo root, which git ignores). Old packages there are deleted first.
// 2. The checks in lib/checks.mjs: layout, metadata, dependencies, symbols and SourceLink, personal data, local paths.
// 3. The smoke test (tests/Isotope.UI.PackageSmoke): an app built against the packed packages alone, from a fresh
//    packages folder, run headless; and the migration analyzer, from its package, run by dotnet format.
// The version comes from MinVer (git tags); --expect-version fails the run if it is another.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, realpathSync } from 'node:fs';
import { homedir, tmpdir, userInfo } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PACKAGES, checkPackages, parseNuspec, regions } from './lib/checks.mjs';
import { readZip } from './lib/zip.mjs';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const SMOKE = path.join(REPO, 'tests', 'Isotope.UI.PackageSmoke');
const USAGE = 'usage: node tools/packaging/pack.mjs [--out <dir>] [--expect-version <version>] [--no-smoke]';

function parseArgs(argv) {
  const opts = { out: path.join(REPO, 'bin', 'packages'), smoke: true };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out' && argv[i + 1]) opts.out = path.resolve(argv[++i]);
    else if (a === '--expect-version' && argv[i + 1]) opts.expectVersion = argv[++i].replace(/^v/, '');
    else if (a === '--no-smoke') opts.smoke = false;
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

// 1. Pack, into a folder that holds nothing but packages.
mkdirSync(opts.out, { recursive: true });
for (const name of readdirSync(opts.out)) {
  if (!/\.s?nupkg$/i.test(name)) fail(opts.out + ' holds other files than packages (' + name + '); give another --out');
  rmSync(path.join(opts.out, name));
}
for (const spec of PACKAGES) {
  const r = run('dotnet', ['pack', spec.project, '-c', 'Release', '-o', opts.out, '-nologo', '-v:q', '-p:ContinuousIntegrationBuild=true']);
  timings.push(['pack ' + spec.id, r.ms]);
  if (r.status !== 0) fail('dotnet pack ' + spec.project + ' failed (exit ' + r.status + ')');
}

// 2. Check.
const read = (pattern) => readdirSync(opts.out).filter((n) => pattern.test(n)).sort()
  .map((file) => ({ file, entries: readZip(readFileSync(path.join(opts.out, file))) }));
const packages = read(/\.nupkg$/i), symbols = read(/\.snupkg$/i);
const appSources = readdirSync(path.join(SMOKE, 'App')).filter((n) => n.endsWith('.cs')).map((n) => readFileSync(path.join(SMOKE, 'App', n), 'utf8'));
const realRepo = realpathSync(REPO);
const problems = checkPackages({
  packages,
  symbols,
  expectVersion: opts.expectVersion,
  localPaths: [...new Set([REPO, realRepo, homedir()])],
  identities: [gitConfig('user.name'), gitConfig('user.email'), userInfo().username].filter(Boolean),
  regions: regions(appSources),
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
  const env = { IsotopeVersion: version, NUGET_PACKAGES: nugetPackages, RestoreAdditionalProjectSources: opts.out };
  try {
    const app = path.join('tests', 'Isotope.UI.PackageSmoke', 'App');
    // No build servers: an MSBuild node kept alive would hold DLLs in the packages folder, which is deleted after.
    let r = run('dotnet', ['build', app, '-c', 'Release', '-nologo', '-v:q', '--no-incremental', '--disable-build-servers'], { env });
    timings.push(['smoke build', r.ms]);
    if (r.status !== 0) fail('the smoke app did not build against the packages (exit ' + r.status + ')');
    r = run('dotnet', ['run', '--project', app, '-c', 'Release', '--no-build'], { env });
    timings.push(['smoke run', r.ms]);
    if (r.status !== 0) fail('the smoke app failed (exit ' + r.status + ')');

    const migration = path.join('tests', 'Isotope.UI.PackageSmoke', 'Migration', 'Isotope.UI.PackageSmoke.Migration.csproj');
    r = run('dotnet', ['restore', migration, '-nologo', '-v:q', '--disable-build-servers'], { env });
    if (r.status !== 0) fail('the migration smoke project did not restore (exit ' + r.status + ')');
    // --verify-no-changes exits non-zero when a fix would change a file: the analyzer and its fix loaded and fired.
    r = run('dotnet', ['format', 'analyzers', migration, '--no-restore', '--verify-no-changes', '--severity', 'warn', '--diagnostics', 'ISOMIG001'], { env, capture: true });
    timings.push(['smoke migration', r.ms]);
    if (r.status === 0 || !r.out.includes('ISOMIG001')) fail('the migration analyzer from its package reported no ISOMIG001 (exit ' + r.status + ')');
    console.log('smoke: the migration analyzer from its package reported ISOMIG001 and offered its fix');
  } finally {
    try { rmSync(nugetPackages, { recursive: true, force: true }); }
    catch (e) { console.warn('pack: could not delete the smoke packages folder (' + e.code + '); the OS temp folder keeps it'); }
  }
}

for (const [what, ms] of timings) console.log('time ' + what + ': ' + (ms / 1000).toFixed(1) + ' s');
console.log('pack: ok, version ' + version + (opts.smoke ? ', smoke passed' : ', smoke skipped'));
