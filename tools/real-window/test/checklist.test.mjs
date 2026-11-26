// The command end to end (#106): node --test tools/real-window/test/*.test.mjs. It runs checklist.mjs on a copy of
// a checklist in a temporary folder, answers over HTTP as the page does, stops and resumes, and finishes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseChecklist } from '../lib/parse.mjs';
import { post, request, tokenOf } from './http.mjs';
import { fixture } from './fixture.mjs';

const TOOL = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'checklist.mjs');

// Starts the tool; resolves once it prints its ready line, with the port, the JSON events so far and a way to wait
// for its exit.
function start(args) {
  const child = spawn(process.execPath, [TOOL, ...args], { stdio: ['ignore', 'pipe', 'pipe'] });
  const events = [];
  let out = '', err = '';
  const exited = new Promise((resolve) => child.on('exit', (code) => resolve(code)));
  child.stderr.on('data', (d) => { err += d; });
  return new Promise((resolve, reject) => {
    child.stdout.on('data', (d) => {
      out += d;
      let i;
      while ((i = out.indexOf('\n')) >= 0) {
        const line = out.slice(0, i); out = out.slice(i + 1);
        const e = JSON.parse(line);
        events.push(e);
        if (e.event === 'ready') resolve({ child, port: Number(new URL(e.url).port), ready: e, events, exited, stderr: () => err });
      }
    });
    exited.then((code) => reject(new Error(`the tool exited (${code}) before it was ready: ${err}`)));
  });
}

test('serve, answer, stop, resume, finish: the checklist is written back and the results file goes', { timeout: 30000 }, async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'checklist-test-'));
  try {
    const file = path.join(dir, 'README.md');
    const resultsFile = file + '.results.json';
    const original = fixture('\r\n');
    writeFileSync(file, original);
    const items = parseChecklist(original).items;

    // First run, with --clear: two answers, then stopped.
    let run = await start([file, '--port', '0', '--clear']);
    assert.equal(run.ready.lines, items.length);
    assert.equal(run.ready.saved, 0);
    assert.equal(readFileSync(file, 'utf8'), original.replace('- [x] A ticked', '- [ ] A ticked').replace('- [X] Another', '- [ ] Another'));
    let token = tokenOf((await request(run.port)).text);
    assert.equal((await post(run.port, '/system', { system: 'Test OS' }, token)).status, 200);
    assert.equal((await post(run.port, '/answer', { key: items[0].key, verdict: 'pass' }, token)).status, 200);
    assert.equal((await post(run.port, '/answer', { key: items[1].key, verdict: 'fail', note: 'it broke' }, token)).status, 200);
    const saved = JSON.parse(readFileSync(resultsFile, 'utf8'));
    assert.equal(saved.checklist, 'README.md');
    assert.equal(Object.keys(saved.answers).length, 2);
    assert.ok(!JSON.stringify(saved).includes(dir), 'the results file holds a path');
    assert.deepEqual(run.events.filter((e) => e.event === 'answer').map((e) => [e.line, e.verdict, e.note]), [[14, 'pass', ''], [16, 'fail', 'it broke']]);
    run.child.kill();
    await run.exited;

    // Second run resumes: the old token is refused, the saved answers are on the page; a skip, then Finish.
    run = await start([file, '--port', '0']);
    assert.equal(run.ready.saved, 2);
    assert.equal((await post(run.port, '/answer', { key: items[2].key, verdict: 'skip' }, token)).status, 403);
    const page = (await request(run.port)).text;
    token = tokenOf(page);
    assert.match(page, /value="it broke"/);
    assert.match(page, /value="Test OS"/);
    assert.equal((await post(run.port, '/answer', { key: items[2].key, verdict: 'skip' }, token)).status, 200);
    const fin = await post(run.port, '/finish', {}, token);
    assert.equal(fin.status, 200);
    assert.match(JSON.parse(fin.text).summary, /1 of 7 lines passed, 1 failed \(First section\), 1 skipped, 4 not run\./);
    assert.equal(await run.exited, 0);

    const written = readFileSync(file, 'utf8');
    const date = /\*\*Failed \(Test OS, (\d{4}-\d{2}-\d{2})\):\*\*/.exec(written)?.[1];
    assert.ok(date, 'no failure note written');
    const expected = clearedFixture().replace('- [ ] Click **Go**', '- [x] Click **Go**')
      .replace('- [ ] A ticked line.', `- [ ] A ticked line. **Failed (Test OS, ${date}):** it broke`);
    assert.equal(written, expected);
    assert.ok(!written.replace(/\r\n/g, '').includes('\n'), 'a bare LF was written');
    assert.equal(existsSync(resultsFile), false, 'the results file is left');
    assert.equal(run.events.at(-1).event, 'finish');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

const clearedFixture = () => fixture('\r\n').replace('- [x] A ticked', '- [ ] A ticked').replace('- [X] Another', '- [ ] Another');

test('a file with no checklist lines, or a results file that is not one, stops with a message', { timeout: 30000 }, async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'checklist-test-'));
  try {
    const empty = path.join(dir, 'empty.md');
    writeFileSync(empty, '# Nothing here\n');
    await assert.rejects(start([empty, '--port', '0']), /exited \(1\).*no "- \[ \]" lines/s);
    const file = path.join(dir, 'c.md');
    writeFileSync(file, '- [ ] One\n');
    writeFileSync(file + '.results.json', '{"something": "else"}');
    await assert.rejects(start([file, '--port', '0']), /exited \(1\).*not a results file/s);
    assert.equal(readFileSync(file + '.results.json', 'utf8'), '{"something": "else"}');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
