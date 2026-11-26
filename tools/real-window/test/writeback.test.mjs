// Tests of the write-back (#106): node --test tools/real-window/test/*.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseChecklist, splitLines } from '../lib/parse.mjs';
import { WIDTH, applyResults, clearTicks, formatSummary, summarize } from '../lib/writeback.mjs';
import { LINES, fixture } from './fixture.mjs';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const DATE = '2026-11-26';
const keyAt = (text, line) => parseChecklist(text).items.find((it) => it.line === line).key;

// Answers for the fixture: a multi-line pass with a note, a skip on a ticked line, a fail with a note, a pass, and a
// fail with no note on a ticked multi-line line.
function answersFor(text) {
  const a = (line, verdict, note = '') => [keyAt(text, line), { verdict, note, date: DATE }];
  return {
    system: 'Win 11',
    answers: Object.fromEntries([
      a(14, 'pass', 'fine on a 4K display'), a(16, 'skip', 'not tried'), a(17, 'fail', 'menu\nlags'),
      a(21, 'pass'), a(24, 'fail'),
    ]),
  };
}

const expected = (eol) => {
  const lines = [...LINES];
  lines[13] = lines[13].replace('- [ ]', '- [x]');
  lines[14] += ' (Win 11, 2026-11-26: fine on a 4K display)';
  lines[16] += ' **Failed (Win 11, 2026-11-26):** menu lags';
  lines[20] = '- [x] A line.';
  lines[23] = '- [ ] Another ticked line, with a capital X,';
  lines[24] += ' **Failed (Win 11, 2026-11-26).**';
  return lines.join(eol) + eol;
};

for (const eol of ['\n', '\r\n']) {
  const name = eol === '\n' ? 'LF' : 'CRLF';

  test(`${name}: pass ticks, fail unticks and appends its note, skip leaves the line; nothing else changes`, () => {
    const text = fixture(eol);
    const { text: out, applied, unmatched } = applyResults(text, answersFor(text));
    assert.equal(out, expected(eol));
    assert.deepEqual(applied, { pass: 2, fail: 2, skip: 1 });
    assert.deepEqual(unmatched, []);
  });

  test(`${name}: applying the same answers again changes nothing`, () => {
    const text = fixture(eol);
    const once = applyResults(text, answersFor(text)).text;
    assert.equal(applyResults(once, answersFor(text)).text, once);
  });

  test(`${name}: --clear unticks every line, and only the tick changes`, () => {
    const text = fixture(eol);
    const { text: out, cleared } = clearTicks(text);
    assert.equal(cleared, 2);
    const lines = [...LINES];
    lines[15] = '- [ ] A ticked line.';
    lines[23] = '- [ ] Another ticked line, with a capital X,';
    assert.equal(out, lines.join(eol) + eol);
    assert.deepEqual(clearTicks(out), { text: out, cleared: 0 });
  });

  test(`${name}: a long note wraps at ${WIDTH} onto indented lines with the file's ending`, () => {
    const text = ['### S', '- [ ] Short line.', '- [ ] Next.'].join(eol) + eol;
    const note = Array.from({ length: 40 }, (_, i) => 'word' + i).join(' ');
    const out = applyResults(text, { system: '', answers: { [keyAt(text, 2)]: { verdict: 'fail', note, date: DATE } } }).text;
    const lines = splitLines(out);
    assert.equal(lines.length, 5);
    assert.ok(lines.every((l) => l.eol === eol));
    assert.ok(lines.every((l) => l.body.length <= WIDTH), 'a line is over the width');
    assert.ok(lines.slice(2, 4).every((l) => l.body.startsWith('  ') && !l.body.startsWith('   ')));
    assert.equal(lines.map((l) => l.body.trim()).join(' ').replace(/\s+/g, ' '), `### S - [ ] Short line. **Failed (${DATE}):** ${note} - [ ] Next.`);
    assert.equal(lines.at(-1).body, '- [ ] Next.');
    assert.equal(parseChecklist(out).items.length, 2, 'the wrapped note made a new item');
  });
}

test('a wrapped note never starts a line with a list, quote or heading marker', () => {
  const text = '### S\n- [ ] Line.\n';
  const note = Array.from({ length: 60 }, (_, i) => ['-', '*', '+', '>', '#', '1.', '[x]'][i % 7] + ' w' + i).join(' ');
  const out = applyResults(text, { answers: { [keyAt(text, 2)]: { verdict: 'fail', note, date: DATE } } }).text;
  const lines = out.split('\n').slice(2, -1);
  assert.ok(lines.length >= 3, 'the note did not wrap');
  for (const l of lines) assert.doesNotMatch(l, /^\s*([-*+>#]|\d+[.)]|\[)/, 'a wrapped line starts a block: ' + l);
  assert.equal(parseChecklist(out).items.length, 1);
});

test('a note wrapped after a last line with no ending keeps the file without a final ending', () => {
  const text = '### S\r\n- [ ] Last.';
  const note = 'x'.repeat(60) + ' ' + 'y'.repeat(60);
  const out = applyResults(text, { system: 'Fedora', answers: { [keyAt(text, 2)]: { verdict: 'pass', note, date: DATE } } }).text;
  assert.equal(out, `### S\r\n- [x] Last. (Fedora, ${DATE}: ${'x'.repeat(60)}\r\n  ${'y'.repeat(60)})`);
});

test('a ticked line inside a code fence is not cleared', () => {
  const text = '```\n- [x] code\n```\n- [x] item\n';
  assert.deepEqual(clearTicks(text), { text: '```\n- [x] code\n```\n- [ ] item\n', cleared: 1 });
});

test('answers whose line changed are left out and reported', () => {
  const text = '### S\n- [ ] One.\n';
  const r = applyResults(text, { answers: { deadbeef0000: { verdict: 'pass', note: '', date: DATE } } });
  assert.equal(r.text, text);
  assert.deepEqual(r.unmatched, ['deadbeef0000']);
});

test('the summary counts each section, lists the failures and suggests a "Last run" sentence', () => {
  const text = fixture('\n');
  const results = { ...answersFor(text), notes: 'Looked fine overall.' };
  const s = summarize(parseChecklist(text).items, results, { date: DATE, commit: 'abc1234' });
  assert.deepEqual(s.bySection, [
    { section: 'First section', pass: 1, fail: 1, skip: 1, none: 0 },
    { section: 'Second section', pass: 1, fail: 1, skip: 0, none: 2 },
  ]);
  assert.deepEqual(s.failed.map((f) => [f.line, f.note]), [[17, 'menu lags'], [24, '']]);
  assert.equal(s.sentence, '**Last run:** 2026-11-26, Win 11, at `abc1234`: 2 of 7 lines passed, 2 failed (First section, Second section), 1 skipped, 2 not run.');
  const out = formatSummary(s);
  assert.match(out, /First section: 1 passed, 1 failed, 1 skipped/);
  assert.match(out, /line 17 \(First section\): A line with `code` and <angle> & "quotes"\.\n {4}note: menu lags/);
  assert.match(out, /Anything else:\n {2}Looked fine overall\./);
});

// The sample app's own checklist, as checked out and with each line ending: every "- [" line at the start of a line
// is an item, and clearing then passing every line changes only the ticks.
for (const eol of ['\n', '\r\n']) {
  test(`the sample README (${eol === '\n' ? 'LF' : 'CRLF'}): every line found, and only ticks change`, () => {
    const raw = readFileSync(path.join(REPO, 'samples', 'Isotope.UI.Samples', 'README.md'), 'utf8');
    const text = raw.replace(/\r?\n/g, eol);
    const { items } = parseChecklist(text);
    assert.equal(items.length, text.split(eol).filter((l) => l.startsWith('- [')).length);
    assert.ok(items.length >= 40, 'too few lines found: ' + items.length);
    const cleared = clearTicks(text).text;
    const answers = Object.fromEntries(items.map((it) => [it.key, { verdict: 'pass', note: '', date: DATE }]));
    const passed = applyResults(cleared, { answers }).text;
    const untick = (s) => s.replace(/^- \[[xX ]\]/gm, '- [ ]');
    assert.equal(untick(cleared), untick(text));
    assert.equal(passed, untick(text).replace(/^- \[ \]/gm, '- [x]'));
  });
}
