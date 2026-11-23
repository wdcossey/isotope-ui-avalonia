// Path changes between two upstream commits that need a classification decision: added, renamed and deleted
// paths under the watched roots, or matched by a tier 1, 2 or 3 rule. Modified files are not listed here;
// the PR sections of the report cover them.
import { classify } from './tiers.mjs';

const tierOf = (map, p) => classify(map, p)?.tier ?? 'unclassified';

export function pathChanges(map, nameStatus, watchRoots) {
  const relevant = (p) => watchRoots.some((r) => p.startsWith(r)) || [1, 2, 3].includes(classify(map, p)?.tier);
  const out = [];
  for (const c of nameStatus) {
    if (c.status === 'A' && relevant(c.path)) out.push({ change: 'added', path: c.path, tier: tierOf(map, c.path) });
    else if (c.status === 'D' && relevant(c.path)) out.push({ change: 'deleted', path: c.path, tier: tierOf(map, c.path) });
    else if (c.status === 'R' && (relevant(c.path) || relevant(c.from)))
      out.push({ change: 'renamed', from: c.from, path: c.path, fromTier: tierOf(map, c.from), tier: tierOf(map, c.path) });
    else if (c.status === 'C' && relevant(c.path)) out.push({ change: 'added', path: c.path, tier: tierOf(map, c.path), copyOf: c.from });
  }
  return out;
}

/** Hints for empty-include errors: an exact path rule whose file was renamed or deleted upstream. */
export function includeHints(map, changes) {
  const hints = [];
  for (const r of map.rules) if (r.kind === 'path') for (const c of changes) {
    if (c.change === 'renamed' && c.from === r.pattern) hints.push('tier map rule ' + (r.index + 1) + ': ' + r.pattern + ' was renamed upstream to ' + c.path);
    if (c.change === 'deleted' && c.path === r.pattern) hints.push('tier map rule ' + (r.index + 1) + ': ' + r.pattern + ' was deleted upstream');
  }
  return hints;
}
