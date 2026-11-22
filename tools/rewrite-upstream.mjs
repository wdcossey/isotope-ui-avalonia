#!/usr/bin/env node
// Mechanical import rewrite for upstream files (Spike A step 3a).
// Reads the linked upstream files from an EngineFiles.props, applies the deterministic rules in a rules
// file (JSON: [{"find": regex, "replace": string, "why": string}]), and writes each file the rules change
// to <outDir>/<relative path>. Writes <outDir>/manifest.txt: one "upstreamPath|rewrittenPath|hits" line per file.
// Usage: node tools/rewrite-upstream.mjs <EngineFiles.props> <rules.json> <upstreamSrcDir> <outDir>
import { readFileSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';

const [propsFile, rulesFile, srcDir, outDir] = process.argv.slice(2);
const props = readFileSync(propsFile, 'utf8');
const rules = JSON.parse(readFileSync(rulesFile, 'utf8')).map((r) => ({ ...r, re: new RegExp(r.find, 'g') }));
const rels = [...props.matchAll(/Include="\$\(UpstreamSrc\)([^"]+)"/g)].map((m) => m[1].replace(/\\/g, '/'));
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });
const manifest = [];
for (const rel of rels) {
  const src = path.join(srcDir, rel);
  let text = readFileSync(src, 'utf8');
  let hits = 0;
  for (const r of rules) text = text.replace(r.re, (...args) => {
    hits++;
    const named = typeof args.at(-1) === 'object';
    const groups = args.slice(1, named ? -3 : -2); // drop the match, offset, input (and named groups)
    return r.replace.replace(/\$(\d)/g, (_, i) => groups[+i - 1] ?? '');
  });
  if (!hits) continue;
  const dst = path.join(outDir, rel);
  mkdirSync(path.dirname(dst), { recursive: true });
  // #line maps compiler errors back to the upstream file and line, so rewritten and verbatim files report alike.
  writeFileSync(dst, '// REWRITTEN from upstream src/Reactor/' + rel + ' by tools/rewrite-upstream.mjs. Do not edit.\n#line 1 "' + path.resolve(src) + '"\n' + text);
  manifest.push([path.resolve(src), path.resolve(dst), hits].join('|'));
}
writeFileSync(path.join(outDir, 'manifest.txt'), manifest.join('\n') + (manifest.length ? '\n' : ''));
console.log('rewrote ' + manifest.length + ' files');
