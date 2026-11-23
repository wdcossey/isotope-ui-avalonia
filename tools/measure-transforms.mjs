#!/usr/bin/env node
// Measures what tools/sync's transforms do to the upstream files a props file compiles: for each file, the rules that
// changed it (from src/Upstream/vendored.json) and the lines that differ from upstream at the commit in UPSTREAM.lock
// (rewritten in place, or blanked by a member drop). The vendored header line is not counted. Reads, writes nothing.
// Usage: node tools/measure-transforms.mjs <files.props> [file ...]
// The props file lists $(UpstreamSrc)<path> includes, relative to upstream's src/Reactor/. With file arguments
// (paths in the same form), reports only those files; the totals always cover every file. Needs the upstream/ cache.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseLock } from './sync/lib/lock.mjs';
import { loadTransforms } from './sync/lib/transforms.mjs';

const repo = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const [propsFile, ...only] = process.argv.slice(2);
if (!propsFile) { console.error('usage: node tools/measure-transforms.mjs <files.props> [file ...]'); process.exit(2); }
const props = readFileSync(propsFile, 'utf8');
const rels = [...props.matchAll(/Include="\$\(UpstreamSrc\)([^"]+)"/g)].map((m) => m[1].replace(/\\/g, '/'));
const { baseline } = parseLock(readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8'));
const manifest = JSON.parse(readFileSync(path.join(repo, 'src', 'Upstream', 'vendored.json'), 'utf8'));
if (manifest.commit !== baseline) throw new Error('src/Upstream was generated at ' + manifest.commit + ', not at the commit in UPSTREAM.lock');
const byPath = new Map(manifest.files.map((f) => [f.path, f]));
const rules = new Map(loadTransforms(JSON.parse(readFileSync(path.join(repo, 'tools', 'sync', 'transforms.json'), 'utf8'))).map((r) => [r.id, r]));
const lines = (s) => s.replace(/^\u{FEFF}/u, '').split(/\r?\n/);

const totals = { files: 0, lines: 0, touchedFiles: 0, rewritten: 0, blanked: 0 };
const used = new Set();
for (const rel of rels) {
  const p = 'src/Reactor/' + rel;
  const entry = byPath.get(p);
  if (!entry) throw new Error(p + ' is not vendored (tools/sync/tiers.json: tier 1 or 2)');
  const up = execFileSync('git', ['-C', path.join(repo, 'upstream'), 'show', baseline + ':' + p], { encoding: 'utf8', maxBuffer: 1 << 26 });
  const before = lines(up);
  const after = lines(readFileSync(path.join(repo, 'src', 'Upstream', ...p.split('/')), 'utf8')).slice(p.endsWith('.cs') ? 1 : 0);
  if (after.length !== before.length) throw new Error(p + ': line count differs from upstream');
  let rewritten = 0, blanked = 0;
  for (let i = 0; i < before.length; i++) {
    if (before[i] === after[i]) continue;
    if (after[i].trim() === '') blanked++; else rewritten++;
  }
  for (const id of entry.transforms) used.add(id);
  const lineCount = before.length - (up.endsWith('\n') ? 1 : 0); // as wc -l counts
  totals.files++;
  totals.lines += lineCount;
  if (rewritten + blanked) totals.touchedFiles++;
  totals.rewritten += rewritten;
  totals.blanked += blanked;
  if (only.length && !only.includes(rel)) continue;
  const drops = entry.transforms.filter((id) => rules.get(id)?.kind === 'drop').length;
  console.log(rel + ': ' + lineCount + ' lines; tier ' + entry.tier + '; ' + entry.transforms.length + ' rules hit (' + drops + ' member drops); ' +
    rewritten + ' lines rewritten, ' + blanked + ' lines blanked');
}
const kinds = [...used].map((id) => rules.get(id));
const count = (f) => kinds.filter(f).length;
console.log('all ' + totals.files + ' files: ' + totals.lines + ' lines; ' + totals.touchedFiles + ' files transformed; ' + totals.rewritten +
  ' lines rewritten, ' + totals.blanked + ' lines blanked; ' + used.size + ' rules hit them (' + count((r) => r.kind === 'find' && r.files) +
  ' find/replace, ' + count((r) => r.kind === 'drop') + ' member drops, ' + count((r) => r.glob) + ' repo-wide) of ' + rules.size + ' in tools/sync/transforms.json');
