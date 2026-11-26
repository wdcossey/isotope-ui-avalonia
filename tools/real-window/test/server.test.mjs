// Tests of the checklist server's checks and endpoints (#106): node --test tools/real-window/test/*.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseChecklist } from '../lib/parse.mjs';
import { emptyResults } from '../lib/results.mjs';
import { createChecklistServer, renderPage } from '../lib/server.mjs';
import { post, request, tokenOf } from './http.mjs';

const TOKEN = 'ab'.repeat(24);
const TEXT = '### Section <one>\n- [ ] First **bold** <script>alert(1)</script>\n- [x] Second & "quoted"\n';

// Starts a server on a free port with a 1 KiB body cap, runs fn, and always closes it.
async function withServer(fn) {
  const items = parseChecklist(TEXT).items;
  const results = emptyResults('checklist.md');
  const calls = { save: 0, log: [], finish: 0 };
  const { server, listen } = createChecklistServer({
    title: 'Checklist <test>', getItems: () => items, results, token: TOKEN, maxBody: 1024,
    save: () => calls.save++, log: (e) => calls.log.push(e), finish: async () => { calls.finish++; return 'summary text'; },
  });
  const port = await listen(0);
  try { return await fn({ port, items, results, calls, server }); } finally { server.closeAllConnections(); await new Promise((r) => server.close(r)); }
}

test('it listens on 127.0.0.1 only', () => withServer(async ({ server }) => {
  assert.equal(server.address().address, '127.0.0.1');
}));

test('the page is served for Host 127.0.0.1:<port> and localhost:<port>, with a strict CSP and the token', () => withServer(async ({ port }) => {
  for (const host of [`127.0.0.1:${port}`, `localhost:${port}`, `LOCALHOST:${port}`]) {
    const r = await request(port, { headers: { host } });
    assert.equal(r.status, 200, host);
    assert.equal(tokenOf(r.text), TOKEN);
    assert.match(r.headers['content-security-policy'], /default-src 'none'; script-src 'nonce-[^']+'/);
    assert.equal(r.headers['x-frame-options'], 'DENY');
  }
}));

test('a bad Host is refused, on GET and POST', () => withServer(async ({ port, calls }) => {
  for (const host of ['evil.example', `evil.example:${port}`, `127.0.0.1:${port + 1}`, `127.0.0.1.evil.example:${port}`, '127.0.0.1']) {
    assert.equal((await request(port, { headers: { host } })).status, 403, 'GET ' + host);
    assert.equal((await post(port, '/system', { system: 'x' }, TOKEN, { host })).status, 403, 'POST ' + host);
  }
  assert.equal(calls.save, 0);
}));

test('a cross-site Origin is refused; the page\'s own origin is allowed', () => withServer(async ({ port, items, calls }) => {
  for (const origin of ['http://evil.example', 'null', `https://127.0.0.1:${port}`, `http://127.0.0.1:${port + 1}`]) {
    assert.equal((await request(port, { headers: { origin } })).status, 403, 'GET ' + origin);
    assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass' }, TOKEN, { origin })).status, 403, 'POST ' + origin);
  }
  assert.equal(calls.save, 0);
  assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass' }, TOKEN, { origin: `http://localhost:${port}` })).status, 200);
}));

test('a POST without the token, or with another, is refused', () => withServer(async ({ port, items, results, calls }) => {
  const body = { key: items[0].key, verdict: 'pass' };
  assert.equal((await post(port, '/answer', body)).status, 403);
  assert.equal((await post(port, '/answer', body, 'cd'.repeat(24))).status, 403);
  assert.equal((await post(port, '/answer', body, TOKEN.slice(0, -2))).status, 403);
  assert.equal((await post(port, '/finish', {})).status, 403);
  assert.deepEqual(results.answers, {});
  assert.deepEqual([calls.save, calls.finish], [0, 0]);
}));

test('a body over the cap is refused, declared or streamed', () => withServer(async ({ port, results }) => {
  const big = { system: 'x'.repeat(2000) };
  assert.equal((await post(port, '/system', big, TOKEN)).status, 413);
  const streamed = await request(port, {
    method: 'POST', path: '/system', headers: { 'content-type': 'application/json', 'x-checklist-token': TOKEN, 'transfer-encoding': 'chunked' },
    body: (req) => { req.write('{"system":"'); for (let i = 0; i < 8; i++) req.write('y'.repeat(512)); req.end('"}'); },
  });
  assert.ok([413, 'reset'].includes(streamed.status), String(streamed.status));
  assert.equal(results.system, '');
}));

test('a POST must be JSON, name a line of the checklist, and give a known verdict', () => withServer(async ({ port, items, results }) => {
  const r = await request(port, { method: 'POST', path: '/answer', body: 'key=x', headers: { 'content-type': 'application/x-www-form-urlencoded', 'x-checklist-token': TOKEN } });
  assert.equal(r.status, 415);
  assert.equal((await post(port, '/answer', { key: '../../etc/passwd', verdict: 'pass' }, TOKEN)).status, 400);
  assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'maybe' }, TOKEN)).status, 400);
  assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass', note: 5 }, TOKEN)).status, 400);
  assert.equal((await post(port, '/answer', [1], TOKEN)).status, 400);
  assert.deepEqual(results.answers, {});
  assert.equal((await request(port, { path: '/answer' })).status, 405);
  assert.equal((await request(port, { path: '/README.md' })).status, 404);
}));

test('an answer is saved, logged and counted; system and notes are saved', () => withServer(async ({ port, items, results, calls }) => {
  const r = await post(port, '/answer', { key: items[1].key, verdict: 'fail', note: 'broken' }, TOKEN);
  assert.equal(r.status, 200);
  assert.deepEqual(JSON.parse(r.text), { ok: true, answered: 1, total: 2 });
  assert.equal(results.answers[items[1].key].verdict, 'fail');
  assert.equal(results.answers[items[1].key].note, 'broken');
  assert.match(results.answers[items[1].key].date, /^\d{4}-\d{2}-\d{2}$/);
  assert.deepEqual([calls.log[0].event, calls.log[0].line, calls.log[0].verdict, calls.log[0].note], ['answer', 3, 'fail', 'broken']);
  assert.equal((await post(port, '/system', { system: 'Fedora 43, Wayland' }, TOKEN)).status, 200);
  assert.equal((await post(port, '/notes', { notes: 'all good' }, TOKEN)).status, 200);
  assert.deepEqual([results.system, results.notes, calls.save], ['Fedora 43, Wayland', 'all good', 3]);
}));

test('finish answers the summary, emits finished once, and refuses answers after', () => withServer(async ({ port, items, server, calls }) => {
  let finished = 0;
  server.on('finished', () => finished++);
  const r = await post(port, '/finish', {}, TOKEN);
  assert.deepEqual([r.status, JSON.parse(r.text).summary], [200, 'summary text']);
  assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass' }, TOKEN)).status, 409);
  assert.equal((await post(port, '/finish', {}, TOKEN)).status, 409);
  assert.deepEqual([finished, calls.finish], [1, 1]);
}));

test('everything the page shows is escaped', () => {
  const items = parseChecklist(TEXT).items;
  const results = emptyResults('c.md');
  results.system = '"><script>bad()</script>';
  results.notes = '</textarea><script>bad()</script>';
  results.answers[items[0].key] = { verdict: 'pass', note: '" onmouseover="bad()', date: '2026-11-26' };
  const html = renderPage({ title: 'T <title>', items, results, token: TOKEN, nonce: 'n' });
  assert.equal((html.match(/<script/g) ?? []).length, 1, 'only the page\'s own script');
  assert.doesNotMatch(html, /onmouseover="bad/);
  assert.match(html, /<b>bold<\/b> &lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.match(html, /Second &amp; &quot;quoted&quot;/);
  assert.match(html, /<h2>Section &lt;one&gt;<\/h2>/);
  assert.match(html, /<title>T &lt;title&gt;<\/title>/);
  assert.match(html, /value="&quot; onmouseover=&quot;bad\(\)"/);
});
