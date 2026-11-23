// Deterministic text transforms for tier 2 files. Each rule lists the upstream files it must change.
// A rule that no longer matches a file it lists fails the sync loudly: fix the rule, never the vendored file.
// rules: {"rules": [{"id", "find" (JS regex source), "replace" ($1..$9 for groups), "why", "files": [paths]}]}

export function loadTransforms(json) {
  if (!json || !Array.isArray(json.rules)) throw new Error('transforms: expected {"rules": [...]}');
  const ids = new Set();
  return json.rules.map((r, i) => {
    const where = 'transform ' + (r.id ?? '#' + (i + 1));
    if (typeof r.id !== 'string' || !r.id) throw new Error('transform #' + (i + 1) + ': needs an "id"');
    if (ids.has(r.id)) throw new Error(where + ': duplicate id');
    ids.add(r.id);
    if (typeof r.find !== 'string' || !r.find) throw new Error(where + ': "find" must be a non-empty regex string');
    if (typeof r.replace !== 'string') throw new Error(where + ': "replace" must be a string');
    if (!Array.isArray(r.files) || !r.files.length) throw new Error(where + ': "files" must list the files it changes');
    let re;
    try { re = new RegExp(r.find, 'g'); } catch (e) { throw new Error(where + ': bad regex: ' + e.message); }
    const groups = new RegExp(r.find + '|').exec('').length - 1;
    for (const m of r.replace.matchAll(/\$(\$|\d+)/g)) if (m[1] !== '$' && (+m[1] < 1 || +m[1] > groups || m[1].length > 1))
      throw new Error(where + ': "replace" uses $' + m[1] + ', but "find" has ' + groups + ' groups (use $1..$' + Math.min(groups, 9) + ', or $$ for a literal $)');
    return { id: r.id, find: r.find, replace: r.replace, why: r.why ?? '', files: r.files, re };
  });
}

/** Rules that apply to one file, in file order. */
export const rulesFor = (rules, p) => rules.filter((r) => r.files.includes(p));

/** Applies the given rules to text. Returns the new text, hits per rule, and an error per rule with no hit. */
export function applyTransforms(rules, p, text) {
  const errors = [];
  const hits = {};
  for (const r of rules) {
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
    else if (!n) errors.push('transform ' + r.id + ' no longer matches ' + p + ': fix the rule in tools/sync/transforms.json, never the vendored file');
  }
  return { text, hits, errors };
}

/** Errors for rules that match a vendored file they don't list (new upstream code a rule would have converted). */
export function unlistedMatches(rules, p, text) {
  const errors = [];
  for (const r of rules) if (!r.files.includes(p) && new RegExp(r.find).test(text))
    errors.push('transform ' + r.id + ' matches ' + p + ', which it does not list: add the file to its "files" (tier 2), or classify the file again');
  return errors;
}

/** Each file a rule lists must exist at the commit and be tier 2. tree: every path at the commit. */
export function checkTransformTargets(rules, byFile, tree) {
  const errors = [];
  for (const r of rules) for (const f of r.files) {
    const c = byFile.get(f);
    if (!c) errors.push('transform ' + r.id + ' lists ' + f + ', which ' + (tree.has(f) ? 'matches no tier map rule' : 'is not in upstream at this commit'));
    else if (c.tier !== 2) errors.push('transform ' + r.id + ' lists ' + f + ', which is tier ' + c.tier + ' (only tier 2 files are transformed)');
  }
  return errors;
}
