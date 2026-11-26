// A small HTTP client for the tests: any Host, Origin or body, and the status, headers and text back.
import http from 'node:http';

export function request(port, { method = 'GET', path = '/', headers = {}, body } = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, method, path, headers: { host: `127.0.0.1:${port}`, ...headers }, agent: false }, (res) => {
      const chunks = [];
      res.on('data', (d) => chunks.push(d));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, text: Buffer.concat(chunks).toString('utf8') }));
    });
    req.on('error', (e) => (e.code === 'ECONNRESET' || e.code === 'EPIPE' ? resolve({ status: 'reset' }) : reject(e)));
    if (typeof body === 'function') body(req);
    else req.end(body);
  });
}

export const post = (port, path, json, token, headers = {}) => request(port, {
  method: 'POST', path, body: JSON.stringify(json),
  headers: { 'content-type': 'application/json', ...(token ? { 'x-checklist-token': token } : {}), ...headers },
});

// The token the page carries.
export const tokenOf = (html) => /<meta name="checklist-token" content="([0-9a-f]+)">/.exec(html)?.[1];
