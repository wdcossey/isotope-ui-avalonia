// Read-only git access to the upstream cache (plus fetch and checkout, the only writes the harness makes there).
import { execFileSync } from 'node:child_process';

const BASE = ['-c', 'core.quotePath=false', '-c', 'log.showSignature=false', '-c', 'diff.renames=true'];

export function git(dir, args, opts = {}) {
  return execFileSync('git', [...BASE, ...args], {
    cwd: dir, input: opts.input, encoding: opts.buffer ? null : 'utf8',
    maxBuffer: 1 << 30, stdio: ['pipe', 'pipe', opts.quiet ? 'ignore' : 'pipe'],
  });
}

export function tryGit(dir, args) {
  try { return git(dir, args, { quiet: true }).trim(); } catch { return null; }
}

export const revParse = (dir, rev) => tryGit(dir, ['rev-parse', '--verify', '--quiet', rev + '^{commit}']);

export const isAncestor = (dir, a, b) => {
  try { git(dir, ['merge-base', '--is-ancestor', a, b], { quiet: true }); return true; } catch { return false; }
};

/** Every entry in the commit's tree: Map(path -> mode). Mode 100644/100755 is a file, 120000 a link, 160000 a submodule. */
export function lsTree(dir, commit) {
  const entries = new Map();
  for (const e of git(dir, ['ls-tree', '-r', '-z', commit]).split('\0')) {
    if (!e) continue;
    const tab = e.indexOf('\t');
    entries.set(e.slice(tab + 1), e.slice(0, e.indexOf(' ')));
  }
  return entries;
}

/** Parses "git log -z --format=%x01... --name-only": NUL-separated, so no path is ever quoted. */
function parseLogZ(out) {
  const commits = [];
  for (let tok of out.split('\0')) {
    if (tok.startsWith('\n')) tok = tok.slice(1);
    if (!tok) continue;
    if (tok.startsWith('\x01')) commits.push({ head: tok.slice(1), paths: [] });
    else if (commits.length) commits.at(-1).paths.push(tok);
  }
  return commits;
}

/** Blob contents (Buffers) of the given paths at the commit, in one git process. */
export function readBlobs(dir, commit, paths) {
  if (!paths.length) return new Map();
  const out = git(dir, ['cat-file', '--batch'], { buffer: true, input: paths.map((p) => commit + ':' + p).join('\n') + '\n' });
  const result = new Map();
  let pos = 0;
  for (const p of paths) {
    const nl = out.indexOf(10, pos);
    const head = out.subarray(pos, nl).toString('utf8');
    const m = /^([0-9a-f]+) blob (\d+)$/.exec(head);
    if (!m) throw new Error('git cat-file: no blob for ' + commit + ':' + p + ' (' + head + ')');
    const size = +m[2];
    result.set(p, out.subarray(nl + 1, nl + 1 + size));
    pos = nl + 1 + size + 1; // content, then a newline
  }
  return result;
}

/** For each path, the last commit at or before the given commit that changed it. */
export function lastChanged(dir, commit, paths) {
  const result = new Map();
  for (let i = 0; i < paths.length; i += 64) {
    const chunk = paths.slice(i, i + 64);
    const want = new Set(chunk);
    const out = git(dir, ['log', '-z', '--no-renames', '--format=%x01%H', '--name-only', commit, '--', ...chunk]);
    for (const c of parseLogZ(out)) for (const p of c.paths) if (want.has(p) && !result.has(p)) result.set(p, c.head);
  }
  for (const p of paths) if (!result.has(p)) throw new Error('git log: no commit found for ' + p);
  return result;
}

/** Added, deleted, renamed (-M) and modified paths between two commits. */
export function diffNameStatus(dir, from, to) {
  const parts = git(dir, ['diff', '-M', '-z', '--name-status', from, to]).split('\0');
  const changes = [];
  for (let i = 0; i < parts.length && parts[i]; ) {
    const status = parts[i++];
    if (status[0] === 'R' || status[0] === 'C') changes.push({ status: status[0], score: +status.slice(1), from: parts[i++], path: parts[i++] });
    else changes.push({ status: status[0], path: parts[i++] });
  }
  return changes;
}

/** First-parent commits in (from, to], oldest first: sha, subject and the paths each one changed. */
export function commitsBetween(dir, from, to) {
  const out = git(dir, ['log', '-z', '--first-parent', '--diff-merges=first-parent', '--no-renames', '--reverse',
    '--format=%x01%H%x02%cs%x02%s', '--name-only', from + '..' + to]);
  return parseLogZ(out).map((c) => {
    const [sha, date, subject] = c.head.split('\x02');
    return { sha, date, subject, paths: c.paths };
  });
}

export function remotes(dir) {
  const out = tryGit(dir, ['remote', '-v']) ?? '';
  const map = new Map();
  for (const line of out.split('\n')) {
    const m = /^(\S+)\s+(\S+)\s+\(fetch\)$/.exec(line.trim());
    if (m) map.set(m[1], m[2]);
  }
  return map;
}
