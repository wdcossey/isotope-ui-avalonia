#!/usr/bin/env node
// Groups the errors in an MSBuild error log (-flp:logfile=<f>;errorsonly) by code, by missing name and by file.
// Usage: node tools/build-errors.mjs <logfile> [--json]
import { readFileSync } from 'node:fs';
import path from 'node:path';

const [logFile, flag] = process.argv.slice(2);
const lines = readFileSync(logFile, 'utf8').split(/\r?\n/);
const re = /^(.*?)\((\d+),(\d+)\): error (\w+): (.*?)(?: \[[^\]]*\])?$/;
// Errors with no source position: MSBuild (MSB3073 when the rewrite Exec fails), NuGet restore (NU1301), CSC.
const reNoPos = /^(?:\d+:\d+>)?(.*?) ?: error (\w+): (.*?)(?: \[[^\]]*\])?$/;
let unparsed = 0;
const seen = new Set();
const errors = [];
for (const l of lines) {
  let m = re.exec(l.trim());
  if (!m) {
    const n = reNoPos.exec(l.trim());
    if (n) m = [l, n[1], '0', '0', n[2], n[3]];
    else { if (/: error /.test(l)) unparsed++; continue; }
  }
  const key = m.slice(1, 6).join('|');
  if (seen.has(key)) continue;
  seen.add(key);
  const [, file, line, col, code, msg] = m;
  const name = (/'([^']+)'/.exec(msg) || [])[1] ?? '';
  // For CS0234 the missing piece is the first quoted name inside the second (namespace) one.
  const ns = code === 'CS0234' ? ((/namespace '([^']+)'/.exec(msg) || [])[1] ?? '') + '.' + name : name;
  errors.push({ file: file.replace(/\\/g, '/'), line: +line, col: +col, code, msg, name: ns });
}
const count = (f) => Object.entries(errors.reduce((a, e) => ((a[f(e)] = (a[f(e)] || 0) + 1), a), {})).sort((a, b) => b[1] - a[1]);
const rel = (f) => f.replace(/^.*?\/(src\/Reactor|obj\/rewritten)\//, '');
if (unparsed) console.error('build-errors: ' + unparsed + ' error line(s) not understood; read the log: ' + logFile);
const out = {
  total: errors.length,
  unparsed,
  byCode: count((e) => e.code),
  byName: count((e) => e.code + ' ' + e.name),
  byFile: count((e) => rel(e.file)),
};
if (flag === '--json') { out.errors = errors; console.log(JSON.stringify(out, null, 2)); }
else {
  console.log('total', out.total);
  for (const k of ['byCode', 'byName', 'byFile']) {
    console.log('\n## ' + k);
    for (const [n, c] of out[k]) console.log(String(c).padStart(5), n);
  }
}
