// Whether CI's samples job measures (#71): docs-only changes skip the measurement, everything else measures. Pure
// functions; should-measure.mjs gathers the facts with git and test/changes.test.mjs tests these.
//
// Docs-only means every changed file is under docs/ or is a .md file anywhere, and nothing else. Anything that can't be
// decided measures: main itself, workflow_dispatch, another event, a tag, a shallow history, no merge base, a git error,
// or no changed files at all.

// Whether a changed path (as git prints it, with / separators) is documentation.
export function isDocs(file) {
  return file.startsWith('docs/') || file.endsWith('.md');
}

// The decision: { measure, reason }. The reason holds no file name (the names are a fork's to choose).
export function decide({ event, ref, shallow, mergeBase, files, error }) {
  if (event !== 'push') return { measure: true, reason: 'event ' + event + ': always measured' };
  if (ref === 'refs/heads/main') return { measure: true, reason: 'main: always measured' };
  if (typeof ref !== 'string' || !ref.startsWith('refs/heads/')) return { measure: true, reason: 'not a branch push: measured' };
  if (error) return { measure: true, reason: 'the changes could not be listed (' + error + '): measured' };
  if (shallow) return { measure: true, reason: 'shallow history, so no merge base: measured' };
  if (!mergeBase) return { measure: true, reason: 'no merge base with origin/main: measured' };
  if (!Array.isArray(files) || files.length === 0) return { measure: true, reason: 'no changed files since the merge base: measured' };
  const other = files.filter(f => !isDocs(f)).length;
  if (other > 0) return { measure: true, reason: other + ' of ' + files.length + ' changed files are not docs: measured' };
  return { measure: false, reason: 'all ' + files.length + ' changed files are docs (under docs/ or *.md): measurement skipped' };
}
