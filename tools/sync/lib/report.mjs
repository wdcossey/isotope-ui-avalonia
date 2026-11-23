// The sync report: what changed upstream, what was regenerated, and the tier 3 PRs to port by hand.
// No personal data: no author names (none are read), merge subjects lose their source branch, handles and
// email addresses in subjects are removed, and so do branch names and repo owners inside a subject.
import { classify, isVendored } from './tiers.mjs';
import { scanText } from './personal.mjs';

// A slash token inside a subject can be a branch ("user/<alias>/topic", "someone/fix-thing") or a GitHub owner/repo
// ("someone/tool@v2", "bump someone/action"), and either can carry a username. Tokens of three or more parts are
// omitted (a branch can't be told from a folder or a type list there), except file paths with a known file extension
// and numbers (dates). Two-part tokens lose their first part where the shape or the words around them say branch or repo.
// Ordinary slashes stay: type pairs ("ListView/GridView"), alternatives ("min/max"), paths that start with "/".
// A two-part token in no such context ("someone/feature") can't be told from "cell/row", so it stays: a known gap.
const SLASH_TOKEN = /(?<![\w./@\\-])[A-Za-z0-9][\w.-]*(?:\/[\w.-]+)+/g;
const FILE_EXT = /\.(?:cs|csproj|props|targets|sln|slnx|xaml|axaml|md|txt|json|ya?ml|xml|config|ps1|sh|cmd|m?js|ts|tsx|py|html|css|svg|png|toml)$/i;
const BRANCH_CONTEXT = /\b(?:bump|branch|from|into|port|cherry-pick|merge|merged|revert)\s+$/i;
function maskSlashToken(tok, offset, s) {
  const segs = tok.split('/');
  const before = s.slice(0, offset);
  const after = s.slice(offset + tok.length);
  if (/^users?$/i.test(segs[0])) return '(branch name omitted)';
  if (segs.every((x) => /^\d+$/.test(x))) return tok;
  if (segs.length >= 3) return FILE_EXT.test(segs.at(-1)) ? tok : '(name omitted)';
  const owner = '(owner omitted)/' + segs[1];
  if (/^[@#!]\w/.test(after) || BRANCH_CONTEXT.test(before)) return owner;
  if (/^[A-Z]/.test(segs[0]) && /^[A-Z]/.test(segs[1])) return tok;
  if (/^[a-z0-9][\w.]*-/.test(segs[1])) return owner;
  return tok;
}

export function sanitizeSubject(s) {
  // A squash subject that is just a branch name ("someone/feature (#12)") can carry a username.
  const branchOnly = /^\S+\/\S+(\s+\(#\d+\))?$/.exec(s.trim());
  if (branchOnly) s = '(subject was a branch name, omitted)' + (branchOnly[1] ?? '');
  return s
    .replace(/(Merge pull request #\d+) from [^\s"']+/g, '$1')
    .replace(/Merge (remote-tracking )?branch '[^']*'( of \S+)?( into \S+)?/g, 'Merge $1branch (source branch omitted)')
    // A URL keeps its scheme and host; user info and the path can name a person. So does an scp-style git remote.
    .replace(/\b([a-z][\w+.-]*):\/\/(?:[^\s/@]*@)?([^\s/@]+)(\/[^\s)\]>"']*)?/gi, (_, scheme, host, rest) => scheme + '://' + host + (rest ? '/(path omitted)' : ''))
    .replace(/(?<![\w.-])[\w.-]+@([\w-]+(?:\.[\w-]+)+):[^\s)\]>"']+/g, '$1:(path omitted)')
    .replace(SLASH_TOKEN, maskSlashToken)
    .replace(/[\w.+-]+@[\w-]+(\.[\w-]+)+/g, '(email removed)')
    .replace(/(^|[^\w])@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/g, '$1(handle removed)')
    // Last, whatever the personal-data check (personal.mjs) still finds: a local path, or an email the rule above missed.
    .replace(/\S+/g, (tok) => (scanText(tok).length ? '(local path or email removed)' : tok))
    .replace(/\|/g, '\\|');
}

export function prNumber(subject) {
  const m = /\(#(\d+)\)\s*$/.exec(subject) ?? /^Merge pull request #(\d+)/.exec(subject);
  return m ? +m[1] : null;
}

const short = (sha) => sha.slice(0, 8);
// A code span in a table cell: pipes escaped, and a longer fence when the text has a backtick.
const code = (s) => {
  const t = s.replace(/\|/g, '\\|');
  return t.includes('\x60') ? '\x60\x60 ' + t + ' \x60\x60' : '\x60' + t + '\x60';
};
const tierCell = (t) => (t === 'unclassified' ? '**unclassified**' : t === 'none' ? 'not imported' : 'tier ' + t);

function commitRows(commits, map, pick, repoUrl) {
  const rows = [];
  for (const c of commits) {
    const paths = c.paths.filter((p) => pick(classify(map, p)?.tier));
    if (!paths.length) continue;
    const n = prNumber(c.subject);
    const link = n ? '[#' + n + '](' + repoUrl + '/pull/' + n + ')' : code(short(c.sha));
    const shown = paths.slice(0, 8).map(code).join('<br>') + (paths.length > 8 ? '<br>… ' + (paths.length - 8) + ' more' : '');
    rows.push('| ' + link + ' | ' + c.date + ' | ' + sanitizeSubject(c.subject) + ' | ' + shown + ' |');
  }
  return rows;
}

/** Compares two manifests' file lists by path and sha256. */
export function vendoredDelta(before, after) {
  const b = new Map((before?.files ?? []).map((f) => [f.path, f.sha256]));
  const a = new Map(after.files.map((f) => [f.path, f.sha256]));
  const delta = { added: [], removed: [], changed: [] };
  for (const [p, h] of a) if (!b.has(p)) delta.added.push(p); else if (b.get(p) !== h) delta.changed.push(p);
  for (const p of b.keys()) if (!a.has(p)) delta.removed.push(p);
  return delta;
}

export function renderReport({ repoUrl, old, new: neu, commits, changes, unclassified, delta, manifest, map, verify }) {
  const t = (n) => manifest.files.filter((f) => f.tier === n).length;
  const L = [];
  L.push('# Upstream sync ' + short(old.sha) + ' → ' + short(neu.sha), '');
  L.push('Generated by tools/sync. Upstream: ' + repoUrl + '.', '');
  L.push('- **Range:** ' + short(old.sha) + ' (' + old.date + ') → ' + short(neu.sha) + ' (' + neu.date + '), ' + commits.length + ' first-parent commits.');
  L.push('- **Vendored:** ' + manifest.files.length + ' files (tier 1: ' + t(1) + ', tier 2: ' + t(2) + '). Changed by this sync: ' +
    delta.changed.length + ' changed, ' + delta.added.length + ' added, ' + delta.removed.length + ' removed.');
  if (verify === null) L.push('- **Upstream tests for vendored paths:** not run (--skip-verify).');
  else if (!verify.length) L.push('- **Upstream tests for vendored paths:** none configured.');
  else for (const v of verify) L.push('- **Upstream tests, ' + v.name + ':** ' + v.passed + ' passed, ' + v.failed + ' failed, ' + v.skipped + ' skipped, ' + v.total + ' total.');
  L.push('');
  L.push('## Paths to classify', '');
  L.push('Added, renamed and deleted upstream paths, with the tier the current tier map gives them. Check each, and change tools/sync/tiers.json where the tier is wrong.', '');
  if (!changes.length) L.push('None.');
  else {
    L.push('| Change | Path | Tier |', '|---|---|---|');
    for (const c of changes) {
      if (c.change === 'renamed') L.push('| renamed | ' + code(c.from) + ' → ' + code(c.path) + ' | ' + tierCell(c.fromTier) + ' → ' + tierCell(c.tier) + ' |');
      else L.push('| ' + c.change + ' | ' + code(c.path) + (c.copyOf ? ' (copy of ' + code(c.copyOf) + ')' : '') + ' | ' + tierCell(c.tier) + ' |');
    }
  }
  if (unclassified.length) {
    L.push('', '**Unclassified at ' + short(neu.sha) + ':** ' + unclassified.length + ' files under the watched roots match no rule.', '');
    for (const p of unclassified.slice(0, 50)) L.push('- ' + code(p));
    if (unclassified.length > 50) L.push('- … ' + (unclassified.length - 50) + ' more');
  }
  L.push('', '## Tier 3: PRs to port by hand', '');
  L.push('These touched rewritten (tier 3) paths. The compiler cannot flag their behaviour changes: read each one and port what applies.', '');
  const t3 = commitRows(commits, map, (tier) => tier === 3, repoUrl);
  L.push(...(t3.length ? ['| PR | Date | Subject | Tier 3 paths |', '|---|---|---|---|', ...t3] : ['None.']));
  L.push('', '## Tier 1 and 2: PRs regenerated', '');
  const t12 = commitRows(commits, map, isVendored, repoUrl);
  L.push(...(t12.length ? ['| PR | Date | Subject | Vendored paths |', '|---|---|---|---|', ...t12] : ['None.']));
  L.push('');
  return L.join('\n');
}
