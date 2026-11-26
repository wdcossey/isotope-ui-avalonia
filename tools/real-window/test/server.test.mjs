// Tests of the checklist server's checks and endpoints (#106): node --test tools/real-window/test/*.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseChecklist } from '../lib/parse.mjs';
import { emptyResults } from '../lib/results.mjs';
import { readFileSync } from 'node:fs';
import net from 'node:net';
import vm from 'node:vm';
import { SCRIPT, STYLE, createChecklistServer, escapeHtml, inlineMarkdown, renderPage } from '../lib/server.mjs';
import { post, request, tokenOf } from './http.mjs';

const TOKEN = 'ab'.repeat(24);
const TEXT = '### Section <one>\n- [ ] First **bold** <script>alert(1)</script>\n- [x] Second & "quoted"\n';

// Starts a server on a free port with a 1 KiB body cap, runs fn, and always closes it.
async function withServer(fn, { getItems, finishDelay = 0 } = {}) {
  const items = parseChecklist(TEXT).items;
  const results = emptyResults('checklist.md');
  const calls = { save: 0, log: [], finish: 0 };
  const { server, listen } = createChecklistServer({
    title: 'Checklist <test>', getItems: getItems ?? (() => items), results, token: TOKEN, maxBody: 1024,
    save: () => calls.save++, log: (e) => calls.log.push(e), finish: async () => { calls.finish++; await new Promise((r) => setTimeout(r, finishDelay)); return 'summary text'; },
  });
  const port = await listen(0);
  try { return await fn({ port, items, results, calls, server }); } finally { server.closeAllConnections(); await new Promise((r) => server.close(r)); }
}

test('it listens on 127.0.0.1 only', { timeout: 15000 }, () => withServer(async ({ server }) => {
  assert.equal(server.address().address, '127.0.0.1');
}));

test('the page is served for Host 127.0.0.1:<port> and localhost:<port>, with a strict CSP and the token', { timeout: 15000 }, () => withServer(async ({ port }) => {
  for (const host of [`127.0.0.1:${port}`, `localhost:${port}`, `LOCALHOST:${port}`]) {
    const r = await request(port, { headers: { host } });
    assert.equal(r.status, 200, host);
    assert.equal(tokenOf(r.text), TOKEN);
    assert.match(r.headers['content-security-policy'], /default-src 'none'; script-src 'nonce-[^']+'/);
    assert.equal(r.headers['x-frame-options'], 'DENY');
  }
}));

test('a bad Host is refused, on GET and POST', { timeout: 15000 }, () => withServer(async ({ port, calls }) => {
  for (const host of ['evil.example', `evil.example:${port}`, `127.0.0.1:${port + 1}`, `127.0.0.1.evil.example:${port}`, '127.0.0.1']) {
    assert.equal((await request(port, { headers: { host } })).status, 403, 'GET ' + host);
    assert.equal((await post(port, '/system', { system: 'x' }, TOKEN, { host })).status, 403, 'POST ' + host);
  }
  assert.equal(calls.save, 0);
}));

test('a cross-site Origin is refused; the page\'s own origin is allowed', { timeout: 15000 }, () => withServer(async ({ port, items, calls }) => {
  for (const origin of ['http://evil.example', 'null', `https://127.0.0.1:${port}`, `http://127.0.0.1:${port + 1}`]) {
    assert.equal((await request(port, { headers: { origin } })).status, 403, 'GET ' + origin);
    assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass' }, TOKEN, { origin })).status, 403, 'POST ' + origin);
  }
  assert.equal(calls.save, 0);
  assert.equal((await post(port, '/answer', { key: items[0].key, verdict: 'pass' }, TOKEN, { origin: `http://localhost:${port}` })).status, 200);
}));

test('a POST without the token, or with another, is refused', { timeout: 15000 }, () => withServer(async ({ port, items, results, calls }) => {
  const body = { key: items[0].key, verdict: 'pass' };
  assert.equal((await post(port, '/answer', body)).status, 403);
  assert.equal((await post(port, '/answer', body, 'cd'.repeat(24))).status, 403);
  assert.equal((await post(port, '/answer', body, TOKEN.slice(0, -2))).status, 403);
  assert.equal((await post(port, '/finish', {})).status, 403);
  assert.deepEqual(results.answers, {});
  assert.deepEqual([calls.save, calls.finish], [0, 0]);
}));

test('a body over the cap is refused, declared or streamed', { timeout: 15000 }, () => withServer(async ({ port, results }) => {
  const big = { system: 'x'.repeat(2000) };
  assert.equal((await post(port, '/system', big, TOKEN)).status, 413);
  const streamed = await request(port, {
    method: 'POST', path: '/system', headers: { 'content-type': 'application/json', 'x-checklist-token': TOKEN, 'transfer-encoding': 'chunked' },
    body: (req) => { req.write('{"system":"'); for (let i = 0; i < 8; i++) req.write('y'.repeat(512)); req.end('"}'); },
  });
  assert.ok([413, 'reset'].includes(streamed.status), String(streamed.status));
  // A declared length over the cap is refused before any of the body is sent.
  let sent;
  const declared = await Promise.race([
    request(port, { method: 'POST', path: '/system', headers: { 'content-type': 'application/json', 'x-checklist-token': TOKEN, 'content-length': '1000000' }, body: (req) => { sent = req; req.flushHeaders(); } }),
    new Promise((r) => setTimeout(() => r({ status: 'no answer' }), 3000).unref()),
  ]);
  sent.destroy();
  assert.equal(declared.status, 413);
  assert.equal(results.system, '');
}));

test('a POST must be JSON, name a line of the checklist, and give a known verdict', { timeout: 15000 }, () => withServer(async ({ port, items, results }) => {
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

test('an answer is saved, logged and counted; system and notes are saved', { timeout: 15000 }, () => withServer(async ({ port, items, results, calls }) => {
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

test('finish answers the summary, emits finished once, and refuses answers after', { timeout: 15000 }, () => withServer(async ({ port, items, server, calls }) => {
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

test('a page that cannot be built is a 500, not a 200 with an error in it', { timeout: 15000 }, () => withServer(async ({ port }) => {
  const r = await request(port);
  assert.equal(r.status, 500);
  assert.match(r.text, /checklist moved/);
}, { getItems: () => { throw new Error('checklist moved'); } }));

test('finished is emitted even when the tab goes before the reply', { timeout: 15000 }, () => withServer(async ({ port, server }) => {
  const finished = new Promise((r) => server.once('finished', r));
  const body = '{}';
  const socket = net.connect(port, '127.0.0.1', () => {
    socket.end(`POST /finish HTTP/1.1\r\nHost: 127.0.0.1:${port}\r\nContent-Type: application/json\r\nX-Checklist-Token: ${TOKEN}\r\nContent-Length: ${body.length}\r\n\r\n${body}`);
  });
  socket.on('data', () => {});
  socket.on('error', () => {});
  const timeout = new Promise((r) => setTimeout(() => r('timed out'), 3000).unref());
  assert.notEqual(await Promise.race([finished.then(() => 'finished'), timeout]), 'timed out');
}, { finishDelay: 200 }));

test('bold is not applied inside a code span', () => {
  assert.equal(inlineMarkdown('Type `**hi**` then **go** <x>'), 'Type <code>**hi**</code> then <b>go</b> &lt;x&gt;');
});

test('the page sends its posts one at a time, in order', async () => {
  // The page's script in a sandbox, with a fetch whose first call answers last if nothing waits for it.
  const started = [], finished = [];
  const fetch = (path, init) => {
    const body = JSON.parse(init.body);
    started.push(body.n);
    return new Promise((resolve) => setTimeout(() => { finished.push(body.n); resolve({ ok: true, json: async () => ({}) }); }, body.n === 1 ? 60 : 5));
  };
  const document = { querySelector: () => ({ content: TOKEN }), addEventListener: () => {} };
  const context = vm.createContext({ document, fetch, setTimeout, Promise, JSON, Error });
  vm.runInContext(SCRIPT + ';globalThis.post = post;', context);
  await Promise.all([context.post('/answer', { n: 1 }), context.post('/answer', { n: 2 })]);
  assert.deepEqual(finished, [1, 2]);
  assert.deepEqual(started, [1, 2]);
});

test('the page carries its stylesheet under the same CSP, by the response\'s nonce, and fetches nothing', { timeout: 15000 }, () => withServer(async ({ port }) => {
  const r = await request(port);
  const csp = r.headers['content-security-policy'];
  const nonce = /style-src 'nonce-([^']+)'/.exec(csp)?.[1];
  assert.ok(nonce, csp);
  assert.equal(csp, `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`);
  // One stylesheet, inline, under the nonce, and it is the tool's own file (#109).
  const styles = [...r.text.matchAll(/<style([^>]*)>([\s\S]*?)<\/style>/g)];
  assert.equal(styles.length, 1);
  assert.equal(styles[0][1], ` nonce="${nonce}"`);
  assert.equal(styles[0][2], readFileSync(new URL('../lib/checklist.css', import.meta.url), 'utf8'));
  assert.match(STYLE, /--background:/);
  // Nothing the CSP would block or that would fetch: no style attribute, link, src or href; no url(), @import or web
  // font; no address at all outside the stylesheet's licence comment.
  assert.doesNotMatch(r.text, /\sstyle=|<link|\s(src|srcset|href|action|formaction|poster|data)=/i);
  const code = r.text.replace(/\/\*[\s\S]*?\*\//g, '');
  assert.doesNotMatch(code, /url\(|@import|@font-face|https?:|\/\/[a-z0-9]/i);
}));

test('the stylesheet carries shadcn/ui\'s copyright and MIT notice', () => {
  const header = /^\/\*([\s\S]*?)\*\//.exec(STYLE)?.[1] ?? '';
  assert.match(header, /shadcn\/ui's licence:\s+MIT License\s+Copyright \(c\) \d{4} \S+/);
  assert.match(header, /Permission is hereby granted, free of charge/);
  assert.match(header, /THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND/);
});

test('escaping holds in every element the page adds: header, cards, badges, buttons and fields', () => {
  const BAD = '"><script>bad()</script><img src=x onerror=bad()>\'';
  const items = parseChecklist(`### ${BAD}\n- [x] ${BAD}\n- [ ] Plain\n`).items;
  const results = emptyResults('c.md');
  results.system = BAD;
  results.notes = BAD;
  // A verdict loadResults would refuse, so the page's escaping is tested on its own.
  results.answers[items[0].key] = { verdict: BAD, note: BAD, date: '2026-11-26' };
  const html = renderPage({ title: BAD, items, results, token: TOKEN, nonce: 'n' });
  const body = html.replace(/<style nonce="n">[\s\S]*?<\/style>/, '').replace(/<script nonce="n">[\s\S]*?<\/script>/, '');
  // Every tag left is the page's own, with quoted attribute values and no event handler.
  for (const [tag, name, attrs] of body.matchAll(/<([a-z][a-z0-9]*)([^>]*)>/gi)) {
    assert.doesNotMatch(name, /^(script|img|iframe|object|embed|svg|link)$/i, tag);
    assert.match(attrs, /^(?:\s+[a-z-]+(?:="[^"<>]*")?)*$/i, tag);
    for (const [, attr] of attrs.matchAll(/\s([a-z-]+)(?:="[^"]*")?/gi)) assert.doesNotMatch(attr, /^on/i, tag);
  }
  const e = escapeHtml(BAD);
  for (const part of [`<h1>${e}</h1>`, `<h2>${e}</h2>`, `data-key="${escapeHtml(items[0].key)}"`, `data-verdict="${e}"`,
    `<span class="badge s">${e}</span>`, `id="sys" class="input" placeholder="OS, display server, version" value="${e}"`,
    `placeholder="note (optional)" value="${e}"`, `aria-label="Anything else">${e}</textarea>`, '<span class="badge badge-outline was">ticked before</span>']) {
    assert.ok(html.includes(part), part);
  }
  // An unknown verdict presses no button.
  assert.equal((body.match(/aria-pressed="true"/g) ?? []).length, 0);
});

test('P, F and S answer the focused line, but never from a field, with a modifier, or after Finish', () => {
  const document = { querySelector: () => ({ content: TOKEN }), addEventListener: () => {} };
  const context = vm.createContext({ document, fetch: () => {}, setTimeout, Promise, JSON, Error });
  vm.runInContext(SCRIPT + ';globalThis.shortcut = shortcut;', context);
  const line = { line: 1 };
  const on = (tagName, more = {}) => ({ tagName, closest: (sel) => (sel === '.it' ? line : null), ...more });
  const verdict = (key, target, mods = {}) => {
    const r = context.shortcut({ key, target, ...mods });
    if (r) assert.equal(r.it, line);
    return r?.verdict ?? null;
  };
  assert.deepEqual(['p', 'f', 's', 'P', 'S'].map((k) => verdict(k, on('DIV'))), ['pass', 'fail', 'skip', 'pass', 'skip']);
  assert.equal(verdict('f', on('BUTTON')), 'fail');
  for (const field of [on('INPUT'), on('TEXTAREA'), on('SELECT'), on('input'), on('DIV', { isContentEditable: true })]) {
    assert.equal(verdict('p', field), null, field.tagName);
  }
  for (const mod of ['ctrlKey', 'altKey', 'metaKey', 'repeat', 'defaultPrevented']) assert.equal(verdict('p', on('DIV'), { [mod]: true }), null, mod);
  assert.equal(verdict('x', on('DIV')), null);
  assert.equal(verdict('p', { tagName: 'DIV', closest: () => null }), null, 'outside a line');
  assert.equal(verdict('p', null), null, 'no target');
  vm.runInContext('finished = true;', context);
  assert.equal(verdict('p', on('DIV')), null, 'after Finish');
});

test('once Finish is confirmed, the page sends no more answers, even before the finish is answered', async () => {
  const listeners = {}, sent = [];
  const el = () => ({ textContent: '', value: '', max: 0, disabled: false, classList: { add() {}, remove() {}, toggle() {} } });
  const document = {
    querySelector: () => ({ content: TOKEN }), addEventListener: (type, fn) => { listeners[type] = fn; },
    getElementById: () => el(), querySelectorAll: () => [],
  };
  // /finish never answers, as during the round trip; an answer answers at once.
  const fetch = (path) => { sent.push(path); return path === '/finish' ? new Promise(() => {}) : Promise.resolve({ ok: true, json: async () => ({ answered: 1, total: 1 }) }); };
  const context = vm.createContext({ document, fetch, confirm: () => true, setTimeout, Promise, JSON, Error, String });
  vm.runInContext(SCRIPT + ';globalThis.answer = answer;', context);
  const section = { querySelectorAll: () => [], querySelector: () => el() };
  const line = { dataset: { key: 'k', verdict: '' }, querySelector: () => el(), querySelectorAll: () => [], closest: () => section };
  assert.equal(await context.answer(line, 'pass'), true);
  assert.deepEqual(sent, ['/answer']);
  listeners.click({ target: { id: 'finish', closest: () => null } });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual(sent, ['/answer', '/finish']);
  assert.equal(await context.answer(line, 'fail'), false);
  assert.equal(context.shortcut({ key: 'p', target: { tagName: 'DIV', closest: () => line } }), null);
  assert.deepEqual(sent, ['/answer', '/finish']);
});
