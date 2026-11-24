#!/usr/bin/env node
// Measures the Reactor -> Isotope migration (#33) on upstream's samples, read-only input from the upstream cache.
//
//   node tools/migration/measure-samples.mjs --out <empty folder outside the repo> [--samples <dir>] [--passes 4] [--only <name>]
//
// For each sample project it:
//   1. copies the project's folder from <samples> (default: upstream/samples) to <out>, and writes an Isotope project
//      file in place of Reactor's (the project-file step: net10.0, no UseWinUI, Isotope's library, desktop startup and
//      the migration analyzer instead of Reactor; other upstream projects and NuGet packages are dropped and listed);
//   2. compiles it as it is ("before"), then runs `dotnet format analyzers` with the fixable migration rules until no
//      file changes, then compiles it again ("after");
//   3. counts, per file, compile errors and the migration diagnostics left (tools/migration/MigrationReport).
// A file "builds" when the compiler reports no error in it. Prints a summary (no local paths) and writes
// <out>/summary.json. The repo and the upstream cache are only read; the script checks git status is unchanged.
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = parseArgs(process.argv.slice(2));
const samples = path.resolve(args.samples ?? path.join(repo, 'upstream', 'samples'));
const out = args.out ? path.resolve(args.out) : fail('--out <folder> is required');
const passes = Number(args.passes ?? 4);
const fixable = ['ISOMIG001', 'ISOMIG003', 'ISOMIG004', 'ISOMIG006', 'ISOMIG008', 'ISOMIG009', 'ISOMIG014'];

for (const forbidden of [repo, samples])
  if (isInside(out, forbidden)) fail('--out must be outside the repo and the samples folder');
if (!fs.existsSync(samples)) fail('no samples folder (the upstream cache is missing)');
if (fs.existsSync(out) && fs.readdirSync(out).length > 0) fail('--out must be empty or missing');

const statusBefore = gitStatus();
fs.mkdirSync(out, { recursive: true });
const copy = path.join(out, 'samples');
fs.cpSync(samples, copy, { recursive: true, filter: src => !/[\\/](bin|obj|\.vs)$/.test(src) });
// Upstream's Directory.Build.* chain to upstream's own repo; the converted projects stand alone.
for (const f of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
  fs.writeFileSync(path.join(out, f), '<Project />\n');
  if (fs.existsSync(path.join(copy, f))) fs.writeFileSync(path.join(copy, f), '<Project />\n');
}

let projects = walk(copy).filter(f => f.endsWith('.csproj')).sort();
if (args.only) projects = projects.filter(p => path.basename(p, '.csproj') === args.only);
// Referenced sample projects first (Chat.Model and Chat.UI before the chat app), so they are migrated and built
// before the projects that use them.
const converted = order(projects.map(convert));

run('dotnet', ['build', path.join(repo, 'tools', 'migration', 'MigrationReport'), '-nologo', '-v:q', '-c', 'Release'], repo, true);
run('dotnet', ['build', path.join(repo, 'src', 'Isotope.UI.Avalonia.Desktop'), '-nologo', '-v:q'], repo, true);
run('dotnet', ['build', path.join(repo, 'src', 'Isotope.UI.Migration'), '-nologo', '-v:q'], repo, true);
const reporter = path.join(repo, 'tools', 'migration', 'MigrationReport', 'bin', 'Release', 'net10.0', 'MigrationReport.dll');

const results = [];
for (const p of converted) {
  const dir = path.dirname(p.file);
  const name = rel(p.file);
  process.stderr.write(`${name}\n`);
  const before = report(p.file);
  let formatRuns = 0;
  for (let i = 0; i < passes; i++) {
    const hash = hashTree(dir);
    // dotnet format skips referenced projects, so only this project's files change.
    run('dotnet', ['format', 'analyzers', p.file, '--no-restore', '--severity', 'warn', '--diagnostics', ...fixable], dir, false);
    formatRuns++;
    if (hashTree(dir) === hash) break;
  }
  const after = report(p.file);
  results.push({ project: name, dropped: p.dropped, formatRuns, projectBuildsBefore: before.builds, projectBuilds: after.builds, before, after });
}

const summary = summarise(results);
fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify({ results, summary }, null, 2));
print(results, summary);
if (gitStatus() !== statusBefore) fail('git status of the repo changed during the run');

// ── project-file step ───────────────────────────────────────────────────────────────────────────────

function convert(file) {
  const xml = fs.readFileSync(file, 'utf8');
  const prop = name => xml.match(new RegExp(`<${name}>([^<]*)</${name}>`))?.[1];
  const dir = path.dirname(file);
  const dropped = [];
  const keptRefs = [];
  for (const m of xml.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
    const target = path.resolve(dir, m[1].replace(/\\/g, '/'));
    if (isInside(target, copy) && fs.existsSync(target)) keptRefs.push(m[1]);
    else dropped.push(path.basename(m[1].replace(/\\/g, '/'), '.csproj'));
  }
  for (const m of xml.matchAll(/<PackageReference\s+Include="([^"]+)"/g)) dropped.push('package ' + m[1]);
  const removes = [...xml.matchAll(/<Compile\s+Remove="([^"]+)"\s*\/>/g)].map(m => m[1]);
  const outputType = /Exe$/.test(prop('OutputType') ?? '') ? 'Exe' : 'Library';
  const lang = prop('LangVersion') ?? '15';
  const src = path.join(repo, 'src');
  const lines = [
    '<Project Sdk="Microsoft.NET.Sdk">',
    '  <!-- Converted from a Reactor sample by tools/migration/measure-samples.mjs. -->',
    '  <PropertyGroup>',
    `    <OutputType>${outputType}</OutputType>`,
    '    <TargetFramework>net10.0</TargetFramework>',
    `    <ImplicitUsings>${prop('ImplicitUsings') ?? 'disable'}</ImplicitUsings>`,
    `    <Nullable>${prop('Nullable') ?? 'disable'}</Nullable>`,
    `    <LangVersion>${lang}</LangVersion>`,
    ...['RootNamespace', 'AssemblyName', 'AllowUnsafeBlocks'].filter(n => prop(n)).map(n => `    <${n}>${prop(n)}</${n}>`),
    '  </PropertyGroup>',
    '  <ItemGroup>',
    `    <ProjectReference Include="${path.join(src, 'Isotope.UI.Avalonia', 'Isotope.UI.Avalonia.csproj')}" />`,
    `    <ProjectReference Include="${path.join(src, 'Isotope.UI.Avalonia.Desktop', 'Isotope.UI.Avalonia.Desktop.csproj')}" />`,
    `    <ProjectReference Include="${path.join(src, 'Isotope.UI.Migration', 'Isotope.UI.Migration.csproj')}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`,
    ...keptRefs.map(r => `    <ProjectReference Include="${r}" />`),
    ...removes.map(r => `    <Compile Remove="${r}" />`),
    '  </ItemGroup>',
    '</Project>',
    '',
  ];
  fs.writeFileSync(file, lines.join('\n'));
  return { file, dropped, refs: keptRefs.map(r => path.resolve(dir, r.replace(/\\/g, '/'))) };
}

function order(list) {
  const done = new Set();
  const result = [];
  const visit = p => {
    if (done.has(p.file)) return;
    done.add(p.file);
    for (const r of p.refs) {
      const dep = list.find(q => path.resolve(q.file) === r);
      if (dep) visit(dep);
    }
    result.push(p);
  };
  list.forEach(visit);
  return result;
}

// ── measuring ──────────────────────────────────────────────────────────────────────────────────────

function report(project) {
  const rep = run('dotnet', [reporter, project, '--json'], path.dirname(project), true);
  return JSON.parse(rep.stdout.trim().split('\n').pop());
}

function summarise(results) {
  const total = { projects: results.length, projectsBuildBefore: 0, projectsBuild: 0, files: 0, filesBuildBefore: 0, filesBuildAfter: 0,
    errorsBefore: 0, errorsAfter: 0, migrationLeft: {}, migrationBefore: {}, migrationMessagesLeft: {}, errorsLeft: {},
    // Files that compiled before the fixes and don't after: what the migration broke.
    brokenByMigration: [] };
  for (const r of results) {
    if (r.projectBuilds) total.projectsBuild++;
    if (r.projectBuildsBefore) total.projectsBuildBefore++;
    add(total.migrationMessagesLeft, r.after.migrationMessages);
    add(total.errorsLeft, r.after.errorMessages);
    for (const [file, f] of Object.entries(r.after.files)) {
      total.files++;
      const b = r.before.files[file];
      const errorsBefore = b ? sum(b.errors) : 0;
      if (b && errorsBefore === 0) total.filesBuildBefore++;
      if (b && errorsBefore === 0 && sum(f.errors) > 0) total.brokenByMigration.push(r.project.replace(/[^/]+$/, '') + file);
      if (sum(f.errors) === 0) total.filesBuildAfter++;
      total.errorsBefore += errorsBefore;
      total.errorsAfter += sum(f.errors);
      add(total.migrationLeft, f.migration);
      if (b) add(total.migrationBefore, b.migration);
    }
  }
  return total;
}

function print(results, s) {
  const rows = results.map(r => {
    const files = Object.values(r.after.files);
    const before = Object.values(r.before.files);
    return `| ${r.project} | ${files.length} | ${before.filter(f => sum(f.errors) === 0).length} | ${files.filter(f => sum(f.errors) === 0).length} | ${sumAll(before, 'errors')} | ${sumAll(files, 'errors')} | ${sumAll(files, 'migration')} | ${r.projectBuilds ? 'yes' : 'no'} | ${r.dropped.join(', ')} |`;
  });
  console.log('| Project | Files | Build before | Build after | Errors before | Errors after | Migration diagnostics left | Project builds | Dropped references |');
  console.log('|---|---|---|---|---|---|---|---|---|');
  for (const row of rows) console.log(row);
  console.log(`\nprojects ${s.projects}, projects that build: before ${s.projectsBuildBefore}, after ${s.projectsBuild}`);
  console.log(`files ${s.files}, files that build: before ${s.filesBuildBefore}, after ${s.filesBuildAfter}`);
  console.log(`compile errors: before ${s.errorsBefore}, after ${s.errorsAfter}`);
  console.log(`files that built before and not after: ${s.brokenByMigration.length} ${s.brokenByMigration.join(', ')}`);
  console.log('migration diagnostics before: ' + JSON.stringify(sorted(s.migrationBefore)));
  console.log('migration diagnostics left: ' + JSON.stringify(sorted(s.migrationLeft)));
  console.log('\ncommonest migration diagnostics left:');
  for (const [m, n] of top(s.migrationMessagesLeft, 40)) console.log(`  ${n} ${m}`);
  console.log('\ncommonest compile errors left (ID and the name they are about):');
  for (const [m, n] of top(s.errorsLeft, 40)) console.log(`  ${n} ${m}`);
}

function top(counts, n) {
  return Object.entries(counts).sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)).slice(0, n);
}

// ── helpers ────────────────────────────────────────────────────────────────────────────────────────

function run(cmd, argv, cwd, mustSucceed) {
  const r = spawnSync(cmd, argv, { cwd, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
  if (mustSucceed && r.status !== 0) fail(`${cmd} ${argv[0]} failed (${r.status}):\n${(r.stdout ?? '').slice(-4000)}${r.stderr ?? ''}`);
  return r;
}

function hashTree(dir) {
  const h = createHash('sha256');
  for (const f of walk(dir).filter(f => f.endsWith('.cs')).sort()) h.update(f).update(fs.readFileSync(f));
  return h.digest('hex');
}

function walk(dir) {
  const result = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory() && /^(bin|obj|\.vs)$/.test(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) result.push(...walk(p)); else result.push(p);
  }
  return result;
}

function gitStatus() {
  return run('git', ['-C', repo, 'status', '--porcelain'], repo, true).stdout;
}

function isInside(p, dir) {
  const r = path.relative(path.resolve(dir), path.resolve(p));
  return r === '' || (!r.startsWith('..') && !path.isAbsolute(r));
}

function sum(counts) { return Object.values(counts).reduce((a, b) => a + b, 0); }
function sumAll(files, key) { return files.reduce((a, f) => a + sum(f[key]), 0); }
function rel(f) { return path.relative(copy, f).replace(/\\/g, '/'); }
function add(into, counts) { for (const [k, v] of Object.entries(counts)) into[k] = (into[k] ?? 0) + v; }
function sorted(o) { return Object.fromEntries(Object.entries(o).sort()); }

function parseArgs(argv) {
  const o = {};
  for (let i = 0; i < argv.length; i++) {
    const m = argv[i].match(/^--(\w+)$/);
    if (!m) fail(`unexpected argument ${argv[i]}`);
    o[m[1]] = argv[++i];
  }
  return o;
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
