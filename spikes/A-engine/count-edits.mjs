#!/usr/bin/env node
// Counts Spike A's edit lines: the distinct source lines with an error our layer can't absorb. Each error of a build
// log is classified from seam.json:
//   seam  a missing member (CS1061/CS0117) on a stubbed upstream type, or a constructor shape (CS1729) of one;
//   shim  a missing member or constructor on a WinUI-concept shim type, or a conversion (CS1503/CS0029) from or to a
//         shim type (the shims live in WinUI's namespaces, Microsoft.UI.Xaml.*);
//   edit  anything else.
// Reproduces outcomes.json at step 3 (89 edit lines in the 18 "needs edits" files).
// Usage: node spikes/A-engine/count-edits.mjs [step]   (reads logs/run-step<step>.log; run run-spike.mjs first)
//        add --json for the per-file counts as JSON.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const json = args.includes('--json');
const step = args.find((a) => !a.startsWith('--')) ?? '3';
const log = path.join(here, 'logs', 'run-step' + step + '.log');
const seam = JSON.parse(readFileSync(path.join(here, 'seam.json'), 'utf8'));
const outcomes = JSON.parse(readFileSync(path.join(here, 'outcomes.json'), 'utf8'));
const { errors } = JSON.parse(execFileSync('node', [path.join(here, '..', '..', 'tools', 'build-errors.mjs'), log, '--json'],
  { encoding: 'utf8', maxBuffer: 1 << 26 }));

const seamTypes = new Set(Object.keys(seam.types));
const shimTypes = new Set(Object.keys(seam.shimSurface.types));
const ctorTypes = new Set(seam.constructorShapes);
const has = (set, name) => { const n = name.replace(/<.*$/, ''); return set.has(n) || set.has(n.split('.').pop()); };
export function kind(e) {
  if (e.code === 'CS1061' || e.code === 'CS0117') return has(seamTypes, e.name) ? 'seam' : has(shimTypes, e.name) ? 'shim' : 'edit';
  if (e.code === 'CS1729') return has(ctorTypes, e.name) ? 'seam' : has(shimTypes, e.name) ? 'shim' : 'edit';
  if ((e.code === 'CS1503' || e.code === 'CS0029') && /'Microsoft\.UI\.Xaml\./.test(e.msg)) return 'shim';
  return 'edit';
}

// Rewritten copies report their upstream path through #line, so both forms name the same file.
const rel = (f) => f.replace(/^.*?\/(?:src\/Reactor|obj\/rewritten)\//, '');
const per = new Map();
for (const e of errors) {
  const f = rel(e.file);
  if (!per.has(f)) per.set(f, { seam: 0, shim: 0, edit: 0, lines: new Set() });
  const p = per.get(f), k = kind(e);
  p[k]++;
  if (k === 'edit') p.lines.add(e.line);
}
const needs = outcomes.filter((o) => o.outcome === 'needs edits').map((o) => o.file);
const row = (f) => { const p = per.get(f) ?? { seam: 0, shim: 0, edit: 0, lines: new Set() }; return { file: f, editLines: p.lines.size, seam: p.seam, shim: p.shim, edit: p.edit }; };
const rows = needs.map(row);
const others = [...per.keys()].filter((f) => !needs.includes(f)).map(row).filter((r) => r.editLines);
const sum = (rs) => rs.reduce((a, r) => a + r.editLines, 0);
const out = { step, errors: errors.length, needsEdits: { files: rows.filter((r) => r.editLines).length, editLines: sum(rows), rows }, otherFiles: { editLines: sum(others), rows: others } };
if (json) console.log(JSON.stringify(out, null, 2));
else {
  console.log('step ' + step + ': ' + errors.length + ' errors');
  console.log('the 18 "needs edits" files of step 3: ' + out.needsEdits.editLines + ' edit lines in ' + out.needsEdits.files + ' files');
  for (const r of rows) console.log(String(r.editLines).padStart(5) + '  ' + r.file + '  (seam ' + r.seam + ', shim ' + r.shim + ')');
  console.log('other files: ' + out.otherFiles.editLines + ' edit lines' + (others.length ? ' (' + others.map((r) => r.file + ' ' + r.editLines).join(', ') + ')' : ''));
}
