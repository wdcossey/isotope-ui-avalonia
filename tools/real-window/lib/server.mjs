// The checklist page and its HTTP endpoints (#106). Local only: it listens on 127.0.0.1; every request's Host must be
// 127.0.0.1:<port> or localhost:<port> and its Origin, when sent, the same origin (against DNS rebinding); every POST
// carries the run's random token in a header (against cross-site requests), is JSON, and has a capped body. Everything
// rendered is escaped, and a content security policy allows only the page's own script. No request names a file: the
// server reads and writes only through the callbacks it is given.
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

// A checklist line as HTML: escaped first, then **bold** and `code` (which escaping leaves alone) made into tags.
export const inlineMarkdown = (s) => escapeHtml(s)
  .replace(/`([^`]+)`/g, '<code>$1</code>')
  .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>');

const STYLE = `body{font:14px system-ui,sans-serif;max-width:920px;margin:1.5em auto;padding:0 1em}
h2{margin-top:1.6em;border-bottom:1px solid #ccc;font-size:1.2em}
.it{padding:.6em .4em;border-bottom:1px solid #eee}.was{font-size:11px;background:#eee;padding:1px 4px;border-radius:3px}
.b{margin-top:.4em;display:flex;gap:.4em;align-items:center}.b input{flex:1;padding:3px}.s{font-weight:600;min-width:5em}
button{padding:2px 10px}.v-pass{background:#eefbea}.v-fail{background:#fdecec}.v-skip{background:#f4f4f4}
.v-pass .s{color:green}.v-fail .s{color:crimson}.v-skip .s{color:#777}
#top{position:sticky;top:0;background:#fff;padding:.5em 0;border-bottom:1px solid #ccc;display:flex;gap:.6em;align-items:center}
#top input{width:22em;padding:3px}textarea{width:100%;box-sizing:border-box}pre{white-space:pre-wrap;background:#f6f6f6;padding:.8em}`;

// The page's script: one click per line sends its verdict and note; changing a note resends the line's verdict.
const SCRIPT = `const token = document.querySelector('meta[name="checklist-token"]').content;
async function post(path, body) {
  const r = await fetch(path, { method: 'POST', headers: { 'content-type': 'application/json', '${TOKEN_HEADER}': token }, body: JSON.stringify(body) });
  if (!r.ok) throw new Error(r.status + ' ' + (await r.text()));
  return r.json();
}
function progress(r) { if (r && r.total != null) document.getElementById('progress').textContent = r.answered + ' of ' + r.total + ' answered'; }
async function answer(it, verdict) {
  const s = it.querySelector('.s');
  try {
    progress(await post('/answer', { key: it.dataset.key, verdict, note: it.querySelector('.note').value }));
    it.dataset.verdict = verdict; it.className = 'it v-' + verdict; s.textContent = verdict;
  } catch (e) { s.textContent = 'error: ' + e.message; }
}
document.addEventListener('click', async (e) => {
  const b = e.target.closest('button[data-verdict]');
  if (b) return answer(b.closest('.it'), b.dataset.verdict);
  if (e.target.id === 'finish') {
    if (!confirm('Write the answers into the checklist and stop the tool?')) return;
    try {
      const r = await post('/finish', {});
      document.getElementById('summary').textContent = r.summary;
      for (const x of document.querySelectorAll('button, input, textarea')) x.disabled = true;
    } catch (err) { document.getElementById('summary').textContent = 'error: ' + err.message; }
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

export function renderPage({ title, items, results, token, nonce }) {
  const answers = results.answers ?? {};
  const answered = items.filter((it) => answers[it.key]).length;
  let body = '';
  for (const s of sections(items)) {
    body += '<h2>' + escapeHtml(s.name || '(no heading)') + '</h2>';
    for (const it of s.items) {
      const a = answers[it.key];
      body += `<div class="it${a ? ' v-' + escapeHtml(a.verdict) : ''}" data-key="${escapeHtml(it.key)}" data-verdict="${escapeHtml(a?.verdict ?? '')}">`
        + `<div class="t">${it.ticked ? '<span class="was">ticked</span> ' : ''}${inlineMarkdown(it.text)}</div><div class="b">`
        + VERDICTS.map((v) => `<button data-verdict="${v}">${v[0].toUpperCase() + v.slice(1)}</button>`).join('')
        + `<input class="note" placeholder="note (optional)" value="${escapeHtml(a?.note ?? '')}"><span class="s">${escapeHtml(a?.verdict ?? '')}</span></div></div>`;
    }
  }
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="checklist-token" content="${escapeHtml(token)}">`
    + `<title>${escapeHtml(title)}</title><style nonce="${nonce}">${STYLE}</style></head><body>`
    + `<h1>${escapeHtml(title)}</h1><div id="top"><label>System <input id="sys" placeholder="OS, display server, version" value="${escapeHtml(results.system)}"></label>`
    + `<span id="syss"></span><span id="progress">${answered} of ${items.length} answered</span></div>${body}`
    + `<h2>Anything else</h2><textarea id="free" rows="5">${escapeHtml(results.notes)}</textarea><div id="frees"></div>`
    + `<p><button id="finish">Finish: write the answers into the checklist</button></p><pre id="summary"></pre>`
    + `<script nonce="${nonce}">${SCRIPT}</script></body></html>`;
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
      res.writeHead(200, {
        'content-type': 'text/html; charset=utf-8',
        'content-security-policy': `default-src 'none'; script-src 'nonce-${nonce}'; style-src 'nonce-${nonce}'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`,
        'x-frame-options': 'DENY', 'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer', 'cache-control': 'no-store',
      });
      return res.end(renderPage({ title, items: getItems(), results, token, nonce }));
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
      finishing = true;
      try { reply = { ok: true, summary: await finish() }; } catch (e) { finishing = false; throw e; }
      res.once('finish', () => server.emit('finished'));
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
