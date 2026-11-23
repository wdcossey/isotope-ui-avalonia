#!/usr/bin/env node
// Measures what the upstream transforms do: for each file a props file links, the rules that hit it and the lines
// they change (rewritten in place, or blanked by a member drop). Reads upstream as linked, writes nothing.
// Usage: node tools/measure-transforms.mjs <files.props> <rules.json> <upstreamSrcDir> [file ...]
// With file arguments (paths relative to upstreamSrcDir), reports only those files.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { applyRules, loadRules } from './rewrite-upstream.mjs';

const [propsFile, rulesFile, srcDir, ...only] = process.argv.slice(2);
const props = readFileSync(propsFile, 'utf8');
const rels = [...props.matchAll(/Include="\$\(UpstreamSrc\)([^"]+)"/g)].map((m) => m[1].replace(/\\/g, '/'));
const rules = loadRules(rulesFile);
let totals = { files: 0, lines: 0, touchedFiles: 0, rewritten: 0, blanked: 0 };
const rows = [];
for (const rel of rels) {
  const text = readFileSync(path.join(srcDir, rel), 'utf8');
  const before = text.split('\n');
  const perRule = rules.map((r) => r.hits);
  const { text: out } = applyRules(rel, text, rules);
  const after = out.split('\n');
  if (after.length !== before.length) throw new Error(rel + ': line count changed');
  const lineCount = before.length - (text.endsWith('\n') ? 1 : 0); // as wc -l counts
  let rewritten = 0, blanked = 0;
  for (let i = 0; i < before.length; i++) {
    if (before[i] === after[i]) continue;
    if (after[i].trim() === '' ) blanked++; else rewritten++;
  }
  const hit = rules.filter((r, i) => r.hits > perRule[i]);
  totals.files++;
  totals.lines += lineCount;
  if (rewritten + blanked) totals.touchedFiles++;
  totals.rewritten += rewritten;
  totals.blanked += blanked;
  if (only.length && !only.includes(rel)) continue;
  rows.push({ rel, lines: lineCount, rules: hit.length, drops: hit.filter((r) => r.drop !== undefined).length, rewritten, blanked });
}
for (const r of rows)
  console.log(`${r.rel}: ${r.lines} lines; ${r.rules} rules hit (${r.drops} member drops); ${r.rewritten} lines rewritten, ${r.blanked} lines blanked`);
console.log(`all ${totals.files} linked files: ${totals.lines} lines; ${totals.touchedFiles} files transformed; ${totals.rewritten} lines rewritten, ${totals.blanked} lines blanked; ${rules.length} rules`);
