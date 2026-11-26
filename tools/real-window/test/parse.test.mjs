// Tests of the checklist parser (#106): node --test tools/real-window/test/*.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { joinLines, parseChecklist, sections, splitLines } from '../lib/parse.mjs';
import { fixture } from './fixture.mjs';

for (const eol of ['\n', '\r\n']) {
  const name = eol === '\n' ? 'LF' : 'CRLF';

  test(`${name}: items, their sections, ticks and multi-line text`, () => {
    const { items } = parseChecklist(fixture(eol));
    assert.deepEqual(items.map((it) => [it.line, it.section, it.ticked, it.text]), [
      [14, 'First section', false, 'Click **Go** → its menu drops down, listing the pages with a radio mark on Basics, and none cut off.'],
      [16, 'First section', true, 'A ticked line.'],
      [17, 'First section', false, 'A line with `code` and <angle> & "quotes".'],
      [21, 'Second section', false, 'A line.'],
      [22, 'Second section', false, 'A line.'],
      [23, 'Second section', false, 'A nested line.'],
      [24, 'Second section', true, 'Another ticked line, with a capital X, over two lines.'],
    ]);
    assert.deepEqual(sections(items).map((s) => [s.name, s.items.length]), [['First section', 3], ['Second section', 4]]);
    assert.ok(items.every((it) => !it.text.includes('\r')), 'a CR is left in an item');
  });

  test(`${name}: lines keep their endings, and join back byte for byte`, () => {
    for (const finalEol of [true, false]) {
      const text = fixture(eol, { finalEol });
      const lines = splitLines(text);
      assert.equal(joinLines(lines), text);
      assert.ok(lines.slice(0, -1).every((l) => l.eol === eol));
      assert.equal(lines.at(-1).eol, finalEol ? eol : '');
    }
  });
}

test('the same line twice in a section gets two keys; the same line in another section another key', () => {
  const { items } = parseChecklist('### A\n- [ ] Same.\n- [ ] Same.\n### B\n- [ ] Same.\n');
  const keys = items.map((it) => it.key);
  assert.equal(new Set(keys).size, 3);
  assert.match(keys[1], /^[0-9a-f]{12}-2$/);
  assert.equal(keys[0], keys[1].slice(0, 12));
});

test('a key ignores the tick and the line endings, so a resumed run finds its answers', () => {
  const a = parseChecklist('### S\n- [ ] Some line\n  over two.\n').items[0];
  const b = parseChecklist('### S\r\n- [x] Some line\r\n  over two.\r\n').items[0];
  assert.equal(a.key, b.key);
});

test('a mixed-ending file keeps each line\'s own ending', () => {
  const text = 'a\r\nb\nc\r\n';
  assert.deepEqual(splitLines(text).map((l) => l.eol), ['\r\n', '\n', '\r\n']);
  assert.equal(joinLines(splitLines(text)), text);
});
