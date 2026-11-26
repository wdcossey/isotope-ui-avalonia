// Writes a run's answers back into the checklist (#106). Only the lines of answered items change: a tick, and a note
// appended to the item's last line (wrapped onto continuation lines past WIDTH). Every other byte, line endings
// included, stays as it was.
import { dominantEol, joinLines, parseChecklist, sections } from './parse.mjs';

export const WIDTH = 120;
export const VERDICTS = ['pass', 'fail', 'skip'];

// A note as one line of Markdown: whitespace runs (newlines included) become one space.
export const oneLine = (s) => String(s ?? '').replace(/\s+/g, ' ').trim();

// A word that would start a list, a quote or a heading if a wrapped line began with it.
const BLOCK_START = /^([-*+>#]|\d+[.)]|\[)/;

function setTick(line, at, ticked) {
  const c = line.body[at];
  if (ticked ? c !== ' ' : c === ' ') return false;
  line.body = line.body.slice(0, at) + (ticked ? 'x' : ' ') + line.body.slice(at + 1);
  return true;
}

// Appends text to an item's last line, wrapping at WIDTH onto lines indented like its continuation lines.
function appendWrapped(lines, item, text, eol) {
  const last = lines[item.last];
  const cont = item.last > item.first ? /^\s*/.exec(last.body)[0] : ' '.repeat(item.indent + 2);
  const out = [];
  let cur = last.body;
  for (const word of text.split(' ')) {
    if (cur.length + 1 + word.length <= WIDTH || cur.trim() === '' || BLOCK_START.test(word)) cur += (cur === cont ? '' : ' ') + word;
    else { out.push(cur); cur = cont + word; }
  }
  out.push(cur);
  const between = last.eol || eol;
  const added = out.map((body, i) => ({ body, eol: i === out.length - 1 ? last.eol : between }));
  lines.splice(item.last, 1, ...added);
  return added.length - 1;
}

const where = (system, date) => (system ? system + ', ' : '') + date;

// The text appended for an answer, or '' for none.
export function annotation(answer, system) {
  const note = oneLine(answer.note);
  const at = where(oneLine(system), answer.date);
  if (answer.verdict === 'fail') return note ? `**Failed (${at}):** ${note}` : `**Failed (${at}).**`;
  if (answer.verdict === 'pass' && note) return `(${at}: ${note})`;
  return '';
}

// Unticks every item. Returns the new text and how many were ticked.
export function clearTicks(text) {
  const { lines, items } = parseChecklist(text);
  let cleared = 0;
  for (const it of items) if (setTick(lines[it.first], it.tickAt, false)) cleared++;
  return { text: joinLines(lines), cleared };
}

// Applies results ({ system, answers: { key: { verdict, note, date } } }) to the text: a pass ticks its line, a fail
// unticks it and appends the failure, a skip leaves it. Answers whose key matches no line are left out; they are
// returned as unmatched. Applying the same results twice changes nothing more, since an annotated line's key changes.
export function applyResults(text, results) {
  const { lines, items } = parseChecklist(text);
  const eol = dominantEol(lines);
  const answers = results.answers ?? {};
  const known = new Set(items.map((it) => it.key));
  const applied = { pass: 0, fail: 0, skip: 0 };
  let shift = 0;
  for (const item of items) {
    const a = answers[item.key];
    if (!a || !VERDICTS.includes(a.verdict)) continue;
    applied[a.verdict]++;
    if (a.verdict === 'skip') continue;
    const at = { ...item, first: item.first + shift, last: item.last + shift };
    setTick(lines[at.first], at.tickAt, a.verdict === 'pass');
    const extra = annotation(a, results.system);
    if (extra) shift += appendWrapped(lines, at, extra, eol);
  }
  const unmatched = Object.keys(answers).filter((k) => !known.has(k));
  return { text: joinLines(lines), applied, unmatched };
}

// Counts per section, the failed lines, and a suggested "Last run" sentence for the checklist's prose (which the
// tool doesn't write). items are the lines as they were before the write-back.
export function summarize(items, results, { date, commit } = {}) {
  const answers = results.answers ?? {};
  const verdictOf = (it) => answers[it.key]?.verdict ?? 'none';
  const total = { pass: 0, fail: 0, skip: 0, none: 0 };
  const bySection = sections(items).map((s) => {
    const counts = { pass: 0, fail: 0, skip: 0, none: 0 };
    for (const it of s.items) { counts[verdictOf(it)]++; total[verdictOf(it)]++; }
    return { section: s.name, ...counts };
  });
  const failed = items.filter((it) => verdictOf(it) === 'fail')
    .map((it) => ({ section: it.section, line: it.line, text: it.text, note: oneLine(answers[it.key].note) }));
  const system = oneLine(results.system);
  const failedSections = bySection.filter((s) => s.fail).map((s) => s.section + (s.fail > 1 ? ` (${s.fail})` : ''));
  let sentence = `**Last run:** ${date}, ${system || '<system>'}, at \`${commit || '<commit>'}\`: `;
  sentence += `${total.pass} of ${items.length} lines passed`;
  if (total.fail) sentence += `, ${total.fail} failed (${failedSections.join(', ')})`;
  if (total.skip) sentence += `, ${total.skip} skipped`;
  if (total.none) sentence += `, ${total.none} not run`;
  sentence += '.';
  return { total, bySection, failed, notes: String(results.notes ?? '').trim(), sentence };
}

const plural = (n, word) => `${n} ${word}`;

// The summary as text for the terminal.
export function formatSummary(s) {
  const out = ['Summary:'];
  for (const x of s.bySection) {
    const parts = [plural(x.pass, 'passed')];
    if (x.fail) parts.push(plural(x.fail, 'failed'));
    if (x.skip) parts.push(plural(x.skip, 'skipped'));
    if (x.none) parts.push(plural(x.none, 'not run'));
    out.push(`  ${x.section || '(no heading)'}: ${parts.join(', ')}`);
  }
  if (s.failed.length) {
    out.push('Failed:');
    for (const f of s.failed) out.push(`  line ${f.line} (${f.section || 'no heading'}): ${f.text}${f.note ? '\n    note: ' + f.note : ''}`);
  }
  if (s.notes) out.push('Anything else:', ...s.notes.split(/\r?\n/).map((l) => '  ' + l));
  out.push('Suggested "Last run" sentence (not written to the file):', '  ' + s.sentence);
  return out.join('\n');
}
