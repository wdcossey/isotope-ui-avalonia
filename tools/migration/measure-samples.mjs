#!/usr/bin/env node
// Measures the Reactor -> Isotope migration (#33) on upstream's samples, read-only input from the upstream cache.
//
//   node tools/migration/measure-samples.mjs --out <empty folder outside the repo> [--samples <dir>] [--passes 4] [--only <name>]
//       [--jobs <n>] [--fixer in-process|dotnet-format] [--check | --write-baseline]
//   node tools/migration/measure-samples.mjs --summary <summary.json of an earlier run> (--check | --write-baseline)
//
// For each sample project it:
//   1. copies the project's folder from <samples> (default: upstream/samples) to <out>, and writes an Isotope project
//      file in place of Reactor's (the project-file step: net10.0, no UseWinUI, Isotope's library, desktop startup and
//      the migration analyzer instead of Reactor; other upstream projects and NuGet packages are dropped and listed);
//   2. compiles it as it is ("before"), then applies the fixable migration rules' code fixes until no file changes (at
//      most --passes times), then compiles it again ("after");
//   3. counts, per file, compile errors and the migration diagnostics left (tools/migration/MigrationReport).
// The fixes run in MigrationReport's workers (#87, lib/workers.mjs), each one process that loads the project with
// MSBuild's workspace and does what one `dotnet format analyzers --severity warn --diagnostics <fixable>` run does
// (MigrationReport/Fixer.cs). --fixer dotnet-format runs dotnet format itself instead, as before #87: the counts are the
// same, and it is how to show they still are.
// Up to --jobs projects (default: the processor count) are measured at once, each after the projects it references; the
// output doesn't depend on it, and <out>/timings.json says where the time went (formatMs: each pass of the fixes).
// A file "builds" when the compiler reports no error in it. Prints a summary (no local paths) and writes
// <out>/summary.json. The repo and the upstream cache are only read; the script checks git status is unchanged.
//
// The baseline (#71): --check compares the run's counts with tools/migration/samples-baseline.json (lib/baseline.mjs
// says which counts) and fails on any difference, naming each count that got worse, and each that got better so the
// same PR updates the baseline. --write-baseline writes the baseline and the migration guide's counts table from the
// run, and fails when either changed, so an update is a deliberate, reviewed commit. CI runs --check (ci.yml, job
// samples). --summary <file> does either on an earlier run's summary.json without measuring again. Both refuse a run
// measured otherwise than the baseline: another upstream commit, a changed upstream/, another samples folder, --only or
// --passes (summary.json records how it was measured).
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { baselineOf, compare, countsOf, withGuideTable } from './lib/baseline.mjs';
import { pool, startReporter } from './lib/workers.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = parseArgs(process.argv.slice(2));
// MSBuild node reuse is off for every child: concurrent builds then share no long-lived MSBuild node, and none is left
// running after the script.
const childEnv = { ...process.env, MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
// The children running now, stopped when the run fails, so none outlives it.
const children = new Set();

// The baseline and the guide; the tests point these at copies (SAMPLES_BASELINE_FILE, SAMPLES_GUIDE_FILE).
const baselineFile = path.resolve(process.env.SAMPLES_BASELINE_FILE ?? path.join(repo, 'tools', 'migration', 'samples-baseline.json'));
const guideFile = path.resolve(process.env.SAMPLES_GUIDE_FILE ?? path.join(repo, 'docs', 'guide', 'migrating-from-reactor.md'));
const lockCommit = fs.readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8').match(/^baseline=([0-9a-f]{40})(?![0-9a-f])/m)?.[1]; // as ci.yml's sed reads it
const gating = args.check || args['write-baseline'];
if (args.check && args['write-baseline']) fail('give --check or --write-baseline, not both');
if (gating && !lockCommit) fail("UPSTREAM.lock has no valid 'baseline=<40 hex>' line");
// What applies the fixes: MigrationReport's workers, or dotnet format as before #87.
const fixer = args.fixer ?? 'in-process';
if (fixer !== 'in-process' && fixer !== 'dotnet-format') fail('--fixer is in-process or dotnet-format');
if (args.summary) {
  if (!gating) fail('--summary needs --check or --write-baseline');
  for (const o of ['out', 'samples', 'passes', 'only', 'jobs', 'fixer']) if (args[o] !== undefined) fail(`--summary measures nothing, so --${o} does nothing`);
  let earlier;
  try { earlier = JSON.parse(fs.readFileSync(path.resolve(args.summary), 'utf8')); }
  catch (e) { fail('cannot read that summary.json (' + (e.code ?? e.message) + ')'); }
  const problem = notComparable(earlier.measured);
  if (problem) fail('that summary.json ' + problem);
  // gate() writes synchronously, so exiting straight after loses no output.
  process.exit(gate(earlier.summary));
}
const samples = path.resolve(args.samples ?? path.join(repo, 'upstream', 'samples'));
const out = args.out ? path.resolve(args.out) : fail('--out <folder> is required');
const passes = Number(args.passes ?? 4);
// Projects measured at once. The counts don't depend on it: a project starts only after the projects it references.
const jobs = Math.max(1, Math.floor(Number(args.jobs ?? os.availableParallelism())) || 1);
// How this run measures, saved in summary.json: the baseline is of the whole upstream sample set, at UPSTREAM.lock's
// commit, with the default passes, so a run measured otherwise is refused before it starts (and --summary refuses its file).
const measured = { upstream: upstreamCommit(), samples: samples === path.resolve(repo, 'upstream', 'samples') ? 'upstream' : 'other', passes, only: args.only ?? null };
if (gating && notComparable(measured)) fail('this run ' + notComparable(measured));
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
// At most --jobs workers, since each phase runs at most --jobs projects at once; each is started when first needed.
let workerCount = 0;
const workers = pool(() => {
  workerCount++;
  const w = startReporter(reporter, { cwd: out, env: childEnv });
  children.add(w.child);
  w.child.on('close', () => children.delete(w.child));
  return w;
});

// Every project's "before" first, so a sample that uses another is measured against the other's unmigrated code. Each
// phase runs up to --jobs projects at once, a project only after those it references (its build reads their output)
// and those nested in its folder; results stay in the order above, so the output doesn't depend on the timing.
const timings = new Map(converted.map(p => [p.file, { project: rel(p.file), beforeMs: 0, formatMs: [], afterMs: 0 }]));
const phaseMs = {};
let started = Date.now();
const befores = new Map();
await inOrder(converted, p => workers.use(async worker => {
  process.stderr.write(`before: ${rel(p.file)}\n`);
  const t = Date.now();
  befores.set(p.file, await report(worker, p.file));
  timings.get(p.file).beforeMs = Date.now() - t;
}));
phaseMs.before = Date.now() - started;
started = Date.now();
const results = await inOrder(converted, p => workers.use(async worker => {
  const dir = path.dirname(p.file);
  const name = rel(p.file);
  const time = timings.get(p.file);
  process.stderr.write(`migrate: ${name}\n`);
  let formatRuns = 0;
  let formatFailures = 0;
  for (let i = 0; i < passes; i++) {
    const hash = hashTree(dir);
    // Like dotnet format, the fixes skip referenced projects, so only this project's files change.
    let t = Date.now();
    const failure = await fixPass(worker, p.file, dir);
    time.formatMs.push(Date.now() - t);
    formatRuns++;
    if (failure) {
      formatFailures++;
      process.stderr.write(`  ${name}: ${failure}\n`);
    }
    if (hashTree(dir) === hash) break;
  }
  const before = befores.get(p.file);
  const t = Date.now();
  const after = await report(worker, p.file);
  time.afterMs = Date.now() - t;
  return { project: name, dropped: p.dropped, formatRuns, formatFailures, projectBuildsBefore: before.builds, projectBuilds: after.builds, before, after };
}));
await workers.closeAll();
phaseMs.migrate = Date.now() - started;
// Where the time went (timings.json, not summary.json, which holds only what is measured about the migration).
const projectTimings = [...timings.values()];
fs.writeFileSync(path.join(out, 'timings.json'), JSON.stringify({ jobs, fixer, workers: workerCount, phaseMs, projects: projectTimings }, null, 2));
const total = key => projectTimings.reduce((a, t) => a + (Array.isArray(t[key]) ? t[key].reduce((x, y) => x + y, 0) : t[key]), 0);
const formatCount = projectTimings.reduce((a, t) => a + t.formatMs.length, 0);
process.stderr.write(`time (${jobs} at once, ${workerCount} workers): before phase ${s1(phaseMs.before)}, migrate phase ${s1(phaseMs.migrate)}; summed over projects: ` +
  `before builds ${s1(total('beforeMs'))}, ${formatCount} ${fixer === 'in-process' ? 'passes of the fixes' : 'dotnet format runs'} ${s1(total('formatMs'))}, after builds ${s1(total('afterMs'))}\n`);

const summary = summarise(results);
fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify({ measured, results, summary }, null, 2));
print(results, summary);
if (gitStatus() !== statusBefore) fail('git status of the repo changed during the run');
if (gating) process.exitCode = gate(summary);

// ── the baseline (#71) ──────────────────────────────────────────────────────────────────────────────

// Why a run measured this way can't be compared with the baseline, or undefined when it can.
function notComparable(m) {
  if (!m) return 'does not say how it was measured (it predates #71): measure again';
  if (m.only) return 'measures only ' + m.only + ', not the whole sample set';
  if (m.samples !== 'upstream') return 'measures another samples folder than upstream/samples';
  if (m.passes !== 4) return 'runs the fixes ' + m.passes + ' times, not 4';
  if (m.upstream === 'changed') return 'measures an upstream/ with changes in its working tree';
  if (m.upstream !== lockCommit) return 'measures upstream at ' + String(m.upstream).slice(0, 8) + ', and UPSTREAM.lock is ' + lockCommit.slice(0, 8) + ': check out the lock commit in upstream/';
  return undefined;
}

// The commit the upstream cache is at (read-only git), 'changed' when its working tree differs from that commit, or null
// when it isn't a git checkout.
function upstreamCommit() {
  const git = (...a) => spawnSync('git', ['-C', path.join(repo, 'upstream'), ...a], { encoding: 'utf8' });
  const head = git('rev-parse', 'HEAD'), status = git('status', '--porcelain');
  if (head.status !== 0 || status.status !== 0) return null;
  return status.stdout.trim() ? 'changed' : head.stdout.trim();
}

// The gate's output, written synchronously: the --summary path exits straight after it (console's writes to a pipe can
// be asynchronous). It is a few lines, so a full pipe can't stall it the way the long report could.
function say(text) { fs.writeSync(1, text + '\n'); }
function warn(text) { fs.writeSync(2, text + '\n'); }

// Checks or writes the baseline; returns the exit code.
function gate(summary) {
  const upstream = lockCommit;
  const counts = countsOf(summary);
  const update = 'To update it, run node tools/migration/measure-samples.mjs --out <empty folder> --write-baseline, review the diff of ' +
    'tools/migration/samples-baseline.json and the guide, and commit both in this PR (docs/guide/migrating-from-reactor.md).';
  if (args['write-baseline']) {
    const text = JSON.stringify(baselineOf(counts, upstream), null, 2) + '\n';
    // Everything is read and checked before anything is written.
    let old, oldBaseline, guide, newGuide;
    try {
      old = fs.existsSync(baselineFile) ? fs.readFileSync(baselineFile, 'utf8').replace(/\r\n/g, '\n') : '';
      oldBaseline = old ? JSON.parse(old) : undefined;
      guide = fs.readFileSync(guideFile, 'utf8');
      newGuide = withGuideTable(guide, counts);
    } catch (e) {
      warn('baseline: cannot write the baseline: ' + (e.code ?? e.message));
      return 1;
    }
    if (old === text && newGuide === guide) {
      say('baseline: tools/migration/samples-baseline.json and the guide are up to date');
      return 0;
    }
    if (old !== text) fs.writeFileSync(baselineFile, text);
    if (newGuide !== guide) fs.writeFileSync(guideFile, newGuide);
    if (oldBaseline) printDiff(compare(oldBaseline, counts, upstream));
    warn('baseline: wrote tools/migration/samples-baseline.json and the guide\'s counts: review the diff and commit both (this run fails so the update is deliberate; --check passes on the same counts)');
    return 1;
  }
  let baseline;
  try { baseline = JSON.parse(fs.readFileSync(baselineFile, 'utf8')); }
  catch (e) { warn('baseline: cannot read tools/migration/samples-baseline.json (' + (e.code ?? e.message) + '). ' + update); return 1; }
  const diff = compare(baseline, counts, upstream);
  if (!printDiff(diff)) {
    say('baseline: the counts match tools/migration/samples-baseline.json');
    return 0;
  }
  if (diff.worse.length) warn('baseline: the migration on the samples got worse (the "worse" lines above). Fix it, or if the change is intended, update the baseline and say why in the PR.');
  if (diff.better.length) warn('baseline: ' + diff.better.length + ' count(s) got better: update the baseline in this PR, so the next change is measured against them.');
  if (diff.changed.length) warn('baseline: the sample set or the upstream commit changed: update the baseline in this PR.');
  warn('baseline: ' + update);
  return 1;
}

// Prints the differences; returns whether there were any.
function printDiff({ worse, better, changed }) {
  for (const line of worse) warn('worse: ' + line);
  for (const line of better) warn('better: ' + line);
  for (const line of changed) warn('changed: ' + line);
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

// The report of a project, from a worker; the run fails when there is none.
async function report(worker, project) {
  let answer;
  try { answer = await worker.request({ op: 'report', project }); }
  catch (e) { fail(`MigrationReport failed on ${rel(project)}: ${e.message}`); }
  if (!answer.ok) fail(`MigrationReport failed on ${rel(project)}: ${answer.error}`);
  return answer.report;
}

// One pass of the fixes on a project: in a worker, or a dotnet format run (--fixer dotnet-format). Resolves to why it
// failed, or undefined.
async function fixPass(worker, project, dir) {
  if (fixer === 'dotnet-format') {
    const f = await runAsync('dotnet', ['format', 'analyzers', project, '--no-restore', '--severity', 'warn', '--diagnostics', ...fixable], dir, false);
    return f.status === 0 ? undefined : 'dotnet format exited ' + f.status;
  }
  try {
    // The answer's log: the warnings dotnet format would print (a fix that threw, a project that didn't load).
    const answer = await worker.request({ op: 'fix', project, diagnostics: fixable });
    return answer.ok ? undefined : 'the fixes failed' + (answer.error ? ': ' + answer.error : '') + (answer.log?.trim() ? '\n' + answer.log.trim().slice(-4000) : '');
  } catch (e) {
    // A worker that ended: the pool starts another for the next project, as a dotnet format run that crashed.
    return 'the fixes failed: ' + e.message;
  }
}

// Runs work(p) for each project, at most `jobs` at once, each after the projects it depends on (its references, and
// projects in folders under its own). Resolves to the results in the order of list.
function inOrder(list, work) {
  const promises = new Map();
  let active = 0;
  const waiting = [];
  const slot = () => (active < jobs ? (active++, Promise.resolve()) : new Promise(resolve => waiting.push(resolve)));
  const release = () => { const next = waiting.shift(); if (next) next(); else active--; };
  const dependencies = p => list.filter(q => q !== p && (p.refs.includes(path.resolve(q.file)) || isInside(path.dirname(q.file), path.dirname(p.file))));
  const visiting = new Set();
  const start = p => {
    if (visiting.has(p.file)) fail('the sample projects depend on each other in a cycle, through ' + rel(p.file) + ' (references, or a project in a folder under another)');
    if (!promises.has(p.file)) {
      visiting.add(p.file);
      promises.set(p.file, (async () => {
        await Promise.all(dependencies(p).map(start));
        await slot();
        try { return await work(p); } finally { release(); }
      })());
      visiting.delete(p.file);
    }
    return promises.get(p.file);
  };
  return Promise.all(list.map(start));
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
  console.log(`${fixer === 'in-process' ? 'passes of the fixes' : 'dotnet format runs'} that failed: ${s.formatFailures ?? 0}`);
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

function runAsync(cmd, argv, cwd, mustSucceed) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, argv, { cwd, env: childEnv });
    children.add(child);
    const out = [], err = [];
    child.stdout.on('data', d => out.push(d));
    child.stderr.on('data', d => err.push(d));
    child.on('error', reject);
    child.on('close', status => {
      children.delete(child);
      const r = { status, stdout: Buffer.concat(out).toString('utf8'), stderr: Buffer.concat(err).toString('utf8') };
      if (mustSucceed && status !== 0) fail(`${cmd} ${argv[0]} failed (${status}):\n${r.stdout.slice(-4000)}${r.stderr}`);
      resolve(r);
    });
  });
}

function run(cmd, argv, cwd, mustSucceed) {
  const r = spawnSync(cmd, argv, { cwd, env: childEnv, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
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

function s1(ms) { return (ms / 1000).toFixed(1) + ' s'; }
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
    else if (i + 1 < argv.length && !argv[i + 1].startsWith('--')) o[m[1]] = argv[++i];
    else fail(`--${m[1]} needs a value`);
  }
  return o;
}

function fail(message) {
  // Written synchronously, since it exits straight after (console's writes to a pipe can be asynchronous).
  fs.writeSync(2, message + '\n');
  for (const child of children) child.kill();
  process.exit(1);
}
