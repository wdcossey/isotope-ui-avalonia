// The answers of a run, saved beside the checklist as <checklist>.results.json (git-ignored) as each is given, so a
// closed tab or a crash loses nothing and the next run resumes (#106). It holds the checklist's file name (never its
// path), the answers, and what the developer typed: nothing about the machine or the user.
import { existsSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { VERDICTS } from './writeback.mjs';

export const resultsPathFor = (checklist) => checklist + '.results.json';

export const LIMITS = { note: 2000, system: 200, notes: 20000 };

export function emptyResults(checklist) {
  return { version: 1, checklist: path.basename(checklist), system: '', notes: '', answers: {} };
}

// Reads the saved results, or empty ones if there are none. A file that isn't a results file is an error, never
// overwritten.
export function loadResults(file, checklist) {
  if (!existsSync(file)) return emptyResults(checklist);
  let r;
  try { r = JSON.parse(readFileSync(file, 'utf8')); } catch (e) { throw new Error(`${path.basename(file)} is not valid JSON (${e.message}); move it away to start a new run`); }
  if (r?.version !== 1 || typeof r.answers !== 'object' || r.answers === null || Array.isArray(r.answers)) {
    throw new Error(`${path.basename(file)} is not a results file of this tool; move it away to start a new run`);
  }
  const out = emptyResults(checklist);
  out.system = typeof r.system === 'string' ? r.system.slice(0, LIMITS.system) : '';
  out.notes = typeof r.notes === 'string' ? r.notes.slice(0, LIMITS.notes) : '';
  for (const [key, a] of Object.entries(r.answers)) {
    if (!a || !VERDICTS.includes(a.verdict)) continue;
    out.answers[key] = {
      verdict: a.verdict,
      note: typeof a.note === 'string' ? a.note.slice(0, LIMITS.note) : '',
      date: typeof a.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(a.date) ? a.date : localDate(),
      line: Number.isInteger(a.line) ? a.line : undefined,
      text: typeof a.text === 'string' ? a.text : undefined,
    };
  }
  return out;
}

// Written to <file>.tmp and renamed over the file, so a crash mid-write leaves the last saved answers whole. If the
// rename is refused (a file held open on Windows), the file is written in place instead.
export function saveResults(file, results) {
  const text = JSON.stringify(results, null, 2) + '\n';
  const tmp = file + '.tmp';
  writeFileSync(tmp, text);
  try { renameSync(tmp, file); } catch { writeFileSync(file, text); rmSync(tmp, { force: true }); }
}

export function deleteResults(file) {
  rmSync(file, { force: true });
}

// Today's date where the developer is (YYYY-MM-DD), for the notes the write-back appends.
export function localDate(d = new Date()) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
