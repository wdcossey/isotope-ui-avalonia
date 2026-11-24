#!/usr/bin/env node
// The scheduled upstream sync (issue #5), run by .github/workflows/upstream-sync.yml. When upstream main has moved past
// UPSTREAM.lock it regenerates the vendored files with tools/sync on a branch "sync/<old>-<new>", pushes that branch,
// dispatches CI on it (a push made with the job's token starts no workflow) and opens an issue with the sync report.
// When nothing moved it writes nothing. A failing sync (a transform that stops matching, a file to classify, a failing
// upstream test, personal data) fails the run, and nothing is pushed or opened. A branch or issue that already exists
// for the same range is not pushed or opened again, so a re-run finishes what a failed run started.
//
// Run it in a disposable checkout with a clean working tree and the upstream cache in <repo>/upstream (CI does).
// --dry-run does the sync on a local branch, prints what it would push and open, then returns to the starting branch.
// GitHub is reached through its REST API (GITHUB_TOKEN, GITHUB_REPOSITORY), so the runner needs no gh CLI.
// Tests: node --test tools/sync/ci/upstream-sync.test.mjs
import { execFileSync, spawnSync } from 'node:child_process';
import { appendFileSync, readFileSync, realpathSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { normUrl } from '../lib/run.mjs';

export const USAGE = [
  'Usage: node tools/sync/ci/upstream-sync.mjs [options]',
  '  --dry-run         sync on a local branch and print what would be pushed and opened; push and open nothing',
  '  --no-fetch        do not fetch the upstream cache first',
  '  --repo <dir>      this repo (default: three levels above this script)',
  '  --upstream <dir>  the upstream cache (default: <repo>/upstream)',
  'Environment: GITHUB_TOKEN and GITHUB_REPOSITORY (owner/name; needed unless --dry-run), GITHUB_API_URL,',
  'GITHUB_SERVER_URL, GITHUB_RUN_ID, GITHUB_OUTPUT and GITHUB_STEP_SUMMARY (all set by GitHub Actions).',
].join('\n');

export class Fail extends Error {}

const short = (sha) => sha.slice(0, 8);
const ISSUE_LIMIT = 60000; // GitHub refuses issue bodies over 65536 characters
export const CI_WORKFLOW = 'ci.yml';

export function parseArgs(argv) {
  const o = { dryRun: false, fetch: true, help: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--dry-run') o.dryRun = true;
    else if (a === '--no-fetch') o.fetch = false;
    else if (a === '--help') o.help = true;
    else if (a === '--repo' || a === '--upstream') {
      if (!argv[i + 1]) throw new Fail(a + ' needs a value');
      o[a.slice(2)] = argv[++i];
    } else throw new Fail('unknown argument ' + a);
  }
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

/**
 * Runs the job. opts: { repo, upstream, dryRun, fetch, syncScript, repoSlug, serverUrl, runUrl }.
 * deps: { api (null: no GitHub access), log, env }. Resolves { status, branch?, issue? }.
 */
export async function runJob(opts, { api = null, log = console.log } = {}) {
  const repo = path.resolve(opts.repo);
  const upstream = path.resolve(opts.upstream ?? path.join(repo, 'upstream'));
  const git = gitIn(repo);
  const up = gitIn(upstream);
  const syncScript = opts.syncScript ?? path.join(repo, 'tools', 'sync', 'sync.mjs');

  const lockText = readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8');
  const field = (k) => new RegExp('^' + k + '=(.*)$', 'm').exec(lockText)?.[1].trim();
  const baseline = field('baseline');
  const upstreamUrl = field('repo');
  if (!/^[0-9a-f]{40}$/.test(baseline ?? '') || !upstreamUrl) throw new Fail('UPSTREAM.lock needs repo= and a 40-character baseline=');
  const remote = tryRun(() => up('remote', '-v'))?.split('\n')
    .map((l) => /^(\S+)\s+(\S+)\s+\(fetch\)$/.exec(l.trim())).find((m) => m && normUrl(m[2]) === normUrl(upstreamUrl))?.[1];
  if (!remote) throw new Fail('no remote in the upstream cache points at ' + upstreamUrl);
  if (opts.fetch) { log('fetching ' + remote + ' in the upstream cache'); up('fetch', '--quiet', remote); }
  const target = tryRun(() => up('rev-parse', '--verify', '--quiet', remote + '/main^{commit}'));
  if (!target) throw new Fail(remote + '/main is not in the upstream cache');

  if (target === baseline) {
    log('up to date: UPSTREAM.lock is upstream main (' + short(target) + '); nothing to do');
    return { status: 'up-to-date', old: baseline, new: target };
  }
  const range = short(baseline) + '-' + short(target);
  const branch = 'sync/' + range;
  const title = 'Upstream sync ' + short(baseline) + ' → ' + short(target);
  const cfg = JSON.parse(readFileSync(path.join(repo, 'tools', 'sync', 'sync.json'), 'utf8'));
  const reportPath = cfg.reportDir + '/' + range + '.md';
  log('upstream moved: ' + short(baseline) + ' → ' + short(target) + '; branch ' + branch);

  if (!opts.dryRun && !api) throw new Fail('a real run needs GITHUB_TOKEN and GITHUB_REPOSITORY (or pass --dry-run)');
  const onRemote = git('ls-remote', '--heads', 'origin', 'refs/heads/' + branch) !== '';
  let issues = null;
  if (api) {
    const page = await api('GET', '/repos/' + opts.repoSlug + '/issues?state=all&per_page=100');
    issues = page.filter((i) => !i.pull_request && typeof i.title === 'string');
  } else log('GitHub not checked (no GITHUB_TOKEN): an existing issue for this range would not be seen');
  const existing = issues?.find((i) => i.title === title);
  // An issue for the range, open or closed, means it was handled: a closed one whose branch is gone was turned down,
  // and is not opened again. A branch with no issue is a run that failed after its push: open the issue now.
  // A dry run notes what a real run would skip, but always runs the sync: testing it is what a dry run is for.
  const plan = existing
    ? 'do nothing: issue #' + existing.number + ' (' + existing.state + ') is for this range' + (onRemote ? ', and ' + branch + ' is pushed' : '')
    : (onRemote ? 'open the issue only (' + branch + ' is already pushed)' : 'push ' + branch) + ', dispatch ' + CI_WORKFLOW + ' on it and open the issue "' + title + '"';
  if (existing && !opts.dryRun) {
    log('already done: ' + plan.replace(/^do nothing: /, '') + '; nothing to do');
    return { status: 'exists', branch, issue: existing.number, issueState: existing.state, pushed: onRemote, old: baseline, new: target };
  }

  // The branch to return to after a dry run, or the commit when HEAD is detached.
  const start = tryRun(() => git('symbolic-ref', '--quiet', '--short', 'HEAD')) || git('rev-parse', 'HEAD');
  const build = !onRemote || opts.dryRun;
  let pushed = false;
  try {
    let report;
    if (build) {
      if (git('status', '--porcelain') !== '') throw new Fail('the working tree is not clean; run in a fresh checkout');
      git('checkout', '--quiet', '-b', branch);
      const sync = spawnSync(process.execPath, [syncScript, target, '--commit', '--no-fetch', '--no-checkout', '--repo', repo, '--upstream', upstream],
        { encoding: 'utf8', maxBuffer: 1 << 28 });
      if (sync.stdout) log(sync.stdout.trimEnd());
      if (sync.status !== 0)
        throw new Fail('tools/sync failed (exit ' + sync.status + '); nothing was pushed or opened:\n' + (sync.stderr || sync.error?.message || '').trimEnd());
      if (sync.stderr) log(sync.stderr.trimEnd()); // its warnings
      report = readFileSync(path.join(repo, ...reportPath.split('/')), 'utf8');
    } else {
      log('branch ' + branch + ' is already pushed; reading its report');
      git('fetch', '--quiet', 'origin', 'refs/heads/' + branch);
      report = git('show', 'FETCH_HEAD:' + reportPath) + '\n';
    }

    const superseded = (issues ?? []).filter((i) => i.state === 'open' && i.title !== title && i.title.startsWith('Upstream sync '));
    const bodyFor = (ci) => issueBody({ report, branch, base: start, reportPath, serverUrl: opts.serverUrl, repoSlug: opts.repoSlug, runUrl: opts.runUrl, ci, superseded });
    if (opts.dryRun) {
      log('dry run: a real run would ' + plan + (superseded.length && !existing ? ' (superseding #' + superseded.map((i) => i.number).join(', #') + ')' : ''));
      return { status: 'dry-run', branch, title, plan, body: bodyFor({ ok: true }), old: baseline, new: target };
    }

    if (!onRemote) {
      git('push', '--quiet', 'origin', 'refs/heads/' + branch + ':refs/heads/' + branch);
      log('pushed ' + branch);
    }
    pushed = true;
    let ci = { ok: true };
    try { await api('POST', '/repos/' + opts.repoSlug + '/actions/workflows/' + CI_WORKFLOW + '/dispatches', { ref: branch }); }
    catch (e) { ci = { ok: false, error: e.message.split('\n')[0] }; log('warning: could not dispatch ' + CI_WORKFLOW + ': ' + ci.error); }
    const issue = await api('POST', '/repos/' + opts.repoSlug + '/issues', { title, body: bodyFor(ci) });
    log('opened issue #' + issue.number + ': ' + title);
    return { status: 'opened', branch, issue: issue.number, ci: ci.ok, old: baseline, new: target };
  } finally {
    // A dry run, or a real run that stopped before its push, leaves the checkout as it found it: the tree was clean, so
    // whatever the sync wrote goes (ignored files such as upstream/ stay), and so does the local branch.
    if (build && !pushed) {
      tryRun(() => git('reset', '--quiet', '--hard'));
      tryRun(() => git('clean', '--quiet', '-fd'));
      tryRun(() => git('checkout', '--quiet', start));
      tryRun(() => git('branch', '--quiet', '-D', branch));
    }
  }
}

export function summarise(r) {
  const head = {
    'up-to-date': 'Upstream main is the commit in UPSTREAM.lock (' + short(r.new) + '): nothing to do.',
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
  const api = env.GITHUB_TOKEN && repoSlug ? githubApi({ token: env.GITHUB_TOKEN, apiUrl: env.GITHUB_API_URL }) : null;
  try {
    const r = await runJob({
      repo: o.repo ?? path.join(here, '..', '..', '..'), upstream: o.upstream, syncScript: path.join(here, '..', 'sync.mjs'), dryRun: o.dryRun, fetch: o.fetch, repoSlug, serverUrl,
      runUrl: repoSlug && env.GITHUB_RUN_ID ? serverUrl + '/' + repoSlug + '/actions/runs/' + env.GITHUB_RUN_ID : null,
    }, { api });
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
