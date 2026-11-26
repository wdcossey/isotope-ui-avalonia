// The in-process fixes against dotnet format (#87): node --test tools/migration/test/fixer-vs-dotnet-format.mjs.
// Needs dotnet, so it isn't one of the *.test.mjs files CI runs without it; CI's samples job runs it before measuring.
// measure-samples.mjs migrates the fixture (test/fixture: a library and an app that uses it, with files the fixes must
// leave alone) once with MigrationReport's in-process fixes and once with dotnet format, and the two must give the same
// files and the same summary. Up to the order of using directives and the number of passes: dotnet format fixes the
// IDs in a hash set's order, which changes from run to run (two of its runs on upstream's samples put the usings of 8
// files in another order and took another number of passes on 3 projects), and the in-process fixes in ordinal order.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const fixture = path.join(here, 'fixture');
const script = path.join(here, '..', 'measure-samples.mjs');

function measure(out, ...flags) {
  const r = spawnSync(process.execPath, [script, '--samples', fixture, '--out', out, '--jobs', '2', ...flags], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  assert.equal(r.status, 0, r.stdout + r.stderr);
  return JSON.parse(fs.readFileSync(path.join(out, 'summary.json'), 'utf8'));
}

// Each file under the migrated samples (not bin/ or obj/): its path and the hash of its bytes, with each run of using
// directives in sorted order when sortUsings.
function tree(dir, sortUsings, base = dir, into = {}) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory() && /^(bin|obj)$/.test(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) tree(p, sortUsings, base, into);
    else {
      let bytes = fs.readFileSync(p);
      if (sortUsings && p.endsWith('.cs')) bytes = Buffer.from(withUsingsSorted(bytes.toString('utf8')));
      into[path.relative(base, p).replace(/\\/g, '/')] = createHash('sha256').update(bytes).digest('hex');
    }
  }
  return into;
}

function withUsingsSorted(text) {
  const lines = text.split('\n');
  for (let i = 0; i < lines.length;) {
    let j = i;
    while (j < lines.length && /^\s*(global\s+)?using\s[^(]*;\r?$/.test(lines[j])) j++;
    if (j > i) lines.splice(i, j - i, ...lines.slice(i, j).sort());
    i = j + 1;
  }
  return lines.join('\n');
}

// A summary's results without the number of passes each project took.
const withoutPasses = results => results.map(({ formatRuns, ...r }) => r);

test('the in-process fixes give the files and the summary dotnet format gives', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'fixer-vs-format-'));
  try {
    const inProcess = measure(path.join(tmp, 'in-process'));
    const format = measure(path.join(tmp, 'dotnet-format'), '--fixer', 'dotnet-format');
    assert.deepEqual(tree(path.join(tmp, 'in-process', 'samples'), true), tree(path.join(tmp, 'dotnet-format', 'samples'), true));
    assert.deepEqual(withoutPasses(inProcess.results), withoutPasses(format.results));
    assert.deepEqual(inProcess.summary, format.summary);

    // The fixture exercises what it should: both projects fixed, the app over several passes, with no failure, and the
    // generated files left as they were.
    const files = tree(path.join(tmp, 'in-process', 'samples'), false);
    const source = tree(fixture, false);
    for (const f of ['App/Program.cs', 'Lib/Widgets.cs']) assert.notEqual(files[f], source[f], f + ' was not fixed');
    for (const f of ['App/Strings.Designer.cs', 'App/Generated.cs', 'App/Skipped.cs', 'App/Quiet.cs']) assert.equal(files[f], source[f], f + ' was fixed');
    const app = inProcess.results.find(r => r.project === 'App/App.csproj');
    assert.ok(app.formatRuns >= 3, 'the app took ' + app.formatRuns + ' passes');
    assert.equal(inProcess.summary.formatFailures, 0);
    // Renames the namespace fix had to come first for (ISOMIG006, ISOMIG009: members and factories of Isotope's types).
    const program = fs.readFileSync(path.join(tmp, 'in-process', 'samples', 'App', 'Program.cs'), 'utf8');
    for (const s of ['using Isotope.UI;', 'Minimum = 1', 'with { Text = "again" }', '(ScrollViewer((ScrollViewer(']) assert.ok(program.includes(s), s);
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
});
