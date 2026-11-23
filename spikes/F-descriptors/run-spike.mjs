#!/usr/bin/env node
// Spike F (#12): builds upstream's descriptor runtime against the library's Avalonia seam and groups the errors.
// Usage (from anywhere): node spikes/F-descriptors/run-spike.mjs [step]   step = 0 or 1 (default 1)
//   0: the library's aliases only; 1: plus DependencyProperty/DependencyObject onto Avalonia (Shims/Step1).
// The library is taken from git at the commit in runtime-files.json ("base", the seam before the swap), so the
// numbers don't move as the library changes; the runtime files come from the upstream/ cache at UPSTREAM.lock with
// tools/sync's repo-wide (glob) rules applied, i.e. as tier 1 would vendor them. Needs upstream/ and git history.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseLock } from '../../tools/sync/lib/lock.mjs';
import { applyTransforms, loadTransforms } from '../../tools/sync/lib/transforms.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.join(here, '..', '..');
const step = process.argv[2] ?? '1';
if (!['0', '1'].includes(step)) { console.error('usage: node spikes/F-descriptors/run-spike.mjs [0|1]'); process.exit(2); }
const git = (cwd, ...args) => execFileSync('git', ['-C', cwd, ...args], { encoding: 'utf8', maxBuffer: 1 << 28 });
const write = (file, text) => { mkdirSync(path.dirname(file), { recursive: true }); writeFileSync(file, text); };

const spec = JSON.parse(readFileSync(path.join(here, 'runtime-files.json'), 'utf8'));
const { baseline } = parseLock(readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8'));
const upstream = path.join(repo, 'upstream');
if (!existsSync(path.join(upstream, '.git'))) throw new Error('needs the upstream/ cache (README, Getting started)');

// The library before the swap: its seam and the vendored files it compiled, from git.
const base = path.join(here, 'obj', 'base');
rmSync(base, { recursive: true, force: true });
const libFiles = git(repo, 'ls-tree', '-r', '--name-only', spec.base, '--', 'src/Isotope.UI.Avalonia', 'src/Upstream/src/Reactor')
  .split('\n').filter(Boolean);
for (const f of libFiles) write(path.join(base, ...f.split('/')), git(repo, 'show', spec.base + ':' + f));

// The runtime files, as tier 1 would vendor them: only the repo-wide rules.
const rules = loadTransforms(JSON.parse(readFileSync(path.join(repo, 'tools', 'sync', 'transforms.json'), 'utf8'))).filter((r) => r.glob);
const rt = path.join(here, 'obj', 'runtime');
rmSync(rt, { recursive: true, force: true });
for (const f of spec.files) {
  const { text, errors } = applyTransforms(rules.filter((r) => r.globRe.test(f)), f, git(upstream, 'show', baseline + ':' + f));
  if (errors.length) throw new Error(errors.join('\n'));
  // #line maps errors back to upstream's path and line numbers.
  write(path.join(rt, ...f.split('/')), '#line 1 "' + path.join(upstream, ...f.split('/')) + '"\n' + text);
}

const log = path.join(here, 'obj', 'logs', 'run-step' + step + '.log');
mkdirSync(path.dirname(log), { recursive: true });
rmSync(log, { force: true });
try {
  execFileSync('dotnet', ['build', path.join(here, 'Descriptor.Runtime.Spike.csproj'), '-nologo', '-v:q', '-clp:ErrorsOnly',
    '-p:SpikeStep=' + step, '-p:TreatWarningsAsErrors=false', '-flp:logfile=' + log + ';errorsonly'], { stdio: 'ignore', cwd: repo });
} catch (e) {
  if (typeof e.status !== 'number') throw e; // dotnet missing or not started: not a build result
}
if (!existsSync(log)) throw new Error('dotnet build wrote no log: ' + log);
execFileSync('node', [path.join(repo, 'tools', 'build-errors.mjs'), log, ...process.argv.slice(3)], { stdio: 'inherit' });
