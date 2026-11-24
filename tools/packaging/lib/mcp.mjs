// A minimal MCP client over stdio for the tool package's smoke test (#60): starts the server, initializes, and sends
// requests as newline-delimited JSON-RPC, as MCP's stdio transport does. Only what the smoke test needs.
import { spawn } from 'node:child_process';

/** Starts command with args and returns {request(method, params), close()}. Fails a request after timeoutMs. */
export function startMcp(command, args, { env, timeoutMs = 60000 } = {}) {
  const child = spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'], env: { ...process.env, ...env }, shell: false });
  const waiting = new Map();
  // exited: why requests now fail (set on the first failure); gone: the process itself has ended or never started.
  let buffer = '', nextId = 0, stderr = '', exited = null, gone = false;
  child.stdout.setEncoding('utf8');
  child.stdout.on('data', (chunk) => {
    buffer += chunk;
    for (let i; (i = buffer.indexOf('\n')) >= 0;) {
      const line = buffer.slice(0, i).trim();
      buffer = buffer.slice(i + 1);
      if (!line) continue;
      let message;
      try { message = JSON.parse(line); } catch { failAll(new Error('the server wrote a line to stdout that is not JSON-RPC: ' + line.slice(0, 200))); continue; }
      const w = message.id != null && waiting.get(message.id);
      if (w) { waiting.delete(message.id); clearTimeout(w.timer); message.error ? w.reject(new Error(w.method + ': ' + JSON.stringify(message.error))) : w.resolve(message.result); }
    }
  });
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', (chunk) => { stderr += chunk; });
  const ended = (error) => { if (exited === null) exited = error; failAll(error); };
  // A server that can't start ('error', with no 'exit'), or that dies with requests unanswered, fails them with its stderr.
  child.on('error', (e) => { gone = true; ended(new Error('the server did not start: ' + e.message)); });
  child.on('exit', (code, signal) => (gone = true) && ended(new Error('the server exited (' + (signal ?? 'code ' + code) + ')' + (stderr ? ': ' + stderr.slice(-2000) : ''))));
  // Writing to a server that has exited fails here, not as an unhandled 'error' event.
  child.stdin.on('error', (e) => ended(new Error('the server stopped reading its input (' + e.code + ')' + (stderr ? ': ' + stderr.slice(-2000) : ''))));
  function failAll(error) { for (const [id, w] of waiting) { waiting.delete(id); clearTimeout(w.timer); w.reject(error); } }
  const send = (message) => child.stdin.write(JSON.stringify({ jsonrpc: '2.0', ...message }) + '\n');
  return {
    request(method, params = {}) {
      if (exited !== null) return Promise.reject(exited);
      const id = ++nextId;
      return new Promise((resolve, reject) => {
        const timer = setTimeout(() => { waiting.delete(id); reject(new Error(method + ': no answer in ' + timeoutMs + ' ms')); }, timeoutMs);
        waiting.set(id, { resolve, reject, timer, method });
        send({ id, method, params });
      });
    },
    /** Whether the server process is still running. */
    get running() { return !gone; },
    notify(method, params = {}) { if (exited === null) send({ method, params }); },
    /** Ends the server's input, stops it if it is still running 5 s later, and resolves once it has gone. */
    close() {
      return new Promise((resolve) => {
        if (gone || child.exitCode !== null || child.pid === undefined) return resolve();
        const timer = setTimeout(() => child.kill(), 5000);
        child.once('exit', () => { clearTimeout(timer); resolve(); });
        child.once('error', () => { clearTimeout(timer); resolve(); });
        child.stdin.end();
      });
    },
  };
}

/** Initializes a session; returns the server's initialize result. */
export async function initialize(server) {
  const result = await server.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'isotope-pack-smoke', version: '1' } });
  server.notify('notifications/initialized');
  return result;
}
