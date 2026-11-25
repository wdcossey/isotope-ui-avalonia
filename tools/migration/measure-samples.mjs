#!/usr/bin/env node
// Measures the Reactor -> Isotope migration (#33) on upstream's samples, read-only input from the upstream cache.
//
//   node tools/migration/measure-samples.mjs --out <empty folder outside the repo> [--samples <dir>] [--passes 4] [--only <name>]
//       [--check | --write-baseline]
//   node tools/migration/measure-samples.mjs --summary <summary.json of an earlier run> (--check | --write-baseline)
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
//
// The baseline (#71): --check compares the run's counts with tools/migration/samples-baseline.json (lib/baseline.mjs
// says which counts) and fails on any difference, naming each count that got worse, and each that got better so the
// same PR updates the baseline. --write-baseline writes the baseline and the migration guide's counts table from the
// run, and fails when either changed, so an update is a deliberate, reviewed commit. CI runs --check (ci.yml, job
// samples). --summary <file> does either on an earlier run's summary.json without measuring again; it assumes the run
// was at the upstream commit in UPSTREAM.lock.
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { baselineOf, compare, countsOf, withGuideTable } from './lib/baseline.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = parseArgs(process.argv.slice(2));
const baselineFile = path.join(repo, 'tools', 'migration', 'samples-baseline.json');
const guideFile = path.join(repo, 'docs', 'guide', 'migrating-from-reactor.md');
if (args.check && args['write-baseline']) fail('give --check or --write-baseline, not both');
if (args.only && (args.check || args['write-baseline'])) fail('--only measures part of the samples, so it cannot be compared with the baseline');
if (args.summary) {
  if (!args.check && !args['write-baseline']) fail('--summary needs --check or --write-baseline');
  process.exit(gate(JSON.parse(fs.readFileSync(path.resolve(args.summary), 'utf8')).summary));
}
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

// Every project's "before" first, so a sample that uses another is measured against the other's unmigrated code.
const befores = new Map();
for (const p of converted) {
  process.stderr.write(`before: ${rel(p.file)}\n`);
  befores.set(p.file, report(p.file));
}
const results = [];
for (const p of converted) {
  const dir = path.dirname(p.file);
  const name = rel(p.file);
  process.stderr.write(`migrate: ${name}\n`);
  let formatRuns = 0;
  let formatFailures = 0;
  for (let i = 0; i < passes; i++) {
    const hash = hashTree(dir);
    // dotnet format skips referenced projects, so only this project's files change.
    const f = run('dotnet', ['format', 'analyzers', p.file, '--no-restore', '--severity', 'warn', '--diagnostics', ...fixable], dir, false);
    formatRuns++;
    if (f.status !== 0) {
      formatFailures++;
      process.stderr.write(`  dotnet format exited ${f.status}\n`);
    }
    if (hashTree(dir) === hash) break;
  }
  const before = befores.get(p.file);
  const after = report(p.file);
  results.push({ project: name, dropped: p.dropped, formatRuns, formatFailures, projectBuildsBefore: before.builds, projectBuilds: after.builds, before, after });
}

const summary = summarise(results);
fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify({ results, summary }, null, 2));
print(results, summary);
if (gitStatus() !== statusBefore) fail('git status of the repo changed during the run');
if (args.check || args['write-baseline']) process.exitCode = gate(summary);

// ── the baseline (#71) ──────────────────────────────────────────────────────────────────────────────

// Checks or writes the baseline; returns the exit code.
function gate(summary) {
  const upstream = fs.readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8').match(/^baseline=([0-9a-f]{40})\s*$/m)?.[1];
  if (!upstream) fail("UPSTREAM.lock has no valid 'baseline=<40 hex>' line");
  const counts = countsOf(summary);
  const update = 'To update it, run node tools/migration/measure-samples.mjs --out <empty folder> --write-baseline, review the diff of ' +
    'tools/migration/samples-baseline.json and the guide, and commit both in this PR (docs/guide/migrating-from-reactor.md).';
  if (args['write-baseline']) {
    const text = JSON.stringify(baselineOf(counts, upstream), null, 2) + '\n';
    const old = fs.existsSync(baselineFile) ? fs.readFileSync(baselineFile, 'utf8').replace(/\r\n/g, '\n') : '';
    const guide = fs.readFileSync(guideFile, 'utf8');
    const newGuide = withGuideTable(guide, counts);
    if (old === text && newGuide === guide) {
      console.log('baseline: tools/migration/samples-baseline.json and the guide are up to date');
      return 0;
    }
    if (old !== text) fs.writeFileSync(baselineFile, text);
    if (newGuide !== guide) fs.writeFileSync(guideFile, newGuide);
    if (old) printDiff(compare(JSON.parse(old), counts, upstream));
    console.error('baseline: wrote tools/migration/samples-baseline.json and the guide\'s counts: review the diff and commit both (this run fails so the update is deliberate; --check passes on the same counts)');
    return 1;
  }
  const baseline = JSON.parse(fs.readFileSync(baselineFile, 'utf8'));
  const diff = compare(baseline, counts, upstream);
  if (!printDiff(diff)) {
    console.log('baseline: the counts match tools/migration/samples-baseline.json');
    return 0;
  }
  if (diff.worse.length) console.error('baseline: the migration on the samples got worse (' + diff.worse.length + ' count(s) above). Fix it, or if the change is intended, update the baseline and say why in the PR.');
  if (diff.better.length) console.error('baseline: ' + diff.better.length + ' count(s) got better: update the baseline in this PR, so the next change is measured against them.');
  if (diff.changed.length) console.error('baseline: the sample set or the upstream commit changed: update the baseline in this PR.');
  console.error('baseline: ' + update);
  return 1;
}

// Prints the differences; returns whether there were any.
function printDiff({ worse, better, changed }) {
  for (const line of worse) console.error('worse: ' + line);
  for (const line of better) console.error('better: ' + line);
  for (const line of changed) console.error('changed: ' + line);
  return worse.length + better.length + changed.length > 0;
}

// ── project-file step ───────────────────────────────────────────────────────────────────────────────

function convert(file) {
  const xml = fs.readFileSync(file, 'utf8');
  // Properties from unconditional property groups only: a conditional one (ReactorGallery's REACTOR_SOURCEMAP, set
  // only with the source-map generator, which is dropped) can't be evaluated here.
  const unconditional = xml.replace(/<PropertyGroup\s+Condition=[\s\S]*?<\/PropertyGroup>/g, '');
  const prop = name => unconditional.match(new RegExp(`<${name}>([^<]*)</${name}>`))?.[1];
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
    ...['RootNamespace', 'AssemblyName', 'AllowUnsafeBlocks', 'DefineConstants'].filter(n => prop(n)).map(n => `    <${n}>${prop(n)}</${n}>`),
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
    total.formatFailures = (total.formatFailures ?? 0) + r.formatFailures;
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
  console.log(`dotnet format runs that failed: ${s.formatFailures ?? 0}`);
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
    const m = argv[i].match(/^--([\w-]+)$/);
    if (!m) fail(`unexpected argument ${argv[i]}`);
    if (m[1] === 'check' || m[1] === 'write-baseline') o[m[1]] = true;
    else if (i + 1 < argv.length) o[m[1]] = argv[++i];
    else fail(`--${m[1]} needs a value`);
  }
  return o;
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
