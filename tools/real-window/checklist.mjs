#!/usr/bin/env node
// A local page for a real-window checklist run (#106): node tools/real-window/checklist.mjs <checklist.md> [--port N]
// [--clear]. It serves the checklist's `- [ ]` lines at http://127.0.0.1:<port>/ (default 8765) with Pass, Fail and
// Skip per line, saves each answer to <checklist>.results.json as it is given (a later run resumes from it), and on
// Finish writes the answers into the checklist, prints a summary, and deletes the results file. --clear unticks every
// line first. Each answer is printed on stdout as one JSON line, for an agent following the run; messages for the
// person at the terminal go to stderr. It writes only the checklist and its results file.
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { parseChecklist, sections } from './lib/parse.mjs';
import { deleteResults, loadResults, localDate, resultsPathFor, saveResults } from './lib/results.mjs';
import { createChecklistServer, newToken } from './lib/server.mjs';
import { applyResults, clearTicks, formatSummary, summarize } from './lib/writeback.mjs';

const USAGE = 'usage: node tools/real-window/checklist.mjs <checklist.md> [--port N] [--clear]';

function parseArgs(argv) {
  const opts = { port: 8765, clear: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--port' && /^\d+$/.test(argv[i + 1] ?? '') && Number(argv[i + 1]) <= 65535) opts.port = Number(argv[++i]);
    else if (a === '--clear') opts.clear = true;
    else if (a === '--help' || a === '-h') { console.log(USAGE); process.exit(0); }
    else if (!a.startsWith('-') && !opts.file) opts.file = path.resolve(a);
    else { console.error(USAGE); process.exit(2); }
  }
  if (!opts.file) { console.error(USAGE); process.exit(2); }
  return opts;
}

const say = (s) => process.stderr.write(s + '\n');
const emit = (event) => process.stdout.write(JSON.stringify(event) + '\n');

// The short commit the checklist's folder is at, for the suggested "Last run" sentence; null outside a git checkout.
function shortCommit(dir) {
  const r = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd: dir, encoding: 'utf8', shell: false });
  return r.status === 0 ? r.stdout.trim() : null;
}

const opts = parseArgs(process.argv.slice(2));
const name = path.basename(opts.file);
const resultsFile = resultsPathFor(opts.file);
const read = () => readFileSync(opts.file, 'utf8');

try {
  if (opts.clear) {
    const { text, cleared } = clearTicks(read());
    if (cleared) writeFileSync(opts.file, text);
    say(`Cleared ${cleared} ticks in ${name}.`);
  }
  const { items } = parseChecklist(read());
  if (!items.length) throw new Error(`${name} has no "- [ ]" lines`);
  const results = loadResults(resultsFile, opts.file);
  const keys = new Set(items.map((it) => it.key));
  const saved = Object.keys(results.answers).filter((k) => keys.has(k)).length;
  const stale = Object.keys(results.answers).length - saved;

  const { server, listen } = createChecklistServer({
    title: `Real-window checklist: ${name}`,
    getItems: () => parseChecklist(read()).items,
    results,
    token: newToken(),
    save: () => saveResults(resultsFile, results),
    log: emit,
    finish: () => {
      const before = read();
      const { items: now } = parseChecklist(before);
      const { text, unmatched } = applyResults(before, results);
      if (text !== before) writeFileSync(opts.file, text);
      const summary = summarize(now, results, { date: localDate(), commit: shortCommit(path.dirname(opts.file)) });
      deleteResults(resultsFile);
      let out = formatSummary(summary);
      if (unmatched.length) out += `\n${unmatched.length} saved answers matched no line (the line's text changed) and were left out.`;
      emit({ event: 'finish', at: new Date().toISOString(), total: summary.total, bySection: summary.bySection, failed: summary.failed, sentence: summary.sentence, unmatched: unmatched.length });
      say(`Wrote the answers into ${name}.\n${out}`);
      return out;
    },
  });
  const port = await listen(opts.port);
  const url = `http://127.0.0.1:${port}/`;
  emit({ event: 'ready', url, lines: items.length, sections: sections(items).length, saved });
  say(`${name}: ${items.length} lines in ${sections(items).length} sections${saved ? `, ${saved} answered in an earlier run` : ''}.`);
  if (stale) say(`${stale} saved answers match no line now (the line's text changed); they will be left out.`);
  say(`Open ${url} and answer each line. Answers are saved to ${path.basename(resultsFile)} as you go; Finish writes them into ${name}.`);
  server.on('finished', () => { server.close(() => process.exit(0)); server.closeAllConnections(); });
  process.on('SIGINT', () => {
    say(`\nStopped without writing ${name}. Answers are kept in ${path.basename(resultsFile)}; run the same command to resume.`);
    process.exit(130);
  });
} catch (e) {
  console.error('checklist: ' + e.message);
  process.exit(1);
}
