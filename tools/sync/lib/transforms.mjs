// Deterministic text transforms for vendored files. Each rule is scoped one of two ways:
//   "files": [paths]  tier 2 files it must change; a listed file it no longer matches fails the sync loudly.
//   "glob": pattern   every vendored file (tier 1 or 2) the glob matches, wherever the rule matches; it must change at
//                     least one file. For repo-wide renames (the namespace), so tier 1 means "no rule but those".
//                     Glob rules come after every listed rule (see loadTransforms).
// and does one of three things:
//   "find" (JS regex source, "flags" optional: any of "imsu", added to "g") with "replace" ($1..$9 for groups, $$ for $);
//   "drop" (JS regex source): deletes the one member whose declaration it matches in code (not in a comment or
//          string), with its doc comment and attributes and its body; the lines are left blank, so line numbers still
//          match upstream's. Exactly one match in each listed file. Only with "files".
//   "roslyn" ({"op", ...}): a syntax-aware transform run by tools/sync/roslyn (lib/roslyn.mjs). Only with "files", and
//          before every other rule, so it reads upstream's own text. Ops:
//            {"op": "drop-member", "member", "type"?, "parameters"?}  drops the one member named member in the type
//              (dotted, innermost last; omitted for a top-level type), "parameters" ("(T1, ref T2)", the types as
//              written, with their modifiers) picking an overload; blanked like "drop".
//            {"op": "retarget-type", "from", "to"}  rewrites every reference to the type name from (qualified, A.B,
//              with or without global::) in code to the text to; comments and strings are left alone. Like a find
//              rule, it fails on a C# file it doesn't list whose code uses the type.
//            {"op": "drop-attribute", "attribute"}  removes every attribute of that name (with or without the
//              Attribute suffix; dotted to match only that qualified form).
//          Each must match in every listed file, and a drop-member exactly once; the files it changes must still
//          parse with no new syntax error; a target that also appears in code an #if leaves out fails the rule.
// Fix a rule that stops matching, never the vendored file.
// rules: {"rules": [{"id", "find"+"replace" | "drop" | "roslyn", "flags"?, "why", "files" | "glob"}]}
import { globToRegExp } from './glob.mjs';
import { codeMask, dropMember } from './csharp.mjs';
import { OPS, runRoslyn } from './roslyn.mjs';

export function loadTransforms(json) {
  if (!json || !Array.isArray(json.rules)) throw new Error('transforms: expected {"rules": [...]}');
  const ids = new Set();
  const rules = json.rules.map((r, i) => {
    const where = 'transform ' + (r.id ?? '#' + (i + 1));
    if (typeof r.id !== 'string' || !r.id) throw new Error('transform #' + (i + 1) + ': needs an "id"');
    if (ids.has(r.id)) throw new Error(where + ': duplicate id');
    ids.add(r.id);
    if (r.roslyn !== undefined) return loadRoslynRule(r, where);
    const drop = r.drop !== undefined;
    if (drop === (r.find !== undefined)) throw new Error(where + ': needs exactly one of "find" or "drop" (or "roslyn")');
    const source = drop ? r.drop : r.find;
    if (typeof source !== 'string' || !source) throw new Error(where + ': "' + (drop ? 'drop' : 'find') + '" must be a non-empty regex string');
    if (drop && r.replace !== undefined) throw new Error(where + ': a "drop" rule takes no "replace"');
    if (!drop && typeof r.replace !== 'string') throw new Error(where + ': "replace" must be a string');
    const flags = r.flags ?? '';
    if (typeof flags !== 'string' || !/^[imsu]*$/.test(flags) || new Set(flags).size !== flags.length)
      throw new Error(where + ': "flags" may hold i, m, s and u, each once');
    if ((r.files === undefined) === (r.glob === undefined)) throw new Error(where + ': needs exactly one of "files" or "glob"');
    if (r.files !== undefined && (!Array.isArray(r.files) || !r.files.length)) throw new Error(where + ': "files" must list the files it changes');
    if (r.glob !== undefined && (typeof r.glob !== 'string' || !r.glob)) throw new Error(where + ': "glob" must be a non-empty pattern');
    if (drop && r.glob !== undefined) throw new Error(where + ': a "drop" rule lists its files (a member lives in one file)');
    let re;
    try { re = new RegExp(source, 'g' + flags); } catch (e) { throw new Error(where + ': bad regex: ' + e.message); }
    if (!drop) {
      const groups = new RegExp(source + '|', flags).exec('').length - 1;
      for (const m of r.replace.matchAll(/\$(\$|\d+)/g)) if (m[1] !== '$' && (+m[1] < 1 || +m[1] > groups || m[1].length > 1))
        throw new Error(where + ': "replace" uses $' + m[1] + ', but "find" has ' + groups + ' groups (use $1..$' + Math.min(groups, 9) + ', or $$ for a literal $)');
    }
    let globRe = null;
    if (r.glob !== undefined) {
      try { globRe = globToRegExp(r.glob); } catch (e) { throw new Error(where + ': ' + e.message); }
    }
    return { id: r.id, kind: drop ? 'drop' : 'find', find: r.find, drop: r.drop, replace: r.replace, flags, why: r.why ?? '',
      files: r.files ?? null, glob: r.glob ?? null, globRe, re };
  });
  // The unlisted-file guard (transformFile) sees the text as transformed so far, so a glob rule placed before a listed
  // rule could convert new upstream code first and hide it from the guard. Glob rules therefore run last.
  // Roslyn rules run first, on upstream's own text, in one batch per sync (applyRoslyn); listing them first keeps the
  // file's order the order they run in.
  const firstText = rules.findIndex((r) => r.kind !== 'roslyn');
  const late = firstText < 0 ? null : rules.find((r, i) => r.kind === 'roslyn' && i > firstText);
  if (late) throw new Error('transform ' + late.id + ': "roslyn" rules come before every other rule (' + rules[firstText].id + ' precedes it)');
  const lastListed = rules.findLastIndex((r) => r.files);
  const early = rules.find((r, i) => r.glob && i < lastListed);
  if (early) throw new Error('transform ' + early.id + ': glob rules come after every listed rule (' + rules[lastListed].id + ' follows it)');
  return rules;
}

function loadRoslynRule(r, where) {
  if (r.find !== undefined || r.drop !== undefined || r.replace !== undefined || r.flags !== undefined)
    throw new Error(where + ': needs exactly one of "find" or "drop" (or "roslyn")');
  if (r.glob !== undefined || !Array.isArray(r.files) || !r.files.length) throw new Error(where + ': a "roslyn" rule lists the files it changes');
  checkRoslynOp(r.roslyn, where);
  return { id: r.id, kind: 'roslyn', roslyn: { ...r.roslyn }, why: r.why ?? '', files: r.files, glob: null, globRe: null,
    guard: r.roslyn.op === 'retarget-type' ? dotted(r.roslyn.from) : null };
}

/** Throws unless o is a known Roslyn op with the fields it takes (shared with tools/rewrite-upstream.mjs). */
export function checkRoslynOp(o, where) {
  if (!o || typeof o !== 'object' || Array.isArray(o)) throw new Error(where + ': "roslyn" must be an object {"op", ...}');
  const spec = OPS[o.op];
  if (!spec) throw new Error(where + ': "roslyn" op must be one of ' + Object.keys(OPS).join(', ') + ', not ' + JSON.stringify(o.op));
  for (const k of Object.keys(o)) if (k !== 'op' && !spec.required.includes(k) && !spec.optional.includes(k))
    throw new Error(where + ': ' + o.op + ' takes no "' + k + '"');
  for (const k of [...spec.required, ...spec.optional]) {
    if (o[k] === undefined && spec.optional.includes(k)) continue;
    if (typeof o[k] !== 'string' || (!o[k].trim() && k !== 'to')) throw new Error(where + ': ' + o.op + ' needs "' + k + '" (a non-empty string)');
  }
  // Without a compilation, a bare name can't be told from a local, a property or a method of the same name.
  if (o.op === 'retarget-type' && !o.from.replace(/^\s*global\s*::/, '').includes('.'))
    throw new Error(where + ': retarget-type needs a qualified "from" (A.B), not ' + JSON.stringify(o.from));
}

// The regex for a retarget's "from" as code writes it (optional global::, any spacing around the dots), for the guard.
const dotted = (from) => new RegExp('(?<![\\w.@])(?:global\\s*::\\s*)?' +
  from.replace(/^\s*global\s*::\s*/, '').split('.').map((s) => s.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('\\s*\\.\\s*') + '(?!\\w)', 'g');

/**
 * Runs the Roslyn rules on the files they list, in one batch: items [{p, tier, text}] (each file's text as read from
 * upstream). Returns Map(p -> {text, hits, applied, errors}) for the files that have Roslyn rules. run: the runner
 * (lib/roslyn.mjs), replaceable in tests.
 */
export function applyRoslyn(rules, items, run = runRoslyn) {
  const batch = [];
  for (const it of items) {
    const mine = rulesFor(rules, it.p, it.tier).filter((r) => r.kind === 'roslyn');
    if (mine.length) batch.push({ it, mine });
  }
  const out = new Map();
  if (!batch.length) return out;
  const res = run(batch.map(({ it, mine }) => ({ path: it.p, text: it.text, ops: mine.map((r) => ({ id: r.id, ...r.roslyn })) })));
  batch.forEach(({ it, mine }, i) => {
    const f = res[i];
    const hits = {};
    const errors = [];
    for (const r of mine) {
      const x = f.results.find((y) => y.id === r.id);
      hits[r.id] = x?.hits ?? 0;
      if (!x) errors.push('transform ' + r.id + ' (roslyn ' + r.roslyn.op + ') was not run on ' + it.p);
      else if (x.error) errors.push('transform ' + r.id + ' (roslyn ' + r.roslyn.op + ') ' + x.error + ' in ' + it.p + ': fix the rule in tools/sync/transforms.json, never the vendored file');
    }
    for (const e of f.errors) errors.push(it.p + ': ' + e);
    // As for text rules, a rule is named in the header only when it changed the text (a retarget to the same name doesn't).
    const applied = mine.filter((r) => f.results.find((y) => y.id === r.id)?.changed);
    out.set(it.p, { text: f.text, hits, applied, errors });
  });
  return out;
}

/** Rules that apply to one vendored file of the given tier, in rule order: listed rules (tier 2 only) and glob rules. */
export const rulesFor = (rules, p, tier) => rules.filter((r) => (r.files ? tier === 2 && r.files.includes(p) : r.globRe.test(p)));

/**
 * Applies the given rules to text. Returns the new text, hits per rule, and errors: a listed rule with no hit, a drop
 * that doesn't find exactly one member, a "find" that matches an empty string.
 */
export function applyTransforms(rules, p, text) {
  const errors = [];
  const hits = {};
  for (const r of rules) {
    if (r.kind === 'roslyn') throw new Error('transform ' + r.id + ': "roslyn" rules run through applyRoslyn, in one batch');
    if (r.kind === 'drop') {
      const d = dropMember(text, r.re);
      hits[r.id] = d.error ? 0 : 1;
      if (d.error) errors.push('transform ' + r.id + ' (drop) ' + d.error + ' in ' + p + ': fix the rule in tools/sync/transforms.json, never the vendored file');
      else text = d.text;
      continue;
    }
    let n = 0;
    r.re.lastIndex = 0;
    let empty = false;
    text = text.replace(r.re, (...args) => {
      n++;
      if (!args[0]) empty = true;
      const named = typeof args.at(-1) === 'object';
      const groups = args.slice(1, named ? -3 : -2); // drop the match, offset, input (and named groups)
      return r.replace.replace(/\$(\$|\d)/g, (_, d) => (d === '$' ? '$' : groups[+d - 1] ?? ''));
    });
    hits[r.id] = n;
    if (empty) errors.push('transform ' + r.id + ' matches an empty string in ' + p + ': its "find" must always consume text');
    else if (!n && r.files) errors.push('transform ' + r.id + ' no longer matches ' + p + ': fix the rule in tools/sync/transforms.json, never the vendored file');
  }
  return { text, hits, errors };
}

/**
 * Transforms one vendored file: applies, in rule order, the rules that apply to it (rulesFor), and checks the others.
 * A listed "find" rule that doesn't list the file but matches its text at that point is an error: new upstream code
 * the rule would have converted. Checking the text as transformed so far means an earlier rule that already
 * converted the same code (a more specific one) doesn't trip it. Glob rules apply wherever they match, and a drop
 * names one member of the files it lists, so neither is checked. Returns {text, hits, applied (rules that changed
 * the file), errors}.
 */
export function transformFile(rules, p, tier, text) {
  const errors = [];
  const hits = {};
  const applied = [];
  const mine = new Set(rulesFor(rules, p, tier));
  for (const r of rules) {
    if (r.kind === 'roslyn') {
      // Applied before, in one batch (applyRoslyn). A retarget that doesn't list this C# file but finds its type in
      // the file's code is an error, as for a find rule: new upstream code it would have converted.
      if (r.guard && !r.files.includes(p) && p.endsWith('.cs')) {
        const code = codeMask(text);
        if ([...text.matchAll(r.guard)].some((m) => code[m.index]))
          errors.push('transform ' + r.id + ' (roslyn retarget-type) matches ' + p + ', which it does not list: add the file to its "files" (tier 2), or classify the file again');
      }
      continue;
    }
    if (mine.has(r)) {
      const x = applyTransforms([r], p, text);
      if (x.text !== text) applied.push(r); // a match whose replacement equals it changes nothing
      text = x.text;
      errors.push(...x.errors);
      hits[r.id] = x.hits[r.id];
    } else if (r.kind === 'find' && r.files && !r.files.includes(p) && new RegExp(r.find, r.flags).test(text)) {
      errors.push('transform ' + r.id + ' matches ' + p + ', which it does not list: add the file to its "files" (tier 2), or classify the file again');
    }
  }
  return { text, hits, applied, errors };
}

/** Each file a listed rule names must exist at the commit and be tier 2. tree: every path at the commit. */
export function checkTransformTargets(rules, byFile, tree) {
  const errors = [];
  for (const r of rules) for (const f of r.files ?? []) {
    const c = byFile.get(f);
    if (!c) errors.push('transform ' + r.id + ' lists ' + f + ', which ' + (tree.has(f) ? 'matches no tier map rule' : 'is not in upstream at this commit'));
    else if (c.tier !== 2) errors.push('transform ' + r.id + ' lists ' + f + ', which is tier ' + c.tier + ' (only tier 2 files take listed rules)');
  }
  return errors;
}

/** Errors for glob rules that changed no vendored file. hits: rule id -> files changed. */
export function idleGlobRules(rules, hits) {
  return rules.filter((r) => r.glob && !hits.get(r.id))
    .map((r) => 'transform ' + r.id + ' (glob ' + r.glob + ') changes no vendored file: fix or remove the rule');
}
