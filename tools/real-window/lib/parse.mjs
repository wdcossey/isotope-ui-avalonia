// Reads a Markdown checklist (#106): its `- [ ]` lines, each under its nearest heading, with the line endings kept so a
// write-back can change a line and leave every other byte as it was.
import { createHash } from 'node:crypto';

const ITEM = /^(\s*)([-*+]) \[([ xX])\](?: (.*))?$/;
const HEADING = /^ {0,3}#{1,6}[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/;
// A code fence, at any indent so one inside a list item counts; a closing fence has nothing after its run.
const FENCE = /^\s*(`{3,}|~{3,})(.*)$/;

// Splits text into lines, each with its own ending ('\r\n', '\n', or '' for a last line with none), so that joining
// every body and ending gives the text back byte for byte.
export function splitLines(text) {
  const lines = [];
  const re = /\r?\n/g;
  let start = 0, m;
  while ((m = re.exec(text))) {
    lines.push({ body: text.slice(start, m.index), eol: m[0] });
    start = m.index + m[0].length;
  }
  if (start < text.length) lines.push({ body: text.slice(start), eol: '' });
  return lines;
}

export const joinLines = (lines) => lines.map((l) => l.body + l.eol).join('');

// The ending most lines use, for a line the write-back adds after a last line with none.
export function dominantEol(lines) {
  let crlf = 0, lf = 0;
  for (const l of lines) { if (l.eol === '\r\n') crlf++; else if (l.eol === '\n') lf++; }
  return crlf > lf ? '\r\n' : '\n';
}

const indentOf = (s) => /^\s*/.exec(s)[0].length;
// Any list item, checklist or not: a nested one ends the item above it, so a note is never appended to it.
const LIST_ITEM = /^\s*([-*+]|\d{1,9}[.)])(\s|$)/;

// The checklist's items, in order. Each has a key (a hash of its section and text, numbered if the same line appears
// twice in a section), its 1-based line number, its first and last line indexes, its section, whether it is ticked,
// and its text with the continuation lines joined by spaces. Lines inside code fences are not items or headings;
// indented (four-space) code blocks are not recognised, so a checklist line shown in one would count as an item.
export function parseChecklist(text) {
  const lines = splitLines(text);
  const items = [];
  const seen = new Map();
  let section = '', fence = null;
  for (let i = 0; i < lines.length; i++) {
    const body = lines[i].body;
    const f = FENCE.exec(body);
    if (fence) { if (f && f[1][0] === fence[0] && f[1].length >= fence.length && !f[2].trim()) fence = null; continue; }
    if (f) { fence = f[1]; continue; }
    const h = HEADING.exec(body);
    if (h) { section = h[1]; continue; }
    const m = ITEM.exec(body);
    if (!m) continue;
    const indent = m[1].length;
    let text = (m[4] ?? '').trim();
    let last = i;
    while (last + 1 < lines.length) {
      const next = lines[last + 1].body;
      if (!next.trim() || indentOf(next) <= indent || LIST_ITEM.test(next) || HEADING.test(next) || FENCE.test(next)) break;
      text += ' ' + next.trim();
      last++;
    }
    const base = createHash('sha256').update(section + '\n' + text).digest('hex').slice(0, 12);
    const n = (seen.get(base) ?? 0) + 1;
    seen.set(base, n);
    items.push({
      key: n === 1 ? base : base + '-' + n,
      line: i + 1, first: i, last, indent, tickAt: indent + 3,
      section, ticked: m[3] !== ' ', text,
    });
    i = last;
  }
  return { lines, items };
}

// The sections in order, each with its items.
export function sections(items) {
  const out = [];
  for (const it of items) {
    if (!out.length || out[out.length - 1].name !== it.section) out.push({ name: it.section, items: [] });
    out[out.length - 1].items.push(it);
  }
  return out;
}
