// The baseline of the migration on upstream's samples (#71): which counts of measure-samples.mjs's summary are gated,
// how a run compares with tools/migration/samples-baseline.json, and the guide's table, which is generated from it.
// No I/O here: measure-samples.mjs reads and writes the files, and test/baseline.test.mjs tests these functions.

// Each gated count: its key in the baseline, what it is, and which way is better ('up', 'down', or 'same' for the size of
// the sample set, which changes only with UPSTREAM.lock).
export const COUNTS = [
  ['projects', 'sample projects measured', 'same'],
  ['files', 'C# files measured', 'same'],
  ['projectsBuildBefore', 'projects that build before the fixes', 'up'],
  ['projectsBuild', 'projects that build after the fixes', 'up'],
  ['filesBuildBefore', 'files that build before the fixes', 'up'],
  ['filesBuildAfter', 'files that build after the fixes', 'up'],
  ['errorsBefore', 'compile errors before the fixes', 'down'],
  ['errorsAfter', 'compile errors after the fixes', 'down'],
  ['brokenByMigration', 'files that built before the fixes and not after', 'down'],
  ['formatFailures', 'dotnet format runs that failed', 'down'],
  ['migrationLeftTotal', 'migration diagnostics left', 'down'],
];

// The gated counts of a summary (summary.json's "summary"). Migration diagnostics are gated per ID as well as in total;
// per-message counts (a name in each message) and compile errors per ID are not: they move whenever a rename or a control
// lands, often while the totals get better.
export function countsOf(summary) {
  const migrationLeft = Object.fromEntries(Object.entries(summary.migrationLeft ?? {}).filter(([, n]) => n > 0).sort(([a], [b]) => (a < b ? -1 : 1)));
  return {
    projects: summary.projects,
    files: summary.files,
    projectsBuildBefore: summary.projectsBuildBefore,
    projectsBuild: summary.projectsBuild,
    filesBuildBefore: summary.filesBuildBefore,
    filesBuildAfter: summary.filesBuildAfter,
    errorsBefore: summary.errorsBefore,
    errorsAfter: summary.errorsAfter,
    brokenByMigration: summary.brokenByMigration.length,
    // Named as well as counted, so a change that fixes one file and breaks another is still worse.
    brokenFiles: [...summary.brokenByMigration].sort(),
    formatFailures: summary.formatFailures ?? 0,
    migrationLeftTotal: Object.values(migrationLeft).reduce((a, b) => a + b, 0),
    migrationLeft,
  };
}

// Compares a run's counts (countsOf) with the baseline's. Returns the differences in three lists of messages: worse,
// better, and changed (the sample set, or the upstream commit, is another). Any difference fails the check.
export function compare(baseline, counts, upstream) {
  const worse = [], better = [], changed = [];
  if (baseline.upstream !== upstream)
    changed.push('the baseline was measured on upstream ' + short(baseline.upstream) + ', and UPSTREAM.lock is ' + short(upstream));
  const was = baseline.counts ?? {};
  const one = (what, direction, before, now) => {
    if (before === now) return;
    if (direction === 'same' || typeof before !== 'number' || typeof now !== 'number') {
      changed.push(what + ': ' + fmt(before) + ' -> ' + fmt(now));
      return;
    }
    const line = what + ': ' + fmt(before) + ' -> ' + fmt(now) + ' (' + (now > before ? '+' : '') + fmt(now - before) + ')';
    if ((direction === 'up') === (now > before)) better.push(line);
    else worse.push(line);
  };
  for (const [key, what, direction] of COUNTS) one(what, direction, was[key], counts[key]);
  const wasBroken = new Set(was.brokenFiles ?? []), nowBroken = new Set(counts.brokenFiles);
  for (const f of nowBroken) if (!wasBroken.has(f)) worse.push('built before the fixes and not after: ' + f);
  for (const f of wasBroken) if (!nowBroken.has(f)) better.push('builds after the fixes again: ' + f);
  const ids = [...new Set([...Object.keys(was.migrationLeft ?? {}), ...Object.keys(counts.migrationLeft)])].sort();
  for (const id of ids) one(id + ' left', 'down', was.migrationLeft?.[id] ?? 0, counts.migrationLeft[id] ?? 0);
  return { worse, better, changed };
}

export function baselineOf(counts, upstream) {
  return {
    about: "The migration on upstream's samples (#71), as tools/migration/measure-samples.mjs measures it at the upstream commit below. CI fails when a run differs. Write it with: node tools/migration/measure-samples.mjs --out <empty folder> --write-baseline",
    upstream,
    counts,
  };
}

export const GUIDE_START = '<!-- samples-baseline: generated from tools/migration/samples-baseline.json by measure-samples.mjs --write-baseline -->';
export const GUIDE_END = '<!-- end samples-baseline -->';

// The guide's counts, as markdown: the before and after table, then the migration diagnostics left per ID.
export function guideTable(counts) {
  const c = counts;
  return [
    '| | Before the fixes | After |',
    '|---|---|---|',
    '| Files that build | ' + fmt(c.filesBuildBefore) + ' of ' + fmt(c.files) + ' | ' + fmt(c.filesBuildAfter) + ' of ' + fmt(c.files) + ' |',
    '| Compile errors | ' + fmt(c.errorsBefore) + ' | ' + fmt(c.errorsAfter) + ' |',
    '| Projects that build | ' + fmt(c.projectsBuildBefore) + ' of ' + fmt(c.projects) + ' | ' + fmt(c.projectsBuild) + ' of ' + fmt(c.projects) + ' |',
    '| Files that built before the fixes and not after | | ' + fmt(c.brokenByMigration) + ' |',
    '| Failed `dotnet format` runs | | ' + fmt(c.formatFailures) + ' |',
    '',
    '| Migration diagnostics left | Count |',
    '|---|---|',
    ...Object.entries(c.migrationLeft).map(([id, n]) => '| ' + id + ' | ' + fmt(n) + ' |'),
    '| All | ' + fmt(c.migrationLeftTotal) + ' |',
  ].join('\n');
}

// The guide with its generated block replaced by the counts' table. Throws unless the guide has each marker once, in order.
export function withGuideTable(guide, counts) {
  const nl = guide.includes('\r\n') ? '\r\n' : '\n';
  const start = guide.indexOf(GUIDE_START), end = guide.indexOf(GUIDE_END);
  if (start < 0 || end < start || guide.indexOf(GUIDE_START, start + 1) >= 0 || guide.indexOf(GUIDE_END, end + 1) >= 0)
    throw new Error('the guide needs one "' + GUIDE_START + '" line, and one "' + GUIDE_END + '" line after it');
  const block = (GUIDE_START + '\n' + guideTable(counts) + '\n' + GUIDE_END).replace(/\n/g, nl);
  return guide.slice(0, start) + block + guide.slice(end + GUIDE_END.length);
}

// Thousands with commas, as the guide writes them (not toLocaleString, which depends on the runtime's locale data).
function fmt(n) { return typeof n === 'number' ? String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',') : String(n); }
function short(sha) { return typeof sha === 'string' ? sha.slice(0, 8) : String(sha); }
