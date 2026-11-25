#!/usr/bin/env node
// Spike J (#75): builds the vendor library and its tests against Isotope's packed packages only, runs the tests on each
// framework, and builds the probes, checking each against Probes/probes.json. Exit 0 when everything is as expected.
//
//   node spikes/J-third-party/run.mjs [--packages <folder>]
//
// --packages: a folder already packed by tools/packaging/pack.mjs (--out). Without it, this packs the branch into a new
// temporary folder first (pack.mjs --no-smoke). Restores go to a fresh temporary NUGET_PACKAGES, so nothing restored
// before (another build of the same version) is used, and nuget.config takes Isotope.* from that folder alone.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..', '..');
const FRAMEWORKS = ['net9.0', 'net10.0', 'net11.0'];
// The tests each framework must run (and pass): a changed count is a changed spike.
const EXPECTED_TESTS = 34;

const args = process.argv.slice(2);
let packages;
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--packages' && args[i + 1]) packages = path.resolve(args[++i]);
  else { console.error('usage: node spikes/J-third-party/run.mjs [--packages <folder>]'); process.exit(2); }
}

const temps = [];
const temp = (prefix) => { const d = mkdtempSync(path.join(tmpdir(), prefix)); temps.push(d); return d; };
function run(cmd, argv, { cwd = HERE, env = {}, quiet = false } = {}) {
  const started = Date.now();
  const r = spawnSync(cmd, argv, { cwd, env: { ...process.env, ...env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', AVALONIA_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' }, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error) throw r.error;
  const out = (r.stdout ?? '') + (r.stderr ?? '');
  if (!quiet) process.stdout.write(out);
  return { status: r.status, out, ms: Date.now() - started };
}

const problems = [];
const lines = [];
try {
  if (!packages) {
    packages = temp('isotope-spike-j-packages-');
    const r = run('node', [path.join('tools', 'packaging', 'pack.mjs'), '--out', packages, '--clean', '--no-smoke'], { cwd: REPO });
    if (r.status !== 0) throw new Error('pack.mjs failed (exit ' + r.status + ')');
  }
  const versions = readdirSync(packages).map((n) => /^Isotope\.UI\.Avalonia\.(\d.*)\.nupkg$/i.exec(n)?.[1]).filter(Boolean);
  if (versions.length !== 1) throw new Error((versions.length ? 'more than one' : 'no') + ' Isotope.UI.Avalonia package in ' + packages + ': pack into an empty folder');
  const version = versions[0];
  const env = { IsotopeVersion: version, ISOTOPE_PACKAGES: packages, NUGET_PACKAGES: temp('isotope-spike-j-nuget-') };
  console.log('spike J: Isotope ' + version);

  // 1. The tests, on each framework. No build servers: one kept alive would hold DLLs in the packages folder.
  let r = run('dotnet', ['build', 'Vendor.Controls.Tests', '--no-incremental', '--disable-build-servers', '-nologo', '-v:q'], { env });
  if (r.status !== 0) throw new Error('the vendor library or its tests did not build (exit ' + r.status + ')');
  r = run('dotnet', ['test', 'Vendor.Controls.Tests', '--no-build', '-nologo', '-v:q', '--logger', 'console;verbosity=minimal'], { env });
  for (const framework of FRAMEWORKS) {
    const m = new RegExp('Failed:\\s*(\\d+), Passed:\\s*(\\d+), Skipped:\\s*(\\d+), Total:\\s*(\\d+).*\\(' + framework.replace('.', '\\.') + '\\)').exec(r.out);
    if (!m) { problems.push('tests: no result for ' + framework); continue; }
    const [failed, passed, , total] = m.slice(1).map(Number);
    lines.push('tests ' + framework + ': ' + passed + ' of ' + total + ' passed');
    if (failed !== 0 || total !== EXPECTED_TESTS) problems.push('tests ' + framework + ': ' + failed + ' failed, ' + total + ' run (' + EXPECTED_TESTS + ' expected)');
  }
  if (r.status !== 0 && !problems.length) problems.push('dotnet test exited ' + r.status);

  // 2. The probes, each built on its own, with the generated sources kept for inspection.
  const spec = JSON.parse(readFileSync(path.join(HERE, 'Probes', 'probes.json'), 'utf8'));
  for (const probe of spec.probes) {
    const dir = path.join(HERE, 'Probes', probe.name);
    // An earlier build's generated sources would stand in for this one's.
    for (const framework of FRAMEWORKS) rmSync(path.join(dir, 'obj', 'Debug', framework, 'generated'), { recursive: true, force: true });
    r = run('dotnet', ['build', dir, '--no-incremental', '--disable-build-servers', '-nologo', '-v:q', '-p:EmitCompilerGeneratedFiles=true'], { env, quiet: true });
    // "<file>(l,c): error ID: message [<project>::TargetFramework=<tf>]"
    const found = [...r.out.matchAll(/^(.*?)\(\d+,\d+\): (error|warning) ([A-Z]+\d+):.*::TargetFramework=(net[\d.]+)\]\s*$/gm)]
      .map((x) => ({ file: x[1], severity: x[2], id: x[3], framework: x[4] }));
    const ok = [];
    for (const framework of FRAMEWORKS) {
      const here = found.filter((d) => d.framework === framework);
      if (probe.expect === 'pass') {
        if (r.status !== 0 || here.length) {
          problems.push('probe ' + probe.name + ' ' + framework + ': expected a clean build, got ' + (here.map((d) => d.id).join(', ') || 'exit ' + r.status));
          continue;
        }
        const gen = path.join(dir, 'obj', 'Debug', framework, 'generated');
        const text = existsSync(gen) ? walk(gen).map((f) => readFileSync(f, 'utf8')).join('\n') : '';
        if (!text.includes('partial record ' + probe.record) || !text.includes('Descriptor')) problems.push('probe ' + probe.name + ' ' + framework + ': no generated descriptor for ' + probe.record);
        else if (probe.generatedLacks && text.includes(probe.generatedLacks)) problems.push('probe ' + probe.name + ' ' + framework + ': the generated sources hold ' + probe.generatedLacks);
        else ok.push(framework);
        continue;
      }
      const missing = probe.diagnostics.filter((id) => !here.some((d) => d.id === id && d.severity === 'error' && (!probe.inGenerated || d.file.endsWith('.g.cs'))));
      const inGenerated = probe.atRecord ? here.filter((d) => d.severity === 'error' && d.file.endsWith('.g.cs')).map((d) => d.id) : [];
      if (r.status === 0 || missing.length) problems.push('probe ' + probe.name + ' ' + framework + ': expected ' + probe.diagnostics.join(', ') + (probe.inGenerated ? ' in a generated file' : '') + ', missing ' + (missing.join(', ') || 'a failing exit'));
      else if (inGenerated.length) problems.push('probe ' + probe.name + ' ' + framework + ': expected errors at the record only, got ' + inGenerated.join(', ') + ' in a generated file');
      else ok.push(framework);
    }
    const ids = [...new Set(found.map((d) => d.id))].join(', ') || 'none';
    lines.push('probe ' + probe.name + ': ' + (probe.expect === 'pass' ? 'builds, ' + (probe.generatedLacks ? probe.generatedLacks + ' not generated' : 'clean') : 'fails with ' + ids) + ' (as expected on ' + (ok.join(', ') || 'none') + ')');
  }
} catch (e) {
  problems.push(e.message);
} finally {
  // Avalonia's build telemetry (Avalonia.BuildServices) can still hold a DLL in the packages folder for a moment after a
  // build, so retry, and keep the folder (as pack.mjs does) rather than fail a run that passed.
  for (const d of temps.reverse()) {
    try { rmSync(d, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 }); }
    catch (e) { console.warn('spike J: could not delete ' + path.basename(d) + ' (' + e.code + '); the OS temp folder keeps it'); }
  }
}

function walk(dir) {
  return readdirSync(dir).flatMap((n) => { const p = path.join(dir, n); return statSync(p).isDirectory() ? walk(p) : [p]; });
}

console.log('');
for (const l of lines) console.log(l);
// exitCode, not exit(): exit() can drop output still queued for a pipe.
if (problems.length) {
  for (const p of problems) console.error('problem: ' + p);
  process.exitCode = 1;
} else console.log('spike J: all as expected');
