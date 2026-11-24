// Tests of the smoke test's MCP client (#60): node --test tools/packaging/test/*.test.mjs. Fake servers in node, so no dotnet is
// needed; pack.mjs drives the real tool with it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { initialize, startMcp } from '../lib/mcp.mjs';

const node = (code) => [process.execPath, ['-e', code]];

// Answers each request with its method's name; the first line is a log on stderr, as the real server's are.
const ECHO = `process.stderr.write('log line\\n');
let buf = '';
process.stdin.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\\n')) >= 0) { const m = JSON.parse(buf.slice(0, i)); buf = buf.slice(i + 1);
  if (m.id != null) process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: { method: m.method, serverInfo: { name: 'echo' } } }) + '\\n'); } });`;

test('requests are answered by id, and close ends the server', { timeout: 15000 }, async () => {
  const server = startMcp(...node(ECHO));
  assert.equal((await initialize(server)).serverInfo.name, 'echo');
  const [a, b] = await Promise.all([server.request('tools/list'), server.request('resources/list')]);
  assert.deepEqual([a.method, b.method], ['tools/list', 'resources/list']);
  await server.close();
  assert.equal(server.running, false);
});

test('a server that cannot start fails its requests, and close still resolves', { timeout: 15000 }, async () => {
  const server = startMcp('isotope-no-such-command-for-this-test', []);
  await assert.rejects(initialize(server), /the server did not start/);
  await server.close();
});

test("a server that exits fails its requests with its exit code and stderr", { timeout: 15000 }, async () => {
  const server = startMcp(...node("process.stderr.write('missing a DLL'); process.exit(3)"));
  await assert.rejects(initialize(server), /the server exited \(code 3\): missing a DLL|stopped reading its input/);
  await assert.rejects(server.request('tools/list'));
  await server.close();
});

test('a server that stops reading but keeps running is stopped by close', { timeout: 15000 }, async () => {
  const server = startMcp(...node("process.stdin.destroy(); setInterval(() => {}, 1000)"), { timeoutMs: 2000 });
  await assert.rejects(initialize(server));
  assert.equal(server.running, true);
  await server.close();
  assert.equal(server.running, false);
});

test('a line on stdout that is not JSON-RPC fails the requests', { timeout: 15000 }, async () => {
  const server = startMcp(...node("process.stdin.on('data', () => process.stdout.write('Application started\\n'))"));
  await assert.rejects(initialize(server), /not JSON-RPC: Application started/);
  await server.close();
});

test('a request with no answer times out', { timeout: 15000 }, async () => {
  const server = startMcp(...node("process.stdin.on('data', () => {})"), { timeoutMs: 300 });
  await assert.rejects(server.request('tools/list'), /tools\/list: no answer in 300 ms/);
  await server.close();
});
