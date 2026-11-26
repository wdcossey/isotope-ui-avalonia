// Tests of the MigrationReport workers (#87): node --test tools/migration/test/*.test.mjs. A stand-in worker written in node
// answers as MigrationReport --serve does, so no dotnet is needed.
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Worker, pool } from '../lib/workers.mjs';

// One JSON request per line; each answered with one line, in order, after a line on stderr. "split" answers in two
// writes, "die" exits before answering, "garbage" answers with a line that isn't JSON.
const standIn = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'workers-test-')), 'stand-in.mjs');
fs.writeFileSync(standIn, `
import readline from 'node:readline';
let n = 0;
for await (const line of readline.createInterface({ input: process.stdin })) {
  const m = JSON.parse(line);
  n++;
  process.stderr.write('working on ' + m.op + ' ' + n + '\\n');
  if (m.op === 'die') { process.stderr.write('dying\\n'); process.exit(3); }
  if (m.op === 'garbage') { process.stdout.write('not json\\n'); continue; }
  const answer = JSON.stringify({ ok: true, op: m.op, n, pid: process.pid });
  if (m.op === 'split') {
    process.stdout.write(answer.slice(0, 5));
    await new Promise(r => setTimeout(r, 50));
    process.stdout.write(answer.slice(5) + '\\r\\n');
  } else process.stdout.write(answer + '\\n');
}
`);
// Every stand-in started, stopped after the tests so a failed test leaves none running.
const started = [];
const start = () => { const w = new Worker(spawn(process.execPath, [standIn], { stdio: ['pipe', 'pipe', 'pipe'] })); started.push(w); return w; };
after(() => { for (const w of started) w.kill(); fs.rmSync(path.dirname(standIn), { recursive: true, force: true }); });

test('a worker answers its requests in order, and ends when closed', async () => {
  const w = start();
  const [a, b] = await Promise.all([w.request({ op: 'one' }), w.request({ op: 'two' })]);
  assert.deepEqual([a.op, a.n, b.op, b.n], ['one', 1, 'two', 2]);
  await w.close();
  assert.match(w.exited, /exited \(0\)/);
});

test('an answer written in pieces, ending in CRLF, is one answer', async () => {
  const w = start();
  const answer = await w.request({ op: 'split' });
  assert.deepEqual([answer.ok, answer.op], [true, 'split']);
  await w.close();
});

test('a worker that exits before answering fails its request, and every later one, with its exit code and stderr', async () => {
  const w = start();
  await assert.rejects(w.request({ op: 'die' }), /exited \(3\) before it answered:[\s\S]*dying/);
  await assert.rejects(w.request({ op: 'one' }), /the worker exited \(3\)/);
});

test('an answer that is not JSON fails the request and stops the worker, so no later answer goes to the wrong request', async () => {
  const w = start();
  const [garbage, next] = await Promise.allSettled([w.request({ op: 'garbage' }), w.request({ op: 'one' })]);
  assert.match(garbage.reason.message, /not JSON: not json/);
  assert.match(next.reason.message, /answered with a line that is not JSON/);
  assert.ok(w.exited);
  await assert.rejects(w.request({ op: 'two' }), /answered with a line that is not JSON/);
});

test('a worker that cannot start fails its request instead of the run', async () => {
  const w = new Worker(spawn(path.join(os.tmpdir(), 'no-such-worker-' + process.pid), [], { stdio: ['pipe', 'pipe', 'pipe'] }));
  await assert.rejects(w.request({ op: 'one' }), /failed to start/);
});

test('the pool starts a worker only when none is idle, and reuses idle ones', async () => {
  let count = 0;
  const p = pool(() => { count++; return start(); });
  const pids = await Promise.all([1, 2].map(() => p.use(async lease => (await lease.request({ op: 'one' })).pid)));
  assert.equal(count, 2);
  assert.notEqual(pids[0], pids[1]);
  const again = await p.use(async lease => (await lease.request({ op: 'one' })).pid);
  assert.equal(count, 2);
  assert.ok(pids.includes(again));
  await p.closeAll();
  assert.equal([...p.all].filter(w => !w.exited).length, 0);
});

test('a lease whose worker died gets a new worker for its next request, and the dead one leaves the pool', async () => {
  let count = 0;
  const p = pool(() => { count++; return start(); });
  const answer = await p.use(async lease => {
    await assert.rejects(lease.request({ op: 'die' }));
    return lease.request({ op: 'one' });
  });
  assert.deepEqual([answer.ok, answer.n, count], [true, 1, 2]);
  assert.equal(p.all.size, 1);
  await p.closeAll();
});
