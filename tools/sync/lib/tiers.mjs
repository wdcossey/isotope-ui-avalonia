// The tier map: ordered rules over upstream repo-relative paths, first match wins.
// Each rule has exactly one of "path" (an exact path) or "glob" (see glob.mjs), and a tier:
//   1      vendored verbatim
//   2      vendored, with the transforms in transforms.json
//   3      rewritten for Avalonia: not vendored, watched (the sync report lists the PRs that touch it)
//   "none" not imported and not watched
import { globToRegExp } from './glob.mjs';

export const TIERS = [1, 2, 3, 'none'];

export function loadTierMap(json) {
  if (!json || !Array.isArray(json.rules)) throw new Error('tier map: expected {"rules": [...]}');
  const rules = json.rules.map((r, i) => {
    const where = 'tier map rule ' + (i + 1);
    if ((r.path === undefined) === (r.glob === undefined)) throw new Error(where + ': needs exactly one of "path" or "glob"');
    const pattern = r.path ?? r.glob;
    if (typeof pattern !== 'string' || !pattern || pattern.startsWith('/') || pattern.includes('\\'))
      throw new Error(where + ': pattern must be a relative path with forward slashes: ' + JSON.stringify(pattern));
    if (!TIERS.includes(r.tier)) throw new Error(where + ' (' + pattern + '): tier must be 1, 2, 3 or "none", not ' + JSON.stringify(r.tier));
    const test = r.path !== undefined ? (p) => p === r.path : ((re) => (p) => re.test(p))(globToRegExp(r.glob));
    return { index: i, pattern, kind: r.path !== undefined ? 'path' : 'glob', tier: r.tier, note: r.note ?? '', test };
  });
  return { rules };
}

/** The first rule matching the path, or null (unclassified). */
export function classify(map, p) {
  for (const r of map.rules) if (r.test(p)) return r;
  return null;
}

export const isVendored = (tier) => tier === 1 || tier === 2;

/**
 * Classifies every file of an upstream tree. Returns the tier of each file, the files no rule matches,
 * and an error for each rule that matches no file (an include that silently matches nothing).
 */
export function classifyTree(map, files) {
  const byFile = new Map();
  const hits = new Array(map.rules.length).fill(0);
  const unclassified = [];
  for (const f of files) {
    const r = classify(map, f);
    if (!r) { unclassified.push(f); continue; }
    hits[r.index]++;
    byFile.set(f, r);
  }
  const errors = [];
  for (const r of map.rules) if (!hits[r.index])
    errors.push('tier map rule ' + (r.index + 1) + ' (' + r.kind + ' ' + r.pattern + ', tier ' + r.tier + ') matches no file' +
      (r.kind === 'path' && files.includes(r.pattern) ? ': an earlier rule takes it' : ''));
  return { byFile, unclassified, errors, hits };
}
