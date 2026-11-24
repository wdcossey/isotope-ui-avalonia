#!/usr/bin/env node
// Deterministic import rewrite for upstream files, at build time (Spike A step 3a). The library no longer uses it: it
// compiles the files tools/sync vendors, transformed by tools/sync/transforms.json.
// Reads the linked upstream files from a props file, applies the rules in a rules file, and writes each file the rules
// change to <outDir>/<relative path>. Writes <outDir>/manifest.txt: one "upstreamPath|rewrittenPath|hits" line per file.
// Usage: node tools/rewrite-upstream.mjs <files.props> <rules.json> <upstreamSrcDir> <outDir> [<more rules.json> ...]
//
// A rules file is a JSON array. Each rule has a "why", optionally "files" (paths relative to upstreamSrcDir; default:
// every file), and one of:
//   {"find": regex, "replace": string, "flags": "m"}  regex replace ($1..$9 are groups); flags are added to "g".
//   {"drop": regex}  deletes the one member whose declaration the regex matches (in code, not in a comment or string),
//                    with its doc comment and attributes and its body ({...}, => ...; or = ...;). The deleted lines
//                    are left blank, so line numbers still match upstream's.
//   {"roslyn": {"op", ...}, "files": [...]}  a syntax-aware transform by tools/sync/roslyn (ops as in
//                    tools/sync/lib/transforms.mjs). Roslyn rules run first, on upstream's text, in one batch.
// Every rule must match: a "find" rule at least once over its files, a "drop" rule exactly once in each of its files.
// Otherwise the import fails (exit 1), so a rule upstream has outgrown can't pass silently.
import { existsSync, readdirSync, readFileSync, realpathSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { dropMember } from './sync/lib/csharp.mjs';
import { runRoslyn } from './sync/lib/roslyn.mjs';

// The C# lexer lives in tools/sync/lib/csharp.mjs (one implementation, shared with tools/sync's drop rules).
export { codeMask } from './sync/lib/csharp.mjs';

export function applyRules(rel, text, rules) {
  let hits = 0;
  for (const r of rules) {
    if (r.files && !r.files.includes(rel)) continue;
    if (r.roslyn !== undefined) continue; // applied before, in one batch (applyRoslynRules)
    if (r.drop !== undefined) {
      const d = dropMember(text, r.re);
      if (d.error) throw new Error(rel + ': drop rule ' + d.error + ': ' + r.drop);
      text = d.text;
      r.hits++;
      hits++;
      continue;
    }
    text = text.replace(r.re, (...args) => {
      hits++;
      r.hits++;
      const named = typeof args.at(-1) === 'object';
      const groups = args.slice(1, named ? -3 : -2); // drop the match, offset, input (and named groups)
      return r.replace.replace(/\$(\d)/g, (_, i) => groups[+i - 1] ?? '');
    });
  }
  return { text, hits };
}

export function loadRules(rulesFile) {
  return JSON.parse(readFileSync(rulesFile, 'utf8')).map((r) => {
    if (r.roslyn === undefined) return { ...r, hits: 0, re: new RegExp(r.drop ?? r.find, 'g' + (r.flags ?? '')) };
    if (!Array.isArray(r.files) || !r.files.length) throw new Error('a "roslyn" rule lists its files: ' + JSON.stringify(r.roslyn));
    return { ...r, hits: 0 };
  });
}

/**
 * Runs the Roslyn rules over the files they list, in one run of the tool. texts: Map(rel -> upstream text).
 * Returns Map(rel -> {text, hits}) for the files they change; throws on a rule that fails or matches nothing.
 */
export function applyRoslynRules(texts, rules, run = runRoslyn) {
  const roslyn = rules.filter((r) => r.roslyn !== undefined);
  const batch = [...texts].map(([rel, text]) => ({ path: rel, text, ops: roslyn.filter((r) => r.files.includes(rel)).map((r) => ({ id: String(rules.indexOf(r)), ...r.roslyn })) }))
    .filter((f) => f.ops.length);
  const out = new Map();
  const problems = [];
  for (const f of run(batch)) {
    let hits = 0;
    for (const x of f.results) {
      const r = rules[+x.id];
      if (x.error) problems.push(f.path + ': roslyn rule ' + JSON.stringify(r.roslyn) + ': ' + x.error);
      r.hits += x.hits ? 1 : 0;
      hits += x.hits;
    }
    problems.push(...f.errors.map((e) => f.path + ': ' + e));
    out.set(f.path, { text: f.text, hits });
  }
  for (const r of roslyn) for (const f of r.files) if (!texts.has(f)) problems.push('roslyn rule ' + JSON.stringify(r.roslyn) + ' lists ' + f + ', which the props file does not compile');
  if (problems.length) throw new Error(problems.join('\n'));
  return out;
}

// Writes a file only when its content changed, so an unchanged rewrite keeps its timestamp and the compile
// that reads it stays up to date.
function writeIfChanged(file, content) {
  if (existsSync(file) && readFileSync(file, 'utf8') === content) return;
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, content);
}

// Every file under dir, as absolute paths.
function listFiles(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir, { withFileTypes: true, recursive: true })
    .filter((e) => e.isFile())
    .map((e) => path.resolve(e.parentPath ?? e.path, e.name));
}

function main() {
  const [propsFile, rulesFile, srcDir, outDir, ...moreRules] = process.argv.slice(2);
  const props = readFileSync(propsFile, 'utf8');
  const rules = [rulesFile, ...moreRules].flatMap(loadRules);
  const rels = [...props.matchAll(/Include="\$\(UpstreamSrc\)([^"]+)"/g)].map((m) => m[1].replace(/\\/g, '/'));
  const manifest = [];
  const written = new Set();
  const manifestFile = path.resolve(outDir, 'manifest.txt');
  // A failed import leaves no manifest, so a build can't go on with the previous run's rewritten files.
  const fail = (message) => {
    rmSync(manifestFile, { force: true });
    console.error(message);
    process.exit(1);
  };
  // Roslyn rules first, on upstream's text, in one run of the tool.
  let pre = new Map();
  if (rules.some((r) => r.roslyn !== undefined)) {
    try {
      pre = applyRoslynRules(new Map(rels.map((rel) => [rel, readFileSync(path.join(srcDir, rel), 'utf8')])), rules);
    } catch (e) {
      fail('rewrite-upstream: ' + e.message);
    }
  }
  for (const rel of rels) {
    const src = path.join(srcDir, rel);
    let text, hits;
    try {
      const before = pre.get(rel);
      ({ text, hits } = applyRules(rel, before?.text ?? readFileSync(src, 'utf8'), rules));
      hits += before?.hits ?? 0;
    } catch (e) {
      fail('rewrite-upstream: ' + e.message);
    }
    if (!hits) continue;
    const dst = path.resolve(outDir, rel);
    // #line maps compiler errors back to the upstream file and line, so rewritten and verbatim files report alike.
    writeIfChanged(dst, '// REWRITTEN from upstream src/Reactor/' + rel + ' by tools/rewrite-upstream.mjs. Do not edit.\n#line 1 "' + path.resolve(src) + '"\n' + text);
    written.add(dst);
    manifest.push([path.resolve(src), dst, hits].join('|'));
  }
  const unmatched = rules.filter((r) => r.hits === 0 || (r.drop !== undefined && r.files && r.hits !== r.files.length));
  if (unmatched.length)
    fail(unmatched.map((r) => 'rewrite-upstream: rule did not match: ' + (r.drop ?? r.find)).join('\n'));
  for (const stale of listFiles(outDir)) if (!written.has(stale) && stale !== manifestFile) rmSync(stale);
  writeIfChanged(manifestFile, manifest.join('\n') + (manifest.length ? '\n' : ''));
  console.log('rewrote ' + manifest.length + ' files');
}

// Run as a script (compared as real paths: Node resolves the main module through junctions and symlinks).
if (process.argv[1] && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url))) main();
