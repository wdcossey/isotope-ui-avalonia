#!/usr/bin/env node
// Measures whether the Roslyn step (tools/sync/roslyn, issue #4) could replace the text-based member drops: each
// "drop" rule in tools/sync/transforms.json is re-expressed as a Roslyn drop-member (the member found where the text
// rule matches, named by member and type, with its parameter types only when the name is overloaded), and the whole
// vendored tree is regenerated with those rules in place of the text ones. Prints how many drops convert, and whether
// every generated file is byte-identical to src/Upstream apart from its header line (which names the rules in the
// order they ran, and Roslyn rules run first). Also times both regenerations, and counts where the "find" rules
// (regex find/replace, several of them type retargets) match outside code: in a comment or a string, which a Roslyn
// retarget-type would leave alone. Reads, writes nothing.
// With --history N, also replays both forms of each drop on the last N upstream commits before the baseline that
// changed the files the drops list: a rule "holds" at a commit when it still finds exactly one member there (a proxy
// for how each form survives upstream's edits, measured backwards).
// Usage: node tools/measure-roslyn.mjs [--history N]   (needs the upstream/ cache, full history, and dotnet)
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseLock } from './sync/lib/lock.mjs';
import { loadTierMap } from './sync/lib/tiers.mjs';
import { loadTransforms } from './sync/lib/transforms.mjs';
import { loadAllowlist } from './sync/lib/personal.mjs';
import { generate } from './sync/lib/vendor.mjs';
import { git, readBlobs, revParse } from './sync/lib/git.mjs';
import { codeMask, dropMember } from './sync/lib/csharp.mjs';
import { runRoslyn } from './sync/lib/roslyn.mjs';

const repo = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const here = path.join(repo, 'tools', 'sync');
const json = (p) => JSON.parse(readFileSync(p, 'utf8'));
const upstreamDir = path.join(repo, 'upstream');
const lock = parseLock(readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8'));
const sha = revParse(upstreamDir, lock.baseline);
if (!sha) throw new Error('the commit in UPSTREAM.lock is not in the upstream cache');
const raw = json(path.join(here, 'transforms.json'));
const rules = loadTransforms(raw);
const drops = rules.filter((r) => r.kind === 'drop');
// Conversions are keyed by rule, so each drop must name one file (every drop does: a member lives in one file).
const multi = drops.filter((r) => r.files.length !== 1);
if (multi.length) throw new Error('drops that list several files can\'t be measured: ' + multi.map((r) => r.id).join(', '));
const files = [...new Set(drops.flatMap((r) => r.files))];
const blobs = readBlobs(upstreamDir, sha, files);

// Where each text drop matches in upstream's own text (Roslyn rules read that text).
const located = [];
const notFound = [];
for (const r of drops) for (const f of r.files) {
  const text = blobs.get(f).toString('utf8');
  const code = codeMask(text);
  const m = [...text.matchAll(new RegExp(r.drop, 'g' + r.flags))].filter((x) => code[x.index]);
  if (m.length !== 1) notFound.push(r.id + ' in ' + f + ' (' + m.length + ' matches in upstream\'s text)');
  else located.push({ r, f, at: m[0].index, text });
}
const byFile = Map.groupBy(located, (x) => x.f);
const res = runRoslyn([...byFile].map(([f, xs]) => ({ path: f, text: xs[0].text, ops: xs.map((x) => ({ id: x.r.id, op: 'locate-member', at: x.at })) })));
const converted = new Map();
const failed = [];
let withParameters = 0;
for (const f of res) for (const x of f.results) {
  if (x.error) { failed.push(x.id + ': ' + x.error); continue; }
  const [member, type, parameters, overloads] = x.info.split('|');
  const op = { op: 'drop-member', member, ...(type ? { type } : {}), ...(+overloads > 1 ? { parameters } : {}) };
  if (+overloads > 1) withParameters++;
  converted.set(x.id, op);
}

// The alternative rule set: converted drops first (Roslyn rules run first), then every other rule in its order.
const alt = {
  rules: [
    ...raw.rules.filter((r) => converted.has(r.id)).map(({ drop, flags, ...r }) => ({ ...r, roslyn: converted.get(r.id) })),
    ...raw.rules.filter((r) => !converted.has(r.id)),
  ],
};
const common = {
  upstreamDir, commit: sha, tierMap: loadTierMap(json(path.join(here, 'tiers.json'))),
  repoName: lock.repo.replace(/^[a-z]+:\/\/[^/]+\//i, '').replace(/\.git$/, ''), repoUrl: lock.repo, allow: loadAllowlist(json(path.join(here, 'personal-data.json'))),
};
runRoslyn([{ path: 'warm.cs', text: 'class C {}', ops: [] }]); // builds the tool, so the timings below don't include the build
const time = (fn) => { const t = process.hrtime.bigint(); const v = fn(); return [v, Number(process.hrtime.bigint() - t) / 1e6]; };
const [textGen, textMs] = time(() => generate({ ...common, transforms: rules }));
const [altGen, altMs] = time(() => generate({ ...common, transforms: loadTransforms(alt) }));
const vendored = (p) => readFileSync(path.join(repo, 'src', 'Upstream', ...p.split('/')));
const body = (b) => { const s = b.toString('utf8'); return s.slice(s.indexOf('\n') + 1); };
let same = 0, headerOnly = 0;
const differ = [];
for (const [p, buf] of altGen.files) {
  if (!p.endsWith('.cs')) continue;
  const mine = vendored(p);
  if (buf.equals(mine)) same++;
  else if (body(buf) === body(mine)) headerOnly++;
  else differ.push(p);
}
const textSame = [...textGen.files].every(([p, b]) => p === 'vendored.json' || b.equals(vendored(p)));
console.log('text drops: ' + drops.length + '; located in upstream\'s text: ' + located.length + '; not found there: ' + notFound.length);
for (const n of notFound) console.log('  not found: ' + n);
console.log('converted to Roslyn drop-member: ' + converted.size + ' (' + withParameters + ' need "parameters": the name is overloaded); not convertible: ' + failed.length);
for (const f of failed) console.log('  ' + f);
console.log('regeneration with the converted rules: ' + altGen.errors.length + ' errors');
for (const e of altGen.errors.slice(0, 20)) console.log('  ' + e);
console.log('C# files: ' + same + ' byte-identical, ' + headerOnly + ' identical apart from the header (rule order), ' + differ.length + ' differ' + (differ.length ? ': ' + differ.join(', ') : ''));
console.log('regeneration with the text rules matches src/Upstream: ' + textSame);
console.log('generate() time: text rules ' + textMs.toFixed(0) + ' ms, Roslyn drops ' + altMs.toFixed(0) + ' ms (tool already built)');

// Where the listed find rules match outside code, each on upstream's text (before the other rules).
{
  const finds = rules.filter((r) => r.kind === 'find' && r.files && r.files.every((f) => f.endsWith('.cs')));
  const texts = readBlobs(upstreamDir, sha, [...new Set(finds.flatMap((r) => r.files))]);
  let inCode = 0, outside = 0;
  const where = [];
  for (const r of finds) {
    let o = 0;
    for (const f of r.files) {
      const text = texts.get(f).toString('utf8');
      const code = codeMask(text);
      for (const m of text.matchAll(new RegExp(r.find, 'g' + r.flags))) if (code[m.index]) inCode++; else { outside++; o++; }
    }
    if (o) where.push(r.id + ' ' + o);
  }
  console.log('listed find rules on C# files: ' + finds.length + '; matches in code ' + inCode + ', in comments or strings ' + outside + (where.length ? ' (' + where.join(', ') + ')' : ''));
}

const hi = process.argv.indexOf('--history');
if (hi > 0) {
  const n = +process.argv[hi + 1];
  if (!(n > 0)) throw new Error('--history needs a number of commits');
  const commits = git(upstreamDir, ['log', '--format=%H', '-n', String(n), sha + '~1', '--', ...files]).trim().split('\n').filter(Boolean);
  const tally = { both: 0, textOnly: 0, roslynOnly: 0, neither: 0 };
  const lost = new Map(); // rule id -> commits where only one form held
  for (const c of commits) {
    const present = new Set(git(upstreamDir, ['ls-tree', '-r', '--name-only', c, '--', ...files]).trim().split('\n'));
    const at = readBlobs(upstreamDir, c, files.filter((f) => present.has(f)));
    const ops = [...at].map(([f, b]) => ({ path: f, text: b.toString('utf8'),
      ops: drops.filter((r) => r.files.includes(f) && converted.has(r.id)).map((r) => ({ id: r.id, ...converted.get(r.id) })) }));
    const roslynOk = new Set(runRoslyn(ops).flatMap((f) => f.results.filter((x) => !x.error).map((x) => x.id)));
    for (const r of drops) {
      if (!converted.has(r.id)) continue;
      const t = at.get(r.files[0]);
      const textOk = !!t && !dropMember(t.toString('utf8'), r.re).error;
      const k = textOk && roslynOk.has(r.id) ? 'both' : textOk ? 'textOnly' : roslynOk.has(r.id) ? 'roslynOnly' : 'neither';
      tally[k]++;
      if (k === 'textOnly' || k === 'roslynOnly') lost.set(r.id + ' (' + k + ')', (lost.get(r.id + ' (' + k + ')') ?? 0) + 1);
    }
  }
  console.log('history: ' + commits.length + ' commits before the baseline that changed the files; rule x commit pairs: ' + tally.both + ' both hold, ' +
    tally.roslynOnly + ' only the Roslyn form, ' + tally.textOnly + ' only the text form, ' + tally.neither + ' neither (the member is newer or was renamed)');
  for (const [k, v] of [...lost].sort((a, b) => b[1] - a[1])) console.log('  ' + k + ': ' + v + ' commits');
}
