// tools/sync: vendor-and-regenerate upstream import (issue #3). See sync.mjs for usage.
import { readFileSync, writeFileSync, existsSync, mkdtempSync, rmSync, mkdirSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { git, tryGit, revParse, isAncestor, diffNameStatus, commitsBetween, remotes } from './git.mjs';
import { loadTierMap } from './tiers.mjs';
import { loadTransforms } from './transforms.mjs';
import { loadAllowlist, scanText } from './personal.mjs';
import { generate, MANIFEST } from './vendor.mjs';
import { pathChanges, includeHints } from './changes.mjs';
import { renderReport, vendoredDelta, prNumber } from './report.mjs';
import { parseLock, updateLock } from './lock.mjs';
import { writeTree, compareTree, replaceTree } from './tree.mjs';
import { runVerify } from './verify.mjs';

export const USAGE = [
  'Usage: node tools/sync/sync.mjs [<upstream commit>] [options]',
  '  <upstream commit>  commit or ref in the upstream cache (default: <remote>/main, the remote whose URL is in UPSTREAM.lock)',
  '  --check            regenerate from the commit in UPSTREAM.lock and fail if src/Upstream differs (for CI)',
  '  --dry-run          do everything except writing to the repo; print the report',
  '  --no-fetch         do not fetch the upstream cache',
  '  --skip-verify      do not build and run the upstream tests for the vendored paths',
  '  --no-checkout      leave the upstream cache checked out where it is',
  '  --commit           commit the result as "sync upstream <old> → <new>"',
  '  --repo <dir>       this repo (default: two levels above this script)',
  '  --upstream <dir>   the upstream cache (default: <repo>/upstream)',
].join('\n');

class Fail extends Error {}

function parseArgs(argv) {
  const o = { flags: new Set() };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--repo' || a === '--upstream') { if (!argv[i + 1]) throw new Fail(a + ' needs a value'); o[a.slice(2)] = argv[++i]; }
    else if (['--check', '--dry-run', '--no-fetch', '--skip-verify', '--no-checkout', '--commit', '--help'].includes(a)) o.flags.add(a.slice(2));
    else if (a.startsWith('-')) throw new Fail('unknown option ' + a);
    else if (o.commit) throw new Fail('only one upstream commit can be given');
    else o.commit = a;
  }
  if (o.flags.has('check') && (o.commit || o.flags.has('dry-run') || o.flags.has('commit'))) throw new Fail('--check takes no commit, --dry-run or --commit');
  if (o.flags.has('dry-run') && o.flags.has('commit')) throw new Fail('--dry-run and --commit exclude each other');
  return o;
}

const readJson = (p) => {
  try { return JSON.parse(readFileSync(p, 'utf8')); } catch (e) { throw new Fail('cannot read ' + p + ': ' + e.message); }
};
// "host/owner/repo" for https, ssh:// and scp-style (git@host:owner/repo) URLs, so an SSH clone of the cache is found too.
export const normUrl = (u) => u.trim().toLowerCase().replace(/\/+$/, '').replace(/\.git$/, '')
  .replace(/^[a-z+]+:\/\/(?:[^@/]+@)?/, '').replace(/^[^@/]+@([^:/]+):/, '$1/').replace(/^([^/:]+):\d+\//, '$1/');
const short = (sha) => sha.slice(0, 8);

export function run(argv, { log = console.log, err = console.error, defaultRepo } = {}) {
  let opts;
  try {
    opts = parseArgs(argv);
    if (opts.flags.has('help')) { log(USAGE); return 0; }
    return sync(opts, log, err, defaultRepo);
  } catch (e) {
    // Fail is ours; anything else (a git or file system error) is reported the same way, without a stack trace.
    err('sync failed: ' + (e instanceof Fail ? e.message : String(e.stderr ?? '').trim() || e.message));
    if (!opts) err(USAGE);
    return opts ? 1 : 2;
  }
}

function sync(opts, log, err, defaultRepo) {
  const has = (f) => opts.flags.has(f);
  const repo = path.resolve(opts.repo ?? defaultRepo);
  const upstreamDir = path.resolve(opts.upstream ?? path.join(repo, 'upstream'));
  const here = path.join(repo, 'tools', 'sync');
  const cfg = readJson(path.join(here, 'sync.json'));
  let tierMap, transforms, allow;
  try {
    tierMap = loadTierMap(readJson(path.join(here, 'tiers.json')));
    transforms = loadTransforms(readJson(path.join(here, 'transforms.json')));
    // Optional: with no allowlist, every hit fails.
    const allowPath = path.join(here, 'personal-data.json');
    allow = existsSync(allowPath) ? loadAllowlist(readJson(allowPath)) : [];
  } catch (e) { throw new Fail(e.message); }
  const lockPath = path.join(repo, 'UPSTREAM.lock');
  const lockText = readFileSync(lockPath, 'utf8');
  let lock;
  try { lock = parseLock(lockText); } catch (e) { throw new Fail(e.message); }
  const repoUrl = lock.repo.trim().replace(/\.git$/, '').replace(/\/+$/, '');
  const repoName = repoUrl.replace(/^[a-z]+:\/\/[^/]+\//i, '');
  // replaceTree deletes vendorDir, so it must be a dedicated folder strictly inside the repo.
  const vendorDir = typeof cfg.vendorDir === 'string' ? cfg.vendorDir : '';
  const vendorRoot = path.resolve(repo, ...vendorDir.split('/'));
  const relVendor = path.relative(repo, vendorRoot);
  if (!vendorDir || !relVendor || relVendor.startsWith('..') || path.isAbsolute(relVendor) || relVendor.split(path.sep).length < 2)
    throw new Fail('sync.json: vendorDir must be a folder at least two levels inside the repo (such as src/Upstream), not ' + JSON.stringify(cfg.vendorDir));
  if (!Array.isArray(cfg.watch) || !cfg.watch.every((w) => typeof w === 'string' && w.endsWith('/')))
    throw new Fail('sync.json: watch must be a list of upstream folders ending in "/"');
  if (typeof cfg.reportDir !== 'string' || !cfg.reportDir || path.isAbsolute(cfg.reportDir) || cfg.reportDir.split('/').includes('..'))
    throw new Fail('sync.json: reportDir must be a relative folder inside the repo');
  if (cfg.syntaxCheck !== undefined && typeof cfg.syntaxCheck !== 'boolean') throw new Fail('sync.json: syntaxCheck must be true or false');
  if (cfg.verify !== undefined && !(Array.isArray(cfg.verify) && cfg.verify.every((v) => v && typeof v.name === 'string' && typeof v.project === 'string')))
    throw new Fail('sync.json: verify must be a list of {"name", "project"}');

  if (!existsSync(path.join(upstreamDir, '.git'))) throw new Fail('no upstream cache at ' + upstreamDir + ' (README, "Getting started", clones it)');
  if (tryGit(upstreamDir, ['rev-parse', '--is-shallow-repository']) === 'true')
    throw new Fail('the upstream cache is a shallow clone; the vendored headers need full history (git fetch --unshallow)');
  const remote = [...remotes(upstreamDir)].find(([, url]) => normUrl(url) === normUrl(repoUrl))?.[0];
  const fetch = () => {
    if (!remote) throw new Fail('no remote in the upstream cache points at ' + repoUrl + '; add one, or pass --no-fetch');
    log('fetching ' + remote + ' in the upstream cache');
    git(upstreamDir, ['fetch', '--quiet', remote]);
  };
  const gen = (sha) => generate({ upstreamDir, commit: sha, tierMap, transforms, repoName, repoUrl, allow, syntaxCheck: cfg.syntaxCheck === true });
  const failWith = (head, errors) => { throw new Fail(head + '\n  ' + errors.join('\n  ')); };

  // --check: regenerate from the pinned commit and compare.
  if (has('check')) {
    let sha = revParse(upstreamDir, lock.baseline);
    if (!sha && !has('no-fetch')) { fetch(); sha = revParse(upstreamDir, lock.baseline); }
    if (!sha) throw new Fail('the commit in UPSTREAM.lock (' + lock.baseline + ') is not in the upstream cache');
    const g = gen(sha);
    if (g.errors.length) failWith('regeneration at ' + short(sha) + ' failed:', g.errors);
    const problems = compareTree(vendorRoot, g.files);
    if (problems.length) failWith(cfg.vendorDir + ' differs from a regeneration at ' + short(sha) + ' (' + problems.length +
      ' problems). Vendored files are never edited by hand: change the tools/sync rules and re-run the sync.', problems.slice(0, 100));
    log('check passed: ' + g.manifest.files.length + ' vendored files match upstream ' + short(sha) + ' after transforms');
    return 0;
  }

  if (!has('no-fetch')) fetch();
  const target = opts.commit ?? (remote ? remote + '/main' : null);
  if (!target) throw new Fail('no upstream commit given and no remote to take main from');
  const newSha = revParse(upstreamDir, target);
  if (!newSha) throw new Fail('not a commit in the upstream cache: ' + target);
  const oldSha = revParse(upstreamDir, lock.baseline);
  if (!oldSha) throw new Fail('the commit in UPSTREAM.lock (' + lock.baseline + ') is not in the upstream cache');
  if (!isAncestor(upstreamDir, oldSha, newSha)) throw new Fail(short(newSha) + ' does not descend from the baseline ' + short(oldSha));
  log('sync ' + short(oldSha) + ' → ' + short(newSha));

  const g = gen(newSha);
  const changes = pathChanges(tierMap, diffNameStatus(upstreamDir, oldSha, newSha), cfg.watch);
  const errors = [...g.errors];
  if (g.classified.errors.length) errors.push(...includeHints(tierMap, changes));
  if (errors.length) failWith('nothing was written:', errors);
  const unclassified = g.classified.unclassified.filter((p) => cfg.watch.some((r) => p.startsWith(r)));
  const commits = commitsBetween(upstreamDir, oldSha, newSha);
  const info = (sha) => {
    const [date, subject] = git(upstreamDir, ['log', '-1', '--format=%cs%x02%s', sha]).trim().split('\x02');
    return { sha, date, subject };
  };

  const temp = mkdtempSync(path.join(os.tmpdir(), 'isotope-sync-'));
  try {
    const tmpVendor = path.join(temp, 'vendor');
    writeTree(tmpVendor, g.files);
    let verify = null;
    if (!has('skip-verify')) {
      const v = runVerify(repo, cfg.verify ?? [], tmpVendor, log);
      if (v.errors.length) failWith('upstream tests for the vendored paths failed; nothing was written:', v.errors);
      verify = v.results;
    }
    const before = existsSync(path.join(vendorRoot, MANIFEST)) ? readJson(path.join(vendorRoot, MANIFEST)) : null;
    const delta = vendoredDelta(before, g.manifest);
    const report = renderReport({ repoUrl, old: info(oldSha), new: info(newSha), commits, changes, unclassified, delta,
      manifest: g.manifest, map: tierMap, verify });
    // A backstop: subjects are sanitised, but the report is committed, so it gets the same check as vendored files.
    const reportHits = [...new Map(scanText(report).map((h) => [h.line, h])).values()];
    if (reportHits.length) failWith('the sync report has personal data; nothing was written. Fix sanitizeSubject in tools/sync/lib/report.mjs:',
      reportHits.map((h) => 'report line ' + h.line + ' (' + h.kind + ')'));
    const summary = g.manifest.files.length + ' vendored files; ' + delta.changed.length + ' changed, ' + delta.added.length + ' added, ' +
      delta.removed.length + ' removed; ' + changes.length + ' paths to classify; ' + unclassified.length + ' unclassified';
    if (has('dry-run')) {
      log(report);
      log('dry run: ' + summary + '. Nothing was written.');
      return 0;
    }
    if (has('commit')) {
      // The commit must hold only what the sync writes, so refuse to start with other changes staged.
      const staged = git(repo, ['diff', '--cached', '--name-only']).trim();
      if (staged) throw new Fail('--commit needs an empty index, and these are staged:\n  ' + staged.split('\n').join('\n  '));
    }
    replaceTree(vendorRoot, g.files);
    const written = [cfg.vendorDir];
    if (oldSha !== newSha) {
      const reportRel = cfg.reportDir + '/' + short(oldSha) + '-' + short(newSha) + '.md';
      mkdirSync(path.join(repo, ...cfg.reportDir.split('/')), { recursive: true });
      writeFileSync(path.join(repo, ...reportRel.split('/')), report);
      const n = info(newSha);
      const pr = prNumber(n.subject);
      writeFileSync(lockPath, updateLock(lockText, {
        baseline: newSha, baseline_date: n.date,
        // No claim about the local cache: the lock is shared, the cache is per machine.
        baseline_note: pr ? 'PR #' + pr : 'commit ' + short(newSha),
      }));
      written.push('UPSTREAM.lock', reportRel);
    }
    // Last, so a failed write above never leaves the cache moved while the lock still names the old commit.
    if (!has('no-checkout')) {
      if (tryGit(upstreamDir, ['status', '--porcelain']) !== '') err('warning: the upstream cache has local changes, so it was not checked out at ' + short(newSha));
      else try { git(upstreamDir, ['checkout', '--quiet', '--detach', newSha]); } catch (e) {
        err('warning: could not check the upstream cache out at ' + short(newSha) + ': ' + String(e.stderr ?? e.message).trim());
      }
    }
    let committed = '';
    if (has('commit')) {
      git(repo, ['add', '--', ...written]);
      if (tryGit(repo, ['diff', '--cached', '--name-only']) === '') committed = ' (nothing changed, so nothing was committed)';
      else {
        git(repo, ['commit', '--quiet', '-m', oldSha === newSha ? 'sync upstream: regenerate at ' + short(newSha) : 'sync upstream ' + short(oldSha) + ' → ' + short(newSha), '--', ...written]);
        committed = ' (committed)';
      }
    }
    log('sync done: ' + summary + '. Wrote ' + written.join(', ') + committed + '.');
    return 0;
  } finally {
    rmSync(temp, { recursive: true, force: true });
  }
}
