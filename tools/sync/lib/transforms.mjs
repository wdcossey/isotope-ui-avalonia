// Deterministic text transforms for vendored files. Each rule is scoped one of two ways:
//   "files": [paths]  tier 2 files it must change; a listed file it no longer matches fails the sync loudly.
//   "glob": pattern   every vendored file (tier 1 or 2) the glob matches, wherever the rule matches; it must change at
//                     least one file. For repo-wide renames (the namespace), so tier 1 means "no rule but those".
//                     Glob rules come after every listed rule (see loadTransforms).
// and does one of two things:
//   "find" (JS regex source, "flags" optional: any of "imsu", added to "g") with "replace" ($1..$9 for groups, $$ for $);
//   "drop" (JS regex source): deletes the one member whose declaration it matches in code (not in a comment or
//          string), with its doc comment and attributes and its body; the lines are left blank, so line numbers still
//          match upstream's. Exactly one match in each listed file. Only with "files".
// Fix a rule that stops matching, never the vendored file.
// rules: {"rules": [{"id", "find"+"replace" | "drop", "flags"?, "why", "files" | "glob"}]}
import { globToRegExp } from './glob.mjs';
import { dropMember } from './csharp.mjs';

export function loadTransforms(json) {
  if (!json || !Array.isArray(json.rules)) throw new Error('transforms: expected {"rules": [...]}');
  const ids = new Set();
  const rules = json.rules.map((r, i) => {
    const where = 'transform ' + (r.id ?? '#' + (i + 1));
    if (typeof r.id !== 'string' || !r.id) throw new Error('transform #' + (i + 1) + ': needs an "id"');
    if (ids.has(r.id)) throw new Error(where + ': duplicate id');
    ids.add(r.id);
    const drop = r.drop !== undefined;
    if (drop === (r.find !== undefined)) throw new Error(where + ': needs exactly one of "find" or "drop"');
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
  const lastListed = rules.findLastIndex((r) => r.files);
  const early = rules.find((r, i) => r.glob && i < lastListed);
  if (early) throw new Error('transform ' + early.id + ': glob rules come after every listed rule (' + rules[lastListed].id + ' follows it)');
  return rules;
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
