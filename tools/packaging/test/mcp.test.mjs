// Tests of the smoke test's MCP client (#60): node --test tools/packaging/test/*.test.mjs. Fake servers in node, so no
// dotnet is needed; pack.mjs drives the real tool with it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { initialize, startMcp } from '../lib/mcp.mjs';

const node = (code) => [process.execPath, ['-e', code]];

// Starts a server, runs fn, and always closes the server, so a failing test leaves no process behind. Requests time
// out in 3 s, well inside each test's own timeout.
async function withServer([command, args], fn, options = {}) {
  const server = startMcp(command, args, { timeoutMs: 3000, ...options });
  try { return await fn(server); } finally { await server.close(); }
}

// Answers each request with its method's name; the first line is a log on stderr, as the real server's are.
const ECHO = `process.stderr.write('log line\\n');
let buf = '';
process.stdin.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\\n')) >= 0) { const m = JSON.parse(buf.slice(0, i)); buf = buf.slice(i + 1);
  if (m.id != null) process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: { method: m.method, serverInfo: { name: 'echo' } } }) + '\\n'); } });`;

test('requests are answered by id, and close ends the server', { timeout: 15000 }, async () => {
  const server = await withServer(node(ECHO), async (s) => {
    assert.equal((await initialize(s)).serverInfo.name, 'echo');
    const [a, b] = await Promise.all([s.request('tools/list'), s.request('resources/list')]);
    assert.deepEqual([a.method, b.method], ['tools/list', 'resources/list']);
    return s;
  });
  assert.equal(server.running, false);
});

test('a server that cannot start fails its requests, and close still resolves', { timeout: 15000 }, async () => {
  await withServer(['isotope-no-such-command-for-this-test', []], async (s) => {
    await assert.rejects(initialize(s), /the server did not start/);
  });
});

test('a server that exits fails its requests with its exit code and stderr', { timeout: 15000 }, async () => {
  await withServer(node("process.stderr.write('missing a DLL'); process.exit(3)"), async (s) => {
    await assert.rejects(initialize(s), /the server exited \(code 3\): missing a DLL|stopped reading its input/);
    await assert.rejects(s.request('tools/list'));
    for (let i = 0; i < 50 && s.running; i++) await delay(100);
    assert.equal(s.running, false, 'the exit was not seen');
  });
});

test('a server that stops reading but keeps running is stopped by close', { timeout: 15000 }, async () => {
  const server = await withServer(node('process.stdin.destroy(); setInterval(() => {}, 1000)'), async (s) => {
    await assert.rejects(initialize(s));
    assert.equal(s.running, true);
    return s;
  });
  assert.equal(server.running, false);
});

test('a line on stdout that is not JSON-RPC fails the requests', { timeout: 15000 }, async () => {
  await withServer(node("process.stdin.on('data', () => process.stdout.write('Application started\\n'))"), async (s) => {
    await assert.rejects(initialize(s), /not JSON-RPC: Application started/);
  });
});

test('a request with no answer times out', { timeout: 15000 }, async () => {
  await withServer(node("process.stdin.on('data', () => {})"), async (s) => {
    await assert.rejects(s.request('tools/list'), /tools\/list: no answer in 300 ms/);
  }, { timeoutMs: 300 });
});
