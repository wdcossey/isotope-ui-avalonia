// The MigrationReport workers of measure-samples.mjs (#87): each is one `MigrationReport --serve` process that answers
// requests one JSON line at a time (tools/migration/MigrationReport/Worker.cs), so a run starts dotnet, Roslyn and
// MSBuild's workspace once per worker instead of once per report and per dotnet format run.
// test/workers.test.mjs tests these against a stand-in worker, so no dotnet is needed.
import { spawn } from 'node:child_process';

// The most of a worker's stderr kept for an error message.
const KEEP = 64 * 1024;

// A started worker. request(object) sends one request and resolves to its answer (the parsed line). It rejects when the
// worker ends before it answers (with the end of its stderr), or answers with a line that isn't JSON.
export class Worker {
  constructor(child) {
    this.child = child;
    this.pending = [];
    this.out = '';
    this.err = '';
    this.exited = null;
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => this.#read(chunk));
    child.stderr.on('data', chunk => { this.err = (this.err + chunk).slice(-KEEP); });
    // A write to a worker that has ended fails here rather than crashing the run; the exit handler rejects the request.
    child.stdin.on('error', () => {});
    const end = why => {
      if (this.exited) return;
      this.exited = why;
      for (const p of this.pending.splice(0)) p.reject(new Error('the worker ' + why + ' before it answered' + this.#tail()));
    };
    child.on('error', e => end('failed to start (' + (e.code ?? e.message) + ')'));
    child.on('close', (code, signal) => end(signal ? 'was stopped (' + signal + ')' : 'exited (' + code + ')'));
  }

  request(message) {
    if (this.exited) return Promise.reject(new Error('the worker ' + this.exited + this.#tail()));
    return new Promise((resolve, reject) => {
      this.pending.push({ resolve, reject });
      this.child.stdin.write(JSON.stringify(message) + '\n');
    });
  }

  // Ends the worker (it exits when its stdin closes); resolves once it has.
  close() {
    if (this.exited) return Promise.resolve();
    return new Promise(resolve => { this.child.once('close', () => resolve()); this.child.stdin.end(); });
  }

  kill() { if (!this.exited) this.child.kill(); }

  #read(chunk) {
    this.out += chunk;
    let at;
    while ((at = this.out.indexOf('\n')) >= 0) {
      const line = this.out.slice(0, at).replace(/\r$/, '');
      this.out = this.out.slice(at + 1);
      if (!line.trim()) continue;
      const p = this.pending.shift();
      if (!p) continue;
      try { p.resolve(JSON.parse(line)); }
      catch { p.reject(new Error('the worker answered with a line that is not JSON: ' + line.slice(0, 200))); }
    }
  }

  #tail() {
    const t = this.err.trim();
    return t ? ':\n' + t.slice(-4000) : '';
  }
}

// Workers started on demand. use(work) runs work(lease) with an idle worker, or a new one from start(), and returns the
// worker to the idle ones after, unless it ended; lease.request(message) is the worker's request, on a new worker when
// the one leased has ended (a worker that crashed on one request answers the next). The live workers are in all.
export function pool(start) {
  const idle = [];
  const all = new Set();
  const started = () => { const w = start(); all.add(w); return w; };
  return {
    all,
    async use(work) {
      let worker = idle.pop() ?? started();
      const lease = {
        request(message) {
          if (worker.exited) { all.delete(worker); worker = started(); }
          return worker.request(message);
        },
      };
      try { return await work(lease); }
      finally {
        if (worker.exited) all.delete(worker);
        else idle.push(worker);
      }
    },
    closeAll: () => Promise.all([...all].map(w => w.close())),
    killAll: () => { for (const w of all) w.kill(); },
  };
}

// Starts `dotnet <reporter dll> --serve` as a worker.
export function startReporter(reporterDll, { cwd, env }) {
  return new Worker(spawn('dotnet', [reporterDll, '--serve'], { cwd, env, stdio: ['pipe', 'pipe', 'pipe'] }));
}
