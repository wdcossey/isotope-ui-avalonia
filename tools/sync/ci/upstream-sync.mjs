#!/usr/bin/env node
// The scheduled upstream sync (issues #5 and #36), run by .github/workflows/upstream-sync.yml in two phases, each in its
// own job, because the sync runs upstream's code (its Yoga tests, on the regenerated files) and that code must never
// share a job with a write token:
//   prepare  (read-only job): when upstream main has moved past UPSTREAM.lock, regenerates the vendored files with
//            tools/sync and commits them on a local branch "sync/<old>-<new>", then writes that one commit as a git bundle
//            with a small metadata file into a folder outside the checkout, for the next job. No GitHub access at all.
//   publish  (the job with the write token, which runs no upstream code and no dotnet): validates the bundle (the
//            folder came from a job that ran untrusted code), then pushes the branch, dispatches CI on it (a push made
//            with the job's token starts no workflow) and opens an issue with the sync report. --dry-run validates and
//            says what it would do, and writes nothing.
// With no command, both run in one process (a local run). When nothing moved, prepare writes nothing and there is
// nothing to publish. A failing sync (a transform that stops matching, a file to classify, a failing upstream test,
// personal data) fails the run, and nothing is pushed or opened. A branch or issue that already exists for the same
// range is not pushed or opened again, so a re-run finishes what a failed run started.
//
// Run each phase in a disposable checkout with a clean working tree; prepare needs the upstream cache in <repo>/upstream.
// Both phases leave the checkout as they found it. GitHub is reached through its REST API (GITHUB_TOKEN,
// GITHUB_REPOSITORY), so the runner needs no gh CLI.
// Tests: node --test tools/sync/ci/upstream-sync.test.mjs
import { execFileSync, spawnSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { normUrl } from '../lib/run.mjs';
import { parseLock } from '../lib/lock.mjs';

export const USAGE = [
  'Usage: node tools/sync/ci/upstream-sync.mjs [prepare|publish] [options]',
  '  (no command)      prepare, then publish, in one process',
  '  prepare           sync on a local branch and write the commit as a bundle into --out; push and open nothing',
  '  publish           validate the bundle in --bundle, then push the branch, dispatch CI and open the issue',
  '  --dry-run         (publish, or no command) validate and print what would be pushed and opened; write nothing',
  '  --no-fetch        (prepare, or no command) do not fetch the upstream cache first',
  '  --out <dir>       (prepare) the folder to write the bundle and its metadata to, outside the repo',
  '  --bundle <dir>    (publish) the folder prepare wrote',
  '  --repo <dir>      this repo (default: three levels above this script)',
  '  --upstream <dir>  (prepare, or no command) the upstream cache (default: <repo>/upstream)',
  'Environment: GITHUB_TOKEN and GITHUB_REPOSITORY (owner/name; needed to publish unless --dry-run), GITHUB_API_URL,',
  'GITHUB_SERVER_URL, GITHUB_RUN_ID, GITHUB_OUTPUT and GITHUB_STEP_SUMMARY (all set by GitHub Actions).',
].join('\n');

export class Fail extends Error {}

const short = (sha) => sha.slice(0, 8);
const ISSUE_LIMIT = 60000; // GitHub refuses issue bodies over 65536 characters
export const CI_WORKFLOW = 'ci.yml';

// The files prepare writes and publish reads.
export const BUNDLE = 'sync.bundle';
export const META = 'meta.json';
const META_KEYS = ['base', 'branch', 'commit', 'new', 'old', 'reportPath'];
// Where publish keeps the bundle's commit while it checks it, and until it is pushed.
const QUARANTINE = 'refs/isotope-sync/candidate';
const SHA = /^[0-9a-f]{40}$/;
const BRANCH = /^sync\/[0-9a-f]{8}-[0-9a-f]{8}$/;

const ALLOWED = {
  all: ['dry-run', 'no-fetch', 'repo', 'upstream'],
  prepare: ['no-fetch', 'repo', 'upstream', 'out'],
  publish: ['dry-run', 'repo', 'bundle'],
};

export function parseArgs(argv) {
  const o = { command: 'all', dryRun: false, fetch: true, help: false };
  let i = 0;
  if (argv[0] === 'prepare' || argv[0] === 'publish') o.command = argv[i++];
  const allowed = ALLOWED[o.command];
  for (; i < argv.length; i++) {
    const a = argv[i];
    const name = a.startsWith('--') ? a.slice(2) : null;
    if (a === '--help') { o.help = true; continue; }
    if (!name || !allowed.includes(name)) throw new Fail('unknown argument ' + a + (o.command === 'all' ? '' : ' for ' + o.command));
    if (name === 'dry-run') o.dryRun = true;
    else if (name === 'no-fetch') o.fetch = false;
    else {
      if (!argv[i + 1]) throw new Fail(a + ' needs a value');
      o[name] = argv[++i];
    }
  }
  if (!o.help && o.command === 'prepare' && !o.out) throw new Fail('prepare needs --out');
  if (!o.help && o.command === 'publish' && !o.bundle) throw new Fail('publish needs --bundle');
  return o;
}

function gitIn(dir) {
  return (...args) => execFileSync('git', ['-c', 'core.quotePath=false', ...args],
    { cwd: dir, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 1 << 28 }).trim();
}
const tryRun = (f) => { try { return f(); } catch { return null; } };

/** The GitHub REST API: api(method, path, body) resolves the parsed JSON (null for an empty reply). A GET that returns a
 * list follows its "Link: rel=next" pages and resolves them all, so a search of the issues sees every one. */
export function githubApi({ token, apiUrl = 'https://api.github.com', fetchImpl = fetch }) {
  const base = apiUrl.replace(/\/+$/, '');
  return async (method, p, body) => {
    let url = base + p;
    let all = null;
    for (;;) {
      const res = await fetchImpl(url, {
        method,
        headers: {
          Authorization: 'Bearer ' + token, Accept: 'application/vnd.github+json',
          'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'isotope-upstream-sync',
          ...(body ? { 'Content-Type': 'application/json' } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      const text = await res.text();
      if (!res.ok) throw new Fail(method + ' ' + p + ' failed: HTTP ' + res.status + ' ' + text.slice(0, 300));
      const data = text ? JSON.parse(text) : null;
      if (method !== 'GET' || !Array.isArray(data)) return data;
      all = (all ?? []).concat(data);
      const next = /<([^>]+)>;\s*rel="next"/.exec(res.headers.get('link') ?? '')?.[1];
      if (!next) return all;
      url = next;
    }
  };
}

// Code spans are left alone; outside them, whatever GitHub would turn into a reference or a mention is put in a code
// span: URLs other than this repo's (a link to an upstream PR records a cross-reference on it), "owner/repo#n",
// "owner/repo@sha", "#n" and "@name". A markdown link to another site keeps only its label.
export function neutralise(markdown, ownUrl) {
  const own = ownUrl ? ownUrl.replace(/\/+$/, '') + '/' : null;
  const isOwn = (url) => own !== null && (url + '/').startsWith(own);
  const wrap = (s) => (s.includes('\x60') ? '\x60\x60 ' + s + ' \x60\x60' : '\x60' + s + '\x60');
  // First, a link to another site becomes its label (code spans in a label included); code spans are skipped.
  const unlinked = markdown.replace(/(\x60+)[^]*?\1|\[((?:[^[\]\n\x60]|\x60[^\x60\n]*\x60)*)\]\((https?:\/\/[^)\s]+)\)/g,
    (m, ticks, label, url) => (ticks !== undefined || isOwn(url) ? m : label || url));
  // Then one pass, so nothing a replacement wrapped is matched again: a URL, or a reference or mention.
  const plain = (text) => text.replace(
    /(?<![\w\x60])(https?:\/\/[^\s<>()\x60|]*[^\s<>()\x60|.,;:!?'"])|(?<![\w\x60/])((?:[\w.-]+\/[\w.-]+)?[#@][A-Za-z0-9][\w.-]*)/g,
    (m, url, ref) => (url !== undefined ? (isOwn(url) ? url : wrap(url)) : wrap(ref)));
  // Split on code spans (a run of backticks up to the same run), which stay as they are.
  return unlinked.split(/(\x60+)([^]*?)(\1)/).reduce((acc, part, i, parts) => {
    const k = i % 4;
    if (k === 0) return acc + plain(part);
    if (k === 1) return acc + part + parts[i + 1] + parts[i + 2];
    return acc;
  }, '');
}

export function issueBody({ report, branch, base = 'main', reportPath, serverUrl, repoSlug, runUrl, ci, superseded = [] }) {
  const ownUrl = serverUrl + '/' + repoSlug;
  const L = [];
  L.push('Opened by the \x60upstream-sync\x60 workflow' + (runUrl ? ' ([run](' + runUrl + '))' : '') + '.', '');
  L.push('- **Branch:** [\x60' + branch + '\x60](' + ownUrl + '/tree/' + branch + '): one commit with the regenerated files, ' +
    '\x60UPSTREAM.lock\x60 and the report ([compare](' + ownUrl + '/compare/' + base + '...' + branch + ')).');
  L.push('- **CI:** ' + (ci.ok ? 'dispatched on the branch ([runs](' + ownUrl + '/actions/workflows/' + CI_WORKFLOW + '?query=branch%3A' +
    encodeURIComponent(branch) + ')).' : 'not dispatched (' + ci.error + '): run \x60ci\x60 on the branch by hand.'));
  L.push('- **Report:** [\x60' + reportPath + '\x60](' + ownUrl + '/blob/' + branch + '/' + reportPath + '). Its upstream links work ' +
    'there; here they are code, so this issue makes no cross-reference upstream.');
  if (superseded.length) L.push('- **Supersedes** the open ' + superseded.map((i) => '#' + i.number + ' (' + i.title.replace(/^Upstream sync /, '') + ')').join(', ') +
    ': close it once this one is merged.');
  L.push('', 'To finish: port what applies from the tier 3 PRs, check the new paths\' tiers, build and test the branch, then merge it.', '', '---', '');
  let body = neutralise(report, ownUrl);
  // Cut at a line end, so no code span is left open (the report's spans never cross a line).
  if (body.length > ISSUE_LIMIT) body = body.slice(0, body.lastIndexOf('\n', ISSUE_LIMIT) + 1) + '\n… (cut: the whole report is the file on the branch)\n';
  return L.join('\n') + '\n' + body;
}

function readLockText(text) {
  try { return parseLock(text); } catch (e) { throw new Fail(e.message); }
}

/**
 * The read-only phase. opts: { repo, upstream, fetch, syncScript, out }. When upstream main has moved past
 * UPSTREAM.lock, commits the sync on a local branch, writes that commit as <out>/sync.bundle (prerequisite: the
 * checkout's HEAD) with <out>/meta.json, and puts the checkout back as it was. Resolves { status: 'up-to-date' | 'built', ... }.
 * It runs upstream's code (tools/sync's verify step), so nothing it writes is trusted: publish checks it all.
 */
export async function prepare(opts, { log = console.log } = {}) {
  const repo = path.resolve(opts.repo);
  const upstream = path.resolve(opts.upstream ?? path.join(repo, 'upstream'));
  const out = path.resolve(opts.out);
  const git = gitIn(repo);
  const up = gitIn(upstream);
  const syncScript = opts.syncScript ?? path.join(repo, 'tools', 'sync', 'sync.mjs');
  const inside = path.relative(repo, out);
  if (!inside || (!inside.startsWith('..') && !path.isAbsolute(inside))) throw new Fail('--out must be outside the repo (the checkout is cleaned after the sync)');

  const lock = readLockText(readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8'));
  const baseline = lock.baseline;
  const remote = tryRun(() => up('remote', '-v'))?.split('\n')
    .map((l) => /^(\S+)\s+(\S+)\s+\(fetch\)$/.exec(l.trim())).find((m) => m && normUrl(m[2]) === normUrl(lock.repo))?.[1];
  if (!remote) throw new Fail('no remote in the upstream cache points at ' + lock.repo);
  if (opts.fetch) { log('fetching ' + remote + ' in the upstream cache'); up('fetch', '--quiet', remote); }
  const target = tryRun(() => up('rev-parse', '--verify', '--quiet', remote + '/main^{commit}'));
  if (!target) throw new Fail(remote + '/main is not in the upstream cache');

  if (target === baseline) {
    log('up to date: UPSTREAM.lock is upstream main (' + short(target) + '); nothing to do');
    return { status: 'up-to-date', old: baseline, new: target };
  }
  const range = short(baseline) + '-' + short(target);
  const branch = 'sync/' + range;
  const cfg = JSON.parse(readFileSync(path.join(repo, 'tools', 'sync', 'sync.json'), 'utf8'));
  const reportPath = cfg.reportDir + '/' + range + '.md';
  log('upstream moved: ' + short(baseline) + ' → ' + short(target) + '; branch ' + branch);

  if (git('status', '--porcelain') !== '') throw new Fail('the working tree is not clean; run in a fresh checkout');
  const base = git('rev-parse', 'HEAD');
  // The branch to return to, or the commit when HEAD is detached.
  const start = tryRun(() => git('symbolic-ref', '--quiet', '--short', 'HEAD')) || base;
  try {
    git('checkout', '--quiet', '-b', branch);
    const sync = spawnSync(process.execPath, [syncScript, target, '--commit', '--no-fetch', '--no-checkout', '--repo', repo, '--upstream', upstream],
      { encoding: 'utf8', maxBuffer: 1 << 28 });
    if (sync.stdout) log(sync.stdout.trimEnd());
    if (sync.status !== 0)
      throw new Fail('tools/sync failed (exit ' + sync.status + '); nothing was pushed or opened:\n' + (sync.stderr || sync.error?.message || '').trimEnd());
    if (sync.stderr) log(sync.stderr.trimEnd()); // its warnings
    const commit = git('rev-parse', 'HEAD');
    if (commit === base) throw new Fail('tools/sync committed nothing');
    const report = readFileSync(path.join(repo, ...reportPath.split('/')), 'utf8');
    mkdirSync(out, { recursive: true });
    git('bundle', 'create', '--quiet', path.join(out, BUNDLE), '^' + base, 'refs/heads/' + branch);
    const meta = { base, branch, commit, new: target, old: baseline, reportPath };
    writeFileSync(path.join(out, META), JSON.stringify(meta, null, 2) + '\n');
    log('built ' + branch + ' (' + short(commit) + ' on ' + short(base) + '): ' + path.join(out, BUNDLE));
    return { status: 'built', ...meta, report };
  } finally {
    // The tree was clean, so whatever the sync wrote goes (ignored files such as upstream/ stay), and so does the
    // local branch: the bundle holds the commit.
    tryRun(() => git('reset', '--quiet', '--hard'));
    tryRun(() => git('clean', '--quiet', '-fd'));
    tryRun(() => git('checkout', '--quiet', start));
    tryRun(() => git('branch', '--quiet', '-D', branch));
  }
}

/**
 * Checks what prepare wrote into dir before anything is pushed, with nothing but this checkout as the reference: a job
 * that ran upstream's code made it. Fetches the commit into a quarantine ref and resolves { branch, old, new, commit,
 * base, reportPath, report }; anything else throws a Fail and leaves no ref behind.
 *
 * The rules: the metadata has exactly its keys and names this checkout's baseline and HEAD; the bundle holds one head,
 * refs/heads/sync/<old8>-<new8>, at that commit, and git's fsck passes it; the commit is the only one on HEAD, made by
 * the identity this job commits with, with the sync's own message; it changes only the vendored tree, UPSTREAM.lock
 * (baseline, baseline_date and baseline_note, to the new commit) and the range's report, all as regular files.
 * The vendored bytes themselves are not checked here (that needs dotnet): CI's "tools/sync --check" on the branch
 * regenerates them from the lock with main's transform rules, which this commit can't touch.
 */
export function verifyBundle({ repo, dir }) {
  const git = gitIn(repo);
  const refuse = (why) => { throw new Fail('refused the sync bundle: ' + why); };
  const head = git('rev-parse', 'HEAD');
  const lockText = git('show', head + ':UPSTREAM.lock');
  const lock = readLockText(lockText);
  const cfg = JSON.parse(git('show', head + ':tools/sync/sync.json'));

  let meta;
  try { meta = JSON.parse(readFileSync(path.join(dir, META), 'utf8')); } catch (e) { refuse('no readable ' + META + ': ' + e.message.split('\n')[0]); }
  if (!meta || typeof meta !== 'object' || Array.isArray(meta) || Object.keys(meta).sort().join() !== META_KEYS.join())
    refuse(META + ' must have exactly the keys ' + META_KEYS.join(', '));
  for (const k of ['base', 'commit', 'new', 'old']) if (typeof meta[k] !== 'string' || !SHA.test(meta[k])) refuse(META + ': ' + k + ' is not a 40-character commit sha');
  if (meta.old !== lock.baseline) refuse(META + ': old ' + short(meta.old) + ' is not the baseline in UPSTREAM.lock (' + short(lock.baseline) + ')');
  if (meta.new === meta.old) refuse(META + ': new is old');
  if (meta.base !== head) refuse(META + ': base ' + short(meta.base) + ' is not this checkout\'s HEAD (' + short(head) + ')');
  const range = short(meta.old) + '-' + short(meta.new);
  const branch = 'sync/' + range;
  if (typeof meta.branch !== 'string' || meta.branch !== branch || !BRANCH.test(branch)) refuse(META + ': branch is not ' + branch);
  const reportPath = cfg.reportDir + '/' + range + '.md';
  if (meta.reportPath !== reportPath) refuse(META + ': reportPath is not ' + reportPath);

  const file = path.join(dir, BUNDLE);
  if (!existsSync(file)) refuse('no ' + BUNDLE);
  const why = (e) => String(e.stderr || e.message).trim().split('\n').join(' ');
  try { git('bundle', 'verify', file); } catch (e) { refuse('git bundle verify: ' + why(e)); }
  const heads = git('bundle', 'list-heads', file).split('\n').filter(Boolean);
  if (heads.length !== 1 || heads[0] !== meta.commit + ' refs/heads/' + branch)
    refuse('it must hold one head, refs/heads/' + branch + ' at ' + short(meta.commit) + '; it holds ' + (heads.join(', ') || 'none'));
  // fsck refuses what git itself should never hold (a ".git" or ".." path, a malformed object).
  try { git('-c', 'fetch.fsckObjects=true', 'fetch', '--quiet', '--no-tags', '--no-write-fetch-head', file, '+refs/heads/' + branch + ':' + QUARANTINE); }
  catch (e) { refuse('git fetch (with fsck): ' + why(e)); }
  try {
    const commit = git('rev-parse', '--verify', '--quiet', QUARANTINE + '^{commit}');
    if (commit !== meta.commit) refuse('its head is not ' + short(meta.commit));
    const parents = git('rev-list', '--parents', '-n', '1', commit).split(' ').slice(1);
    if (parents.length !== 1 || parents[0] !== head)
      refuse('the commit must have one parent, this checkout\'s HEAD ' + short(head) + '; it has ' + (parents.map(short).join(', ') || 'none'));
    const [an, ae, cn, ce, message] = git('log', '-1', '--format=%an%x00%ae%x00%cn%x00%ce%x00%B', commit).split('\0');
    // tools/sync's --commit message (tools/sync/lib/run.mjs), with nothing added.
    const want = 'sync upstream ' + short(meta.old) + ' → ' + short(meta.new);
    if (message.trim() !== want) refuse('the commit message is not "' + want + '"');
    const ident = (v) => { const m = /^(.*) <(.*)> \d+ [+-]\d{4}$/.exec(git('var', v)); return m ? m[1] + ' <' + m[2] + '>' : null; };
    if (an + ' <' + ae + '>' !== ident('GIT_AUTHOR_IDENT') || cn + ' <' + ce + '>' !== ident('GIT_COMMITTER_IDENT'))
      refuse('the commit was not made by the identity this job commits with');

    const tokens = git('diff-tree', '-r', '-z', '--no-renames', '--raw', head, commit).split('\0');
    const entries = [];
    for (let i = 0; i + 1 < tokens.length; i += 2) {
      const m = /^:\d{6} (\d{6}) [0-9a-f]+ [0-9a-f]+ ([A-Z])$/.exec(tokens[i]);
      if (!m) refuse('unreadable diff line ' + JSON.stringify(tokens[i]));
      entries.push({ mode: m[1], status: m[2], path: tokens[i + 1] });
    }
    for (const e of entries) {
      if (e.path === '.github' || e.path.startsWith('.github/')) refuse('it changes ' + e.path + ': nothing under .github/ may change');
      const ok = e.path.startsWith(cfg.vendorDir + '/') || (e.path === 'UPSTREAM.lock' && e.status === 'M') ||
        (e.path === reportPath && (e.status === 'A' || e.status === 'M'));
      if (!ok) refuse('it changes ' + e.path + ' (' + e.status + '); only ' + cfg.vendorDir + '/, UPSTREAM.lock and ' + reportPath + ' may change');
      if (e.status !== 'D' && e.mode !== '100644' && e.mode !== '100755') refuse(e.path + ' is not a regular file (mode ' + e.mode + ')');
    }
    if (!entries.some((e) => e.path === 'UPSTREAM.lock')) refuse('it does not update UPSTREAM.lock');
    if (!entries.some((e) => e.path === reportPath)) refuse('it has no report at ' + reportPath);

    const newLockText = git('show', commit + ':UPSTREAM.lock');
    let newLock;
    try { newLock = parseLock(newLockText); } catch (e) { refuse(e.message); }
    if (newLock.baseline !== meta.new) refuse('UPSTREAM.lock: baseline is not ' + short(meta.new));
    if (!/^\d{4}-\d{2}-\d{2}$/.test(newLock.baseline_date ?? '') || !/^(PR #\d+|commit [0-9a-f]{8})$/.test(newLock.baseline_note ?? ''))
      refuse('UPSTREAM.lock: baseline_date or baseline_note is not what tools/sync writes');
    const rest = (t) => t.replace(/\r\n/g, '\n').split('\n').filter((l) => !/^(baseline|baseline_date|baseline_note)=/.test(l)).join('\n').trim();
    if (rest(newLockText) !== rest(lockText)) refuse('UPSTREAM.lock: lines other than baseline, baseline_date and baseline_note changed');

    const report = git('show', commit + ':' + reportPath) + '\n';
    return { branch, old: meta.old, new: meta.new, commit, base: head, reportPath, report };
  } catch (e) {
    tryRun(() => git('update-ref', '-d', QUARANTINE));
    throw e;
  }
}

/**
 * The write phase. opts: { repo, bundle, dryRun, repoSlug, serverUrl, runUrl }. deps: { api (null: no GitHub access),
 * log }. Checks the bundle (verifyBundle), then pushes its commit, dispatches CI and opens the issue, unless the range
 * is done already. Runs no upstream code. Resolves { status: 'exists' | 'dry-run' | 'opened', ... }.
 */
export async function publish(opts, { api = null, log = console.log } = {}) {
  const repo = path.resolve(opts.repo);
  const git = gitIn(repo);
  if (!opts.dryRun && !api) throw new Fail('a real run needs GITHUB_TOKEN and GITHUB_REPOSITORY (or pass --dry-run)');
  const c = verifyBundle({ repo, dir: path.resolve(opts.bundle) });
  try {
    const { branch, commit } = c;
    log('the sync bundle is valid: ' + branch + ' at ' + short(commit) + ', one commit on ' + short(c.base));
    const title = 'Upstream sync ' + short(c.old) + ' → ' + short(c.new);
    const onRemote = git('ls-remote', '--heads', 'origin', 'refs/heads/' + branch) !== '';
    let issues = null;
    if (api) {
      const page = await api('GET', '/repos/' + opts.repoSlug + '/issues?state=all&per_page=100');
      issues = page.filter((i) => !i.pull_request && typeof i.title === 'string');
    } else log('GitHub not checked (no GITHUB_TOKEN): an existing issue for this range would not be seen');
    const existing = issues?.find((i) => i.title === title);
    // An issue for the range, open or closed, means it was handled: a closed one whose branch is gone was turned down,
    // and is not opened again. A branch with no issue is a run that failed after its push: open the issue now.
    const plan = existing
      ? 'do nothing: issue #' + existing.number + ' (' + existing.state + ') is for this range' + (onRemote ? ', and ' + branch + ' is pushed' : '')
      : (onRemote ? 'open the issue only (' + branch + ' is already pushed)' : 'push ' + branch) + ', dispatch ' + CI_WORKFLOW + ' on it and open the issue "' + title + '"';
    if (existing && !opts.dryRun) {
      log('already done: ' + plan.replace(/^do nothing: /, '') + '; nothing to do');
      return { status: 'exists', branch, issue: existing.number, issueState: existing.state, pushed: onRemote, old: c.old, new: c.new };
    }
    // A pushed branch is what the issue describes, so its report is read from there; a dry run shows this sync's.
    let report = c.report;
    if (onRemote && !opts.dryRun) {
      log('branch ' + branch + ' is already pushed; reading its report');
      git('fetch', '--quiet', 'origin', 'refs/heads/' + branch);
      report = git('show', 'FETCH_HEAD:' + c.reportPath) + '\n';
    }
    // The compare link's base: the branch this checkout is on, or its commit.
    const start = tryRun(() => git('symbolic-ref', '--quiet', '--short', 'HEAD')) || c.base;
    const superseded = (issues ?? []).filter((i) => i.state === 'open' && i.title !== title && i.title.startsWith('Upstream sync '));
    const bodyFor = (ci) => issueBody({ report, branch, base: start, reportPath: c.reportPath, serverUrl: opts.serverUrl, repoSlug: opts.repoSlug, runUrl: opts.runUrl, ci, superseded });
    if (opts.dryRun) {
      log('dry run: a real run would ' + plan + (superseded.length && !existing ? ' (superseding #' + superseded.map((i) => i.number).join(', #') + ')' : ''));
      return { status: 'dry-run', branch, title, plan, body: bodyFor({ ok: true }), old: c.old, new: c.new };
    }

    if (!onRemote) {
      // The checked commit itself, not a ref name.
      git('push', '--quiet', 'origin', commit + ':refs/heads/' + branch);
      log('pushed ' + branch);
    }
    let ci = { ok: true };
    try { await api('POST', '/repos/' + opts.repoSlug + '/actions/workflows/' + CI_WORKFLOW + '/dispatches', { ref: branch }); }
    catch (e) { ci = { ok: false, error: e.message.split('\n')[0] }; log('warning: could not dispatch ' + CI_WORKFLOW + ': ' + ci.error); }
    const issue = await api('POST', '/repos/' + opts.repoSlug + '/issues', { title, body: bodyFor(ci) });
    log('opened issue #' + issue.number + ': ' + title);
    return { status: 'opened', branch, issue: issue.number, ci: ci.ok, old: c.old, new: c.new };
  } finally {
    tryRun(() => git('update-ref', '-d', QUARANTINE));
  }
}

/**
 * Both phases in one process (a local run, and most tests): prepare into a temporary folder, then publish from it.
 * opts: { repo, upstream, dryRun, fetch, syncScript, repoSlug, serverUrl, runUrl }. deps: { api, log }.
 */
export async function runJob(opts, { api = null, log = console.log } = {}) {
  if (!opts.dryRun && !api) throw new Fail('a real run needs GITHUB_TOKEN and GITHUB_REPOSITORY (or pass --dry-run)');
  const out = mkdtempSync(path.join(os.tmpdir(), 'isotope-sync-bundle-'));
  try {
    const p = await prepare({ repo: opts.repo, upstream: opts.upstream, fetch: opts.fetch, syncScript: opts.syncScript, out }, { log });
    if (p.status === 'up-to-date') return p;
    return await publish({ repo: opts.repo, bundle: out, dryRun: opts.dryRun, repoSlug: opts.repoSlug, serverUrl: opts.serverUrl, runUrl: opts.runUrl }, { api, log });
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
}

export function summarise(r) {
  const head = {
    'up-to-date': 'Upstream main is the commit in UPSTREAM.lock (' + short(r.new) + '): nothing to do.',
    built: 'Upstream moved ' + short(r.old) + ' → ' + short(r.new) + ': the sync commit for \x60' + r.branch + '\x60 is built and handed to the publish job, which checks it.',
    exists: 'Already done: #' + r.issue + ' (' + r.issueState + ') is for this range' + (r.pushed ? ', and \x60' + r.branch + '\x60 is pushed' : ', and its branch is gone') + '.',
    'dry-run': 'Dry run: upstream moved ' + short(r.old) + ' → ' + short(r.new) + '. Nothing was pushed or opened.',
    opened: 'Upstream moved ' + short(r.old) + ' → ' + short(r.new) + ': pushed \x60' + r.branch + '\x60 and opened #' + r.issue + '.' +
      (r.ci ? '' : ' CI could not be dispatched on it.'),
  }[r.status];
  return '## Upstream sync\n\n' + head + '\n' + (r.body ? '\nThe issue it would open, "' + r.title + '":\n\n' + r.body : '');
}

async function main(argv, env) {
  let o;
  try { o = parseArgs(argv); } catch (e) { console.error(e.message + '\n' + USAGE); return 2; }
  if (o.help) { console.log(USAGE); return 0; }
  const here = path.dirname(fileURLToPath(import.meta.url));
  const serverUrl = (env.GITHUB_SERVER_URL || 'https://github.com').replace(/\/+$/, '');
  const repoSlug = env.GITHUB_REPOSITORY;
  const repo = o.repo ?? path.join(here, '..', '..', '..');
  const syncScript = path.join(here, '..', 'sync.mjs');
  const api = env.GITHUB_TOKEN && repoSlug ? githubApi({ token: env.GITHUB_TOKEN, apiUrl: env.GITHUB_API_URL }) : null;
  const where = { repoSlug, serverUrl, runUrl: repoSlug && env.GITHUB_RUN_ID ? serverUrl + '/' + repoSlug + '/actions/runs/' + env.GITHUB_RUN_ID : null };
  try {
    const r = o.command === 'prepare'
      ? await prepare({ repo, upstream: o.upstream, syncScript, fetch: o.fetch, out: o.out })
      : o.command === 'publish'
        ? await publish({ repo, bundle: o.bundle, dryRun: o.dryRun, ...where }, { api })
        : await runJob({ repo, upstream: o.upstream, syncScript, dryRun: o.dryRun, fetch: o.fetch, ...where }, { api });
    if (r.body) console.log('issue "' + r.title + '":\n' + r.body);
    if (env.GITHUB_OUTPUT) appendFileSync(env.GITHUB_OUTPUT, 'status=' + r.status + '\nbranch=' + (r.branch ?? '') + '\nissue=' + (r.issue ?? '') + '\n');
    if (env.GITHUB_STEP_SUMMARY) appendFileSync(env.GITHUB_STEP_SUMMARY, summarise(r));
    return 0;
  } catch (e) {
    const msg = e instanceof Fail ? e.message : String(e.stderr ?? '').trim() || e.message;
    console.error('upstream sync failed: ' + msg);
    if (env.GITHUB_STEP_SUMMARY) appendFileSync(env.GITHUB_STEP_SUMMARY, '## Upstream sync failed\n\n\x60\x60\x60\n' + msg + '\n\x60\x60\x60\n');
    return 1;
  }
}

// Run as a command (not imported by the tests). Compared as real paths, case-folded on Windows, so a junction or a drive
// letter in another case still runs it.
const canonical = (p) => { try { const r = realpathSync.native(p); return process.platform === 'win32' ? r.toLowerCase() : r; } catch { return null; } };
const invoked = process.argv[1] ? canonical(process.argv[1]) : null;
if (invoked && invoked === canonical(fileURLToPath(import.meta.url))) process.exitCode = await main(process.argv.slice(2), process.env);
