#!/usr/bin/env node
// Deterministic import rewrite for upstream files (Spike A step 3a; Isotope.UI.Avalonia's upstream transforms).
// Reads the linked upstream files from a props file, applies the rules in a rules file, and writes each file the rules
// change to <outDir>/<relative path>. Writes <outDir>/manifest.txt: one "upstreamPath|rewrittenPath|hits" line per file.
// Usage: node tools/rewrite-upstream.mjs <files.props> <rules.json> <upstreamSrcDir> <outDir>
//
// A rules file is a JSON array. Each rule has a "why", optionally "files" (paths relative to upstreamSrcDir; default:
// every file), and one of:
//   {"find": regex, "replace": string, "flags": "m"}  regex replace ($1..$9 are groups); flags are added to "g".
//   {"drop": regex}  deletes the one member whose declaration the regex matches (in code, not in a comment or string),
//                    with its doc comment and attributes and its body ({...}, => ...; or = ...;). The deleted lines
//                    are left blank, so line numbers still match upstream's.
// Every rule must match: a "find" rule at least once over its files, a "drop" rule exactly once in each of its files.
// Otherwise the import fails (exit 1), so a rule upstream has outgrown can't pass silently.
import { existsSync, readdirSync, readFileSync, realpathSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Marks which characters of C# source are code: not in a comment, string or char literal (interpolation holes count
// as code). Handles //, /* */, '…', "…", @"…", $"…", $@"…"/@$"…" and raw """…""" strings.
export function codeMask(text) {
  const n = text.length;
  const code = new Uint8Array(n);
  let i = 0;
  function charLit() {
    i++;
    while (i < n && text[i] !== "'") { if (text[i] === '\\') i++; i++; }
    i++;
  }
  function str() {
    let dollars = 0, verbatim = false;
    while (text[i] === '$' || text[i] === '@') { if (text[i] === '$') dollars++; else verbatim = true; i++; }
    let q = 0;
    while (text[i + q] === '"') q++;
    if (q >= 3 && !verbatim) {
      i += q;
      const close = '"'.repeat(q);
      while (i < n) {
        if (text.startsWith(close, i)) { i += q; return; }
        if (dollars && text[i] === '{') {
          let k = 0;
          while (text[i + k] === '{') k++;
          i += k;
          if (k >= dollars) { run(true); i += dollars; }
          continue;
        }
        i++;
      }
      return;
    }
    i++;
    while (i < n) {
      const c = text[i];
      if (verbatim) {
        if (c === '"') { if (text[i + 1] === '"') { i += 2; continue; } i++; return; }
      } else {
        if (c === '\\') { i += 2; continue; }
        if (c === '"' || c === '\n') { i++; return; }
      }
      if (dollars && c === '{') {
        if (text[i + 1] === '{') { i += 2; continue; }
        i++;
        run(true);
        i++;
        continue;
      }
      i++;
    }
  }
  // Scans code; in an interpolation hole, returns at its closing brace (not consumed).
  function run(hole) {
    let depth = 0;
    while (i < n) {
      const c = text[i], d = text[i + 1];
      if (c === '/' && d === '/') { while (i < n && text[i] !== '\n') i++; continue; }
      if (c === '/' && d === '*') { const e = text.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue; }
      // A preprocessor line (#region Users' settings, #error "…") holds no code and no literals.
      if (c === '#' && !hole && /^[ \t]*$/.test(text.slice(text.lastIndexOf('\n', i - 1) + 1, i))) {
        while (i < n && text[i] !== '\n') i++;
        continue;
      }
      if (c === "'") { charLit(); continue; }
      if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(i, i + 6)))) { str(); continue; }
      if (hole) {
        if (c === '(' || c === '[' || c === '{') depth++;
        else if (c === ')' || c === ']') depth--;
        else if (c === '}') { if (depth === 0) return; depth--; }
        else if (c === ':' && depth === 0 && d !== ':' && text[i - 1] !== ':') {
          while (i < n && text[i] !== '}') i++; // format clause
          return;
        }
      }
      code[i] = 1;
      i++;
    }
  }
  run(false);
  return code;
}

// The [start, end) character range of the member whose declaration starts at `at`: from its first doc-comment or
// attribute line to the end of the line its body ends on.
function memberRange(text, code, at) {
  const n = text.length;
  const nextCode = (k) => { while (k < n && (!code[k] || /\s/.test(text[k]))) k++; return k; };
  // Index of the ';' ending the construct that starts at k, at bracket depth 0.
  const toSemicolon = (k) => {
    let depth = 0;
    for (; k < n; k++) {
      if (!code[k]) continue;
      const c = text[k];
      if ('([{'.includes(c)) depth++;
      else if (')]}'.includes(c)) depth--;
      else if (c === ';' && depth === 0) return k;
    }
    throw new Error('no end of member');
  };
  const matchBrace = (k) => {
    let depth = 0;
    for (; k < n; k++) {
      if (!code[k]) continue;
      if (text[k] === '{') depth++;
      else if (text[k] === '}' && --depth === 0) return k;
    }
    throw new Error('unbalanced braces');
  };
  let end = -1, paren = 0;
  for (let k = at; k < n && end < 0; k++) {
    if (!code[k]) continue;
    const c = text[k];
    if (c === '(' || c === '[') paren++;
    else if (c === ')' || c === ']') paren--;
    else if (paren === 0 && c === '{') {
      end = matchBrace(k);
      const j = nextCode(end + 1);
      if (text[j] === '=' && text[j + 1] !== '>') end = toSemicolon(j); // property initializer
    } else if (paren === 0 && c === '=') {
      // '==', '!=', '<=', '>=' (an operator declaration's name) are not the start of a body or initializer.
      if (text[k + 1] === '=' || '=!<>'.includes(text[k - 1])) { if (text[k + 1] === '=') k++; continue; }
      end = toSemicolon(k);
    }
    else if (paren === 0 && c === ';') end = k;
  }
  if (end < 0) throw new Error('no end of member');
  let lineEnd = text.indexOf('\n', end);
  lineEnd = lineEnd < 0 ? n : lineEnd;
  for (let k = end + 1; k < lineEnd; k++)
    if (code[k] && !/\s/.test(text[k])) throw new Error('code follows the member on its last line');
  let start = text.lastIndexOf('\n', at - 1) + 1;
  if (text.slice(start, at).trim() !== '') throw new Error('code precedes the member on its first line');
  for (;;) {
    if (start === 0) break;
    const prevStart = text.lastIndexOf('\n', start - 2) + 1;
    const prev = text.slice(prevStart, start).trim();
    if (prev.startsWith('///') || prev.startsWith('[')) start = prevStart;
    else break;
  }
  return [start, lineEnd];
}

// Blanks [start, end) line by line, keeping each line ending so later line numbers don't move.
const blank = (text, start, end) => text.slice(0, start) + text.slice(start, end).replace(/[^\r\n]+/g, '') + text.slice(end);

export function applyRules(rel, text, rules) {
  let hits = 0;
  for (const r of rules) {
    if (r.files && !r.files.includes(rel)) continue;
    if (r.drop !== undefined) {
      const code = codeMask(text);
      const found = [...text.matchAll(r.re)].filter((m) => code[m.index]);
      if (found.length !== 1) throw new Error(rel + ': drop rule matched ' + found.length + ' times, expected 1: ' + r.drop);
      const [s, e] = memberRange(text, code, found[0].index);
      text = blank(text, s, e);
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
  return JSON.parse(readFileSync(rulesFile, 'utf8')).map((r) => ({
    ...r,
    hits: 0,
    re: new RegExp(r.drop ?? r.find, 'g' + (r.flags ?? '')),
  }));
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
  const [propsFile, rulesFile, srcDir, outDir] = process.argv.slice(2);
  const props = readFileSync(propsFile, 'utf8');
  const rules = loadRules(rulesFile);
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
  for (const rel of rels) {
    const src = path.join(srcDir, rel);
    let text, hits;
    try {
      ({ text, hits } = applyRules(rel, readFileSync(src, 'utf8'), rules));
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
