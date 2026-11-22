#!/usr/bin/env node
// Re-runs Spike A's final build (step 3) and prints the grouped errors.
// Usage (from the repo root): node spikes/A-engine/run-spike.mjs [step]   step = 1, 2 or 3 (default 3)
// Steps 1-2 leave out later shim folders and the import rewrite; EngineFiles.props is the final file list.
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const step = process.argv[2] ?? '3';
const log = path.join(here, 'logs', 'run-step' + step + '.log');
try {
  execFileSync('dotnet', ['build', path.join(here, 'Reactor.Engine.Spike.csproj'), '-nologo', '-v:q', '-clp:ErrorsOnly',
    '-p:SpikeStep=' + step, '-p:TreatWarningsAsErrors=false', '-flp:logfile=' + log + ';errorsonly'], { stdio: 'ignore' });
} catch { /* errors expected */ }
execFileSync('node', [path.join(here, '..', '..', 'tools', 'build-errors.mjs'), log], { stdio: 'inherit' });
