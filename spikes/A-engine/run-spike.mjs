#!/usr/bin/env node
// Re-runs Spike A's build at a step and prints the grouped errors.
// Usage (from anywhere): node spikes/A-engine/run-spike.mjs [step]   step = 1, 2, 3 or 4 (default 3)
// Step 4 adds the Roslyn rules in roslyn-rules.json (issue #4); its total is in expected-total-step4.txt.
// Every step uses the final file list in EngineFiles.props; see docs/spikes/A-engine-compile.md.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const step = process.argv[2] ?? '3';
mkdirSync(path.join(here, 'logs'), { recursive: true });
const log = path.join(here, 'logs', 'run-step' + step + '.log');
rmSync(log, { force: true });
try {
  execFileSync('dotnet', ['build', path.join(here, 'Reactor.Engine.Spike.csproj'), '-nologo', '-v:q', '-clp:ErrorsOnly',
    '-p:SpikeStep=' + step, '-p:TreatWarningsAsErrors=false', '-flp:logfile=' + log + ';errorsonly'], { stdio: 'ignore' });
} catch (e) {
  if (typeof e.status !== 'number') throw e; // dotnet missing or not started: not a build result
}
if (!existsSync(log)) throw new Error('dotnet build wrote no log: ' + log);
execFileSync('node', [path.join(here, '..', '..', 'tools', 'build-errors.mjs'), log], { stdio: 'inherit' });
