// The Roslyn step of tools/sync (issue #4): syntax-aware transforms run by a small .NET tool, tools/sync/roslyn.
// The tool is built on demand (an incremental dotnet build, once per process) and run once per batch of files: a JSON
// request on stdin, a JSON response on stdout. Only syntax is used, never a compilation, so it needs no references.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const TOOL_DIR = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'roslyn');
const PROJECT = path.join(TOOL_DIR, 'Isotope.Sync.Roslyn.csproj');
const DLL = path.join(TOOL_DIR, 'bin', 'Release', 'net10.0', 'Isotope.Sync.Roslyn.dll');
export const OPS = {
  'drop-member': { required: ['member'], optional: ['type', 'parameters'] },
  'retarget-type': { required: ['from', 'to'], optional: [] },
  'drop-attribute': { required: ['attribute'], optional: [] },
};

let built = false;
// dotnet runs from the tool's folder, so it takes this repo's global.json (never upstream's).
const dotnet = (args, input) => spawnSync('dotnet', args, { cwd: TOOL_DIR, input, encoding: 'utf8', maxBuffer: 1 << 28 });

// The tool's sources: its project and C# files (not bin/ or obj/).
const sources = () => readdirSync(TOOL_DIR).filter((f) => /\.(cs|csproj)$/.test(f)).map((f) => path.join(TOOL_DIR, f));

const fresh = () => existsSync(DLL) && sources().every((f) => statSync(f).mtimeMs < statSync(DLL).mtimeMs);
const LOCK = path.join(TOOL_DIR, 'obj', 'isotope-build.lock');
const pause = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

function build() {
  if (built) return;
  // Built already and newer than every source: no dotnet build (it saves the build's start-up).
  if (fresh()) { built = true; return; }
  // Two processes (test files run by node --test) mustn't build into the same bin/ at once: a lock folder, taken
  // atomically, serialises them; the second finds the DLL fresh once it has the lock. A lock older than 10 minutes is
  // a crashed build's, and is taken over.
  mkdirSync(path.dirname(LOCK), { recursive: true });
  for (const start = Date.now(); ;) {
    try { mkdirSync(LOCK); break; } catch (e) {
      if (e.code !== 'EEXIST') throw e;
      try { if (Date.now() - statSync(LOCK).mtimeMs > 600_000) rmSync(LOCK, { recursive: true, force: true }); } catch { /* gone already */ }
      if (Date.now() - start > 900_000) throw new Error('the Roslyn transform tool: another build has held ' + LOCK + ' for 15 minutes');
      pause(250);
    }
  }
  try {
    if (!fresh()) {
      const r = dotnet(['build', PROJECT, '-c', 'Release', '-nologo', '-v:q', '-clp:ErrorsOnly']);
      if (r.error) throw new Error('the Roslyn transform tool needs the .NET SDK (dotnet): ' + r.error.message);
      if (r.status !== 0) throw new Error('the Roslyn transform tool (tools/sync/roslyn) did not build:\n' + (r.stdout + r.stderr).trim());
    }
    built = true;
  } finally {
    rmSync(LOCK, { recursive: true, force: true });
  }
}

/**
 * Runs Roslyn ops on files: [{path, text, ops: [{id, op, ...}]}]. Returns, for each file in order,
 * {path, text, results: [{id, hits, error}], errors}. Throws if the tool can't be built or run.
 */
export function runRoslyn(files) {
  if (!files.length) return [];
  build();
  const r = dotnet([DLL], JSON.stringify({ files }));
  if (r.error) throw new Error('the Roslyn transform tool did not start: ' + r.error.message);
  if (r.status !== 0) throw new Error('the Roslyn transform tool failed (exit ' + r.status + '): ' + (r.stderr || r.stdout).trim());
  const out = JSON.parse(r.stdout).files;
  if (out.length !== files.length || out.some((f, i) => f.path !== files[i].path)) throw new Error('the Roslyn transform tool answered for other files');
  return out;
}
