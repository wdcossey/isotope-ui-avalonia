// The checklist page and its HTTP endpoints (#106). Local only: it listens on 127.0.0.1; every request's Host must be
// 127.0.0.1:<port> or localhost:<port> and its Origin, when sent, the same origin (against DNS rebinding); every POST
// carries the run's random token in a header (against cross-site requests), is JSON, and has a capped body. Everything
// rendered is escaped, and a content security policy allows only the page's own script and style. No request names a
// file: apart from its stylesheet, read once at load, the server reads and writes only through the callbacks it is given.
import { readFileSync } from 'node:fs';
import http from 'node:http';
import { randomBytes, timingSafeEqual } from 'node:crypto';
import { sections } from './parse.mjs';
import { LIMITS, localDate } from './results.mjs';
import { VERDICTS } from './writeback.mjs';

export const HOST = '127.0.0.1';
export const TOKEN_HEADER = 'x-checklist-token';
export const MAX_BODY = 64 * 1024;

export const newToken = () => randomBytes(24).toString('hex');

export const escapeHtml = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

// A checklist line as HTML: escaped first, then `code` spans and, outside them, **bold** made into tags (escaping
// leaves backticks and asterisks alone).
export const inlineMarkdown = (s) => escapeHtml(s).split(/(`[^`]+`)/).map((part, i) => (i % 2
  ? '<code>' + part.slice(1, -1) + '</code>'
  : part.replace(/\*\*(.+?)\*\*/g, '<b>$1</b>'))).join('');

// The page's stylesheet (#109): shadcn/ui's look in plain CSS, read once and inlined in the page by the request's nonce,
// so the CSP stays as it is. It names no URL (no @import, url() or web font).
export const STYLE = readFileSync(new URL('./checklist.css', import.meta.url), 'utf8');

// The page's script: one click per line sends its verdict and note; changing a note resends the line's verdict.
// Posts go one at a time, in order, so a note's resend (on blur) can't land after the click that caused the blur.
// P, F and S answer the focused line (the line itself or a button in it) and move to the next line; never from a
// field, with Ctrl, Alt or Meta held, on a key held down, or once Finish is confirmed (no answer is sent after it).
export const SCRIPT = `const token = document.querySelector('meta[name="checklist-token"]').content;
let queue = Promise.resolve();
function post(path, body) {
  const send = async () => {
    const r = await fetch(path, { method: 'POST', headers: { 'content-type': 'application/json', '${TOKEN_HEADER}': token }, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(r.status + ' ' + (await r.text()));
    return r.json();
  };
  const p = queue.then(send);
  queue = p.catch(() => {});
  return p;
}
const KEYS = { p: 'pass', f: 'fail', s: 'skip' };
let finished = false;
function typing(t) { return !!t && (t.isContentEditable === true || /^(INPUT|TEXTAREA|SELECT)$/i.test(String(t.tagName))); }
function shortcut(e) {
  if (finished || e.defaultPrevented || e.repeat || e.ctrlKey || e.altKey || e.metaKey || typing(e.target)) return null;
  const verdict = KEYS[String(e.key).toLowerCase()];
  const it = verdict && e.target && typeof e.target.closest === 'function' ? e.target.closest('.it') : null;
  return it ? { it, verdict } : null;
}
function progress(r) {
  if (!r || r.total == null) return;
  document.getElementById('progress').textContent = r.answered + ' of ' + r.total + ' answered';
  const bar = document.getElementById('bar'); bar.max = r.total; bar.value = r.answered;
}
function sectionCount(it) {
  const sec = it.closest('section'), lines = [...sec.querySelectorAll('.it')], done = lines.filter((l) => l.dataset.verdict).length;
  const c = sec.querySelector('.count');
  c.textContent = done + ' of ' + lines.length;
  c.classList.toggle('badge-default', done === lines.length); c.classList.toggle('badge-outline', done !== lines.length);
}
async function answer(it, verdict) {
  const s = it.querySelector('.s');
  if (finished) return false;
  try {
    progress(await post('/answer', { key: it.dataset.key, verdict, note: it.querySelector('.note').value }));
    it.dataset.verdict = verdict; s.textContent = verdict; s.classList.remove('err');
    for (const b of it.querySelectorAll('button[data-verdict]')) b.setAttribute('aria-pressed', String(b.dataset.verdict === verdict));
    sectionCount(it);
    return true;
  } catch (e) { s.textContent = 'error: ' + e.message; s.classList.add('err'); return false; }
}
// Focuses el and scrolls it into view below the sticky header, whatever height the header has wrapped to.
function show(el) {
  if (!el) return;
  el.focus({ preventScroll: true });
  const top = document.getElementById('top').getBoundingClientRect().bottom + 8, r = el.getBoundingClientRect();
  if (r.top < top) window.scrollBy(0, r.top - top);
  else if (r.bottom > window.innerHeight - 8) window.scrollBy(0, Math.min(r.bottom - window.innerHeight + 8, r.top - top));
}
document.addEventListener('keydown', async (e) => {
  const k = shortcut(e);
  if (!k) return;
  e.preventDefault();
  const lines = [...document.querySelectorAll('.it')];
  const next = lines[lines.indexOf(k.it) + 1] || document.getElementById('finish');
  if (await answer(k.it, k.verdict) && k.it.contains(document.activeElement)) show(next);
});
document.addEventListener('click', async (e) => {
  const b = e.target.closest('button[data-verdict]');
  if (b) return answer(b.closest('.it'), b.dataset.verdict);
  if (e.target.id === 'next') return show(document.querySelector('.it[data-verdict=""]') || document.getElementById('finish'));
  if (e.target.id === 'finish') {
    if (finished || !confirm('Write the answers into the checklist and stop the tool?')) return;
    finished = true;
    try {
      const r = await post('/finish', {});
      document.getElementById('summary').textContent = r.summary;
      for (const x of document.querySelectorAll('button, input, textarea')) x.disabled = true;
    } catch (err) { finished = false; document.getElementById('summary').textContent = 'error: ' + err.message; }
  }
});
document.addEventListener('change', async (e) => {
  const t = e.target, st = document.getElementById(t.id + 's');
  try {
    if (t.classList.contains('note')) { const it = t.closest('.it'); if (it.dataset.verdict) await answer(it, it.dataset.verdict); }
    else if (t.id === 'sys') { await post('/system', { system: t.value }); st.textContent = 'saved'; }
    else if (t.id === 'free') { await post('/notes', { notes: t.value }); st.textContent = 'saved'; }
  } catch (err) { if (st) st.textContent = 'error: ' + err.message; }
});`;

// Each verdict's button, in shadcn/ui's variants: Pass the default, Fail destructive, Skip outline.
const VARIANT = { pass: 'default', fail: 'destructive', skip: 'outline' };
const label = (v) => v[0].toUpperCase() + v.slice(1);

export function renderPage({ title, items, results, token, nonce }) {
  const answers = results.answers ?? {};
  const done = (list) => list.filter((it) => answers[it.key]).length;
  const answered = done(items);
  let body = '', line = 0;
  for (const s of sections(items)) {
    const n = done(s.items);
    body += `<section class="card"><div class="card-header"><h2>${escapeHtml(s.name || '(no heading)')}</h2>`
      + `<span class="badge ${n === s.items.length ? 'badge-default' : 'badge-outline'} count">${n} of ${s.items.length}</span></div><div class="card-content lines">`;
    s.items.forEach((it, i) => {
      const a = answers[it.key], id = 't' + line++;
      if (i) body += '<div class="sep" role="none"></div>';
      // A line takes focus from a click or the keys (tabindex -1), not as an extra Tab stop: Tab reaches its buttons.
      body += `<div class="it" role="group" aria-labelledby="${id}" tabindex="-1" data-key="${escapeHtml(it.key)}" data-verdict="${escapeHtml(a?.verdict ?? '')}">`
        + `<div class="t" id="${id}">${it.ticked ? '<span class="badge badge-outline was">ticked before</span> ' : ''}${inlineMarkdown(it.text)}</div><div class="b">`
        + VERDICTS.map((v) => `<button class="btn btn-sm btn-${VARIANT[v]}" data-verdict="${v}" aria-pressed="${a?.verdict === v}">${label(v)}</button>`).join('')
        + `<input class="input input-sm note" aria-label="Note" placeholder="note (optional)" value="${escapeHtml(a?.note ?? '')}"><span class="badge s">${escapeHtml(a?.verdict ?? '')}</span></div></div>`;
    });
    body += '</div></section>';
  }
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="checklist-token" content="${escapeHtml(token)}">`
    + '<meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light dark">'
    + `<title>${escapeHtml(title)}</title><style nonce="${nonce}">${STYLE}</style></head><body>`
    + `<header id="top" class="top"><div class="top-in"><div class="top-head"><h1>${escapeHtml(title)}</h1>`
    + '<p class="hint">Answers are saved as you go. On the focused line: <kbd>P</kbd> pass, <kbd>F</kbd> fail, <kbd>S</kbd> skip.</p></div>'
    + `<div class="top-row"><label class="field"><span class="label">System</span><input id="sys" class="input" placeholder="OS, display server, version" value="${escapeHtml(results.system)}"></label>`
    + `<span id="syss" class="status"></span><div class="meter"><progress id="bar" max="${items.length}" value="${answered}" aria-label="Lines answered"></progress>`
    + `<span id="progress" class="badge badge-secondary">${answered} of ${items.length} answered</span></div>`
    + '<button id="next" class="btn btn-sm btn-ghost">Next unanswered</button></div></div></header><main>'
    + body
    + '<section class="card"><div class="card-header stack"><h2>Anything else</h2><p class="card-desc">Notes for the run as a whole.</p></div>'
    + `<div class="card-content"><textarea id="free" class="textarea" rows="5" aria-label="Anything else">${escapeHtml(results.notes)}</textarea><div id="frees" class="status"></div></div></section>`
    + '<section class="card finish"><div class="card-header stack"><h2>Finish</h2><p class="card-desc">Ticks the passed lines, unticks the failed ones, adds the notes, and stops the tool.</p></div>'
    + '<div class="card-content"><button id="finish" class="btn btn-lg btn-default">Finish: write the answers into the checklist</button><pre id="summary"></pre></div></section>'
    + `</main><script nonce="${nonce}">${SCRIPT}</script></body></html>`;
}

class HttpError extends Error { constructor(status, message) { super(message); this.status = status; } }

function sameToken(given, token) {
  const a = Buffer.from(String(given ?? '')), b = Buffer.from(token);
  return a.length === b.length && timingSafeEqual(a, b);
}

function readJson(req, maxBody) {
  return new Promise((resolve, reject) => {
    const declared = Number(req.headers['content-length']);
    if (declared > maxBody) { req.resume(); return reject(new HttpError(413, 'body too large')); }
    const chunks = []; let size = 0, done = false;
    req.on('data', (d) => {
      if (done) return;
      size += d.length;
      if (size > maxBody) { done = true; req.resume(); reject(new HttpError(413, 'body too large')); return; }
      chunks.push(d);
    });
    req.on('end', () => {
      if (done) return;
      done = true;
      try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8') || 'null')); } catch { reject(new HttpError(400, 'not JSON')); }
    });
    req.on('error', (e) => { if (!done) { done = true; reject(e); } });
  });
}

const str = (v, max, name) => {
  if (v === undefined || v === null) return '';
  if (typeof v !== 'string') throw new HttpError(400, name + ' must be a string');
  if (v.length > max) throw new HttpError(400, `${name} is longer than ${max} characters`);
  return v;
};

// options: title; getItems() (the checklist's lines, read afresh); results (changed in place); token; save() after
// each change; log(event) for each answer; finish() resolving the summary text. Returns the server, listen(port)
// resolving the port, and a 'finished' event once a finish has been answered.
export function createChecklistServer({ title, getItems, results, token, save, log, finish, maxBody = MAX_BODY }) {
  let port = 0, finishing = false;
  const allowedHosts = () => [`127.0.0.1:${port}`, `localhost:${port}`];
  const allowedOrigins = () => allowedHosts().map((h) => 'http://' + h);

  async function handle(req, res) {
    const host = String(req.headers.host ?? '').toLowerCase();
    if (!allowedHosts().includes(host)) throw new HttpError(403, 'bad Host');
    const origin = req.headers.origin;
    if (origin !== undefined && !allowedOrigins().includes(String(origin).toLowerCase())) throw new HttpError(403, 'bad Origin');
    const url = new URL(req.url, 'http://' + host);
    if (req.method === 'GET' && url.pathname === '/') {
      const nonce = randomBytes(16).toString('base64');
      const page = renderPage({ title, items: getItems(), results, token, nonce });
      res.writeHead(200, {
        'content-type': 'text/html; charset=utf-8',
        'content-security-policy': `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`,
        'x-frame-options': 'DENY', 'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer', 'cache-control': 'no-store',
      });
      return res.end(page);
    }
    const routes = ['/answer', '/system', '/notes', '/finish'];
    if (!routes.includes(url.pathname)) throw new HttpError(404, 'not found');
    if (req.method !== 'POST') throw new HttpError(405, 'POST only');
    if (!sameToken(req.headers[TOKEN_HEADER], token)) throw new HttpError(403, 'missing or wrong token');
    if (!/^application\/json\s*(;|$)/i.test(String(req.headers['content-type'] ?? ''))) throw new HttpError(415, 'JSON only');
    const body = await readJson(req, maxBody);
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw new HttpError(400, 'expected a JSON object');
    if (finishing) throw new HttpError(409, 'the run has finished');
    const at = new Date();
    let reply = { ok: true };
    if (url.pathname === '/answer') {
      const items = getItems();
      const item = items.find((it) => it.key === body.key);
      if (typeof body.key !== 'string' || !item) throw new HttpError(400, 'no such line');
      if (!VERDICTS.includes(body.verdict)) throw new HttpError(400, 'verdict must be pass, fail or skip');
      const note = str(body.note, LIMITS.note, 'note');
      results.answers[item.key] = { verdict: body.verdict, note, date: localDate(at), line: item.line, text: item.text.slice(0, 100) };
      save();
      log({ event: 'answer', at: at.toISOString(), line: item.line, section: item.section, verdict: body.verdict, note, text: item.text.slice(0, 120) });
      reply = { ok: true, answered: items.filter((it) => results.answers[it.key]).length, total: items.length };
    } else if (url.pathname === '/system') {
      results.system = str(body.system, LIMITS.system, 'system');
      save();
      log({ event: 'system', at: at.toISOString(), system: results.system });
    } else if (url.pathname === '/notes') {
      results.notes = str(body.notes, LIMITS.notes, 'notes');
      save();
      log({ event: 'notes', at: at.toISOString(), notes: results.notes });
    } else {
      // 'finished' goes once the reply is sent or the connection is gone (the tab closed while finish() ran), so the
      // tool stops either way.
      finishing = true;
      let closed = false, done = false;
      res.once('close', () => { closed = true; if (done) server.emit('finished'); });
      try { reply = { ok: true, summary: await finish() }; } catch (e) { finishing = false; throw e; }
      done = true;
      if (closed) server.emit('finished');
    }
    res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    res.end(JSON.stringify(reply));
  }

  const server = http.createServer((req, res) => {
    handle(req, res).catch((e) => {
      const status = e instanceof HttpError ? e.status : 500;
      if (!res.headersSent) res.writeHead(status, { 'content-type': 'text/plain; charset=utf-8', connection: 'close' });
      res.end(e instanceof HttpError ? e.message : 'internal error: ' + e.message);
    });
  });
  const listen = (p = 0) => new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(p, HOST, () => { server.off('error', reject); port = server.address().port; resolve(port); });
  });
  return { server, listen };
}
