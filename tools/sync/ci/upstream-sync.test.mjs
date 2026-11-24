// Tests of the scheduled sync's glue (upstream-sync.mjs): node --test tools/sync/ci/upstream-sync.test.mjs
// Each test builds a fixture: an upstream cache with a remote-tracking main, our repo with the sync settings and a bare
// "origin", and a fake GitHub API. The real tools/sync runs on it.
import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, existsSync, symlinkSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runJob, plan, prepare, publish, verifyBundle, badPath, BUNDLE, META, neutralise, issueBody, parseArgs, githubApi, summarise, Fail } from './upstream-sync.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const SCRIPT = path.join(here, 'upstream-sync.mjs');
const UPSTREAM_URL = 'https://example.invalid/upstream';
const G = ['-c', 'core.autocrlf=false', '-c', 'commit.gpgsign=false', '-c', 'init.defaultBranch=main'];
const SERVER = 'https://github.example.invalid';
const SLUG = 'owner/repo';

const FILES = {
  'LICENSE': 'MIT\n',
  'src/Lib/Yoga/Node.cs': 'namespace Y;\nclass Node {}\n',
  'src/Lib/Core/Engine.cs': 'class Engine { Microsoft.UI.Xaml.UIElement e; }\n',
  'src/Lib/Elements/Button.cs': 'class Button {}\n',
};
const TIERS = { rules: [
  { path: 'LICENSE', tier: 1 },
  { glob: 'src/Lib/Yoga/*.cs', tier: 1 },
  { path: 'src/Lib/Core/Engine.cs', tier: 2 },
  { glob: 'src/Lib/Elements/**', tier: 3 },
] };
const TRANSFORMS = { rules: [{ id: 'drop-ns', find: 'Microsoft\\.UI\\.Xaml\\.(UIElement)\\b', replace: '$1', files: ['src/Lib/Core/Engine.cs'] }] };

function fixture() {
  const root = mkdtempSync(path.join(os.tmpdir(), 'isotope-sync-job-test-'));
  const up = path.join(root, 'up');
  const repo = path.join(root, 'repo');
  const origin = path.join(root, 'origin.git');
  const git = (dir, ...a) => execFileSync('git', [...G, ...a], { cwd: dir, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const put = (base, rel, content) => {
    const p = path.join(base, ...rel.split('/'));
    mkdirSync(path.dirname(p), { recursive: true });
    writeFileSync(p, content);
  };
  const init = (dir) => {
    mkdirSync(dir, { recursive: true });
    git(dir, 'init', '-q');
    // Our own identity and settings, so the tests don't depend on the machine's git config.
    for (const [k, v] of [['user.name', 'sync-fixture'], ['user.email', 'sync-fixture@example.invalid'], ['commit.gpgsign', 'false'], ['core.autocrlf', 'false']])
      git(dir, 'config', k, v);
  };

  init(up);
  git(up, 'remote', 'add', 'upstream-remote', UPSTREAM_URL);
  /** Commits to upstream and moves its remote-tracking main there, as a fetch would. */
  const advance = (changes, message) => {
    for (const [rel, content] of Object.entries(changes)) put(up, rel, content);
    git(up, 'add', '-A');
    git(up, 'commit', '-q', '-m', message);
    const sha = git(up, 'rev-parse', 'HEAD');
    git(up, 'update-ref', 'refs/remotes/upstream-remote/main', sha);
    return sha;
  };
  const base = advance(FILES, 'Initial import (#1)');

  git(root, 'init', '-q', '--bare', origin);
  init(repo);
  put(repo, 'tools/sync/sync.json', JSON.stringify({ vendorDir: 'src/Upstream', reportDir: 'docs/sync', watch: ['src/'], verify: [] }));
  put(repo, 'tools/sync/tiers.json', JSON.stringify(TIERS));
  put(repo, 'tools/sync/transforms.json', JSON.stringify(TRANSFORMS));
  put(repo, 'UPSTREAM.lock', 'repo=' + UPSTREAM_URL + '\nbaseline=' + base + '\nbaseline_date=2000-01-01\nbaseline_note=fixture\n');
  git(repo, 'add', '-A');
  git(repo, 'commit', '-q', '-m', 'settings');
  const vendor = spawnSync(process.execPath, [path.join(here, '..', 'sync.mjs'), base, '--commit', '--no-fetch', '--no-checkout', '--repo', repo, '--upstream', up], { encoding: 'utf8' });
  assert.equal(vendor.status, 0, vendor.stderr);
  git(repo, 'remote', 'add', 'origin', origin);
  git(repo, 'push', '-q', 'origin', 'main');

  const job = (o = {}, deps = {}) => runJob({ repo, upstream: up, fetch: false, syncScript: path.join(here, '..', 'sync.mjs'),
    repoSlug: SLUG, serverUrl: SERVER, runUrl: SERVER + '/' + SLUG + '/actions/runs/1', ...o }, { log: () => {}, ...deps });
  const remoteHead = (branch) => git(repo, 'ls-remote', 'origin', 'refs/heads/' + branch).split(/\s/)[0] || null;
  /** A second checkout of the repo (CI's two jobs each check out the same commit), with the same git identity. */
  const clone = (name) => {
    const dir = path.join(root, name);
    git(root, 'clone', '-q', repo, dir);
    for (const [k, v] of [['user.name', 'sync-fixture'], ['user.email', 'sync-fixture@example.invalid'], ['commit.gpgsign', 'false'], ['core.autocrlf', 'false']])
      git(dir, 'config', k, v);
    return dir;
  };
  return { root, up, repo, base, advance, job, remoteHead, clone, gitAt: git, git: (...a) => git(repo, ...a), cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

/** A fake GitHub API: records calls, keeps the issues it opens, and can fail on chosen endpoints. */
function fakeApi({ issues = [], fail = {} } = {}) {
  const calls = [];
  let next = 100;
  const api = async (method, p, body) => {
    calls.push({ method, p, body });
    if (method === 'GET' && p === '/repos/' + SLUG + '/issues?state=all&per_page=100') return issues.slice();
    if (method === 'POST' && p === '/repos/' + SLUG + '/actions/workflows/ci.yml/dispatches') {
      if (fail.dispatch) throw new Fail('POST dispatches failed: HTTP 403 Resource not accessible by integration');
      return null;
    }
    if (method === 'POST' && p === '/repos/' + SLUG + '/issues') {
      if (fail.issue) throw new Fail('POST issues failed: HTTP 500');
      const issue = { number: next++, title: body.title, body: body.body, state: 'open' };
      issues.push(issue);
      return issue;
    }
    throw new Error('unexpected API call ' + method + ' ' + p);
  };
  return { api, calls, issues, posts: () => calls.filter((c) => c.method === 'POST') };
}

const range = (a, b) => a.slice(0, 8) + '-' + b.slice(0, 8);
// A sync script that doesn't exist: a run given it fails if it syncs, so it proves a run didn't.
const NO_SYNC = path.join(os.tmpdir(), 'isotope-no-such-sync-script.mjs');

test('nothing to do when UPSTREAM.lock is upstream main: no branch, no API call', async () => {
  const f = fixture();
  try {
    const gh = fakeApi();
    const r = await f.job({}, { api: gh.api });
    assert.equal(r.status, 'up-to-date');
    assert.deepEqual(gh.calls, []);
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
    assert.equal(f.git('ls-remote', '--heads', 'origin', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('upstream moved: pushes sync/<old>-<new> with the regenerated files, dispatches CI and opens an issue with the report', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'namespace Y;\nclass Node { int depth; }\n', 'src/Lib/Elements/Button.cs': 'class Button { int x; }\n' },
      'Fix the button (#9)');
    const gh = fakeApi();
    const r = await f.job({}, { api: gh.api });
    const branch = 'sync/' + range(f.base, next);
    assert.equal(r.status, 'opened');
    assert.equal(r.branch, branch);
    assert.ok(f.remoteHead(branch), 'the branch is pushed');
    const files = f.git('show', '--name-only', '--format=', 'origin/' + branch).split('\n').sort();
    assert.deepEqual(files, ['UPSTREAM.lock', 'docs/sync/' + range(f.base, next) + '.md', 'src/Upstream/src/Lib/Yoga/Node.cs', 'src/Upstream/vendored.json']);
    assert.match(f.git('show', 'origin/' + branch + ':UPSTREAM.lock'), new RegExp('^baseline=' + next + '$', 'm'));
    assert.equal(f.git('rev-parse', 'origin/' + branch + '~1'), f.git('rev-parse', 'main'), 'one commit on top of the checkout');
    const [dispatch, issue] = gh.posts();
    assert.deepEqual(dispatch.body, { ref: branch });
    assert.equal(issue.body.title, 'Upstream sync ' + f.base.slice(0, 8) + ' → ' + next.slice(0, 8));
    assert.match(issue.body.body, /## Tier 3: PRs to port by hand/);
    assert.match(issue.body.body, /\x60#9\x60/, 'the upstream PR number is code, not a reference');
    assert.doesNotMatch(issue.body.body, /\]\(https:\/\/example\.invalid/, 'no link to upstream');
    assert.ok(issue.body.body.includes(SERVER + '/' + SLUG + '/tree/' + branch), 'links the branch');
    assert.match(issue.body.body, /CI:\*\* dispatched/);
    assert.equal(r.issue, gh.issues[0].number);
  } finally { f.cleanup(); }
});

test('--dry-run syncs locally but pushes and opens nothing, and returns to the starting branch', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const gh = fakeApi();
    const r = await f.job({ dryRun: true }, { api: gh.api });
    assert.equal(r.status, 'dry-run');
    assert.match(r.body, /# Upstream sync/);
    assert.match(r.body, /\x60#8\x60/, 'the issue body it would open, neutralised');
    assert.equal(r.branch, 'sync/' + range(f.base, next));
    assert.deepEqual(gh.posts(), []);
    assert.equal(f.remoteHead(r.branch), null);
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
    assert.equal(f.git('status', '--porcelain'), '');
    assert.match(f.git('show', 'HEAD:UPSTREAM.lock'), new RegExp('baseline=' + f.base));
  } finally { f.cleanup(); }
});

test('a dry run needs no GitHub access; a real run refuses to start without it', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    assert.equal((await f.job({ dryRun: true })).status, 'dry-run');
    await assert.rejects(f.job({}), /needs GITHUB_TOKEN/);
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('a re-run for the same range pushes and opens nothing again', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const gh = fakeApi();
    const first = await f.job({}, { api: gh.api });
    const pushed = f.remoteHead(first.branch);
    f.git('checkout', '-q', 'main'); // the next scheduled run starts from a fresh checkout of main
    const again = await f.job({ syncScript: NO_SYNC }, { api: gh.api });
    assert.equal(again.status, 'exists');
    assert.equal(again.issue, first.issue);
    assert.equal(gh.posts().length, 2, 'only the first run dispatched and opened');
    assert.equal(f.remoteHead(first.branch), pushed);
  } finally { f.cleanup(); }
});

test('a branch pushed by a run that failed before its issue gets the issue on the next run, from its own report', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const broken = fakeApi({ fail: { issue: true } });
    await assert.rejects(f.job({}, { api: broken.api }), /HTTP 500/);
    const branch = 'sync/' + range(f.base, next);
    const pushed = f.remoteHead(branch);
    assert.ok(pushed);
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main', 'the checkout is back where it was');
    const gh = fakeApi();
    const r = await f.job({ syncScript: NO_SYNC }, { api: gh.api });
    assert.equal(r.status, 'opened');
    assert.equal(f.remoteHead(branch), pushed, 'not pushed again');
    const issue = gh.posts().find((c) => c.p.endsWith('/issues'));
    assert.match(issue.body.body, /# Upstream sync/);
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('a range whose issue was closed and whose branch is gone is not pushed or opened again', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const title = 'Upstream sync ' + f.base.slice(0, 8) + ' → ' + next.slice(0, 8);
    const gh = fakeApi({ issues: [{ number: 7, title, state: 'closed' }] });
    const r = await f.job({ syncScript: NO_SYNC }, { api: gh.api }); // a done range is not synced again
    assert.equal(r.status, 'exists');
    assert.equal(r.issue, 7);
    assert.deepEqual(gh.posts(), []);
    assert.equal(f.git('ls-remote', '--heads', 'origin', 'sync/*'), '');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('a CI dispatch that fails still opens the issue, which says so', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const gh = fakeApi({ fail: { dispatch: true } });
    const r = await f.job({}, { api: gh.api });
    assert.equal(r.status, 'opened');
    assert.equal(r.ci, false);
    assert.match(gh.issues[0].body, /CI:\*\* not dispatched \(POST dispatches failed: HTTP 403[^)]*\): run \x60ci\x60 on the branch by hand/);
  } finally { f.cleanup(); }
});

test('a failing sync fails the job loudly: nothing pushed or opened, the checkout is back where it was', async () => {
  const f = fixture();
  try {
    // The tier 2 file loses the text its transform rule rewrites, so the rule stops matching.
    f.advance({ 'src/Lib/Core/Engine.cs': 'class Engine { object e; }\n' }, 'Drop the WinUI field (#10)');
    const gh = fakeApi();
    await assert.rejects(f.job({}, { api: gh.api }), (e) => e instanceof Fail && /tools\/sync failed \(exit 1\); nothing was pushed or opened:[^]*drop-ns/.test(e.message));
    assert.deepEqual(gh.posts(), []);
    assert.equal(f.git('ls-remote', '--heads', 'origin', 'sync/*'), '');
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
    assert.equal(f.git('status', '--porcelain'), '');
  } finally { f.cleanup(); }
});

test('an older open sync issue is named as superseded', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const gh = fakeApi({ issues: [
      { number: 3, title: 'Upstream sync aaaaaaaa → bbbbbbbb', state: 'open' },
      { number: 2, title: 'Upstream sync cccccccc → dddddddd', state: 'closed' },
      { number: 4, title: 'Upstream sync eeeeeeee → ffffffff', state: 'open', pull_request: {} },
    ] });
    await f.job({}, { api: gh.api });
    const body = gh.issues.at(-1).body;
    assert.match(body, /Supersedes\*\* the open #3 \(aaaaaaaa → bbbbbbbb\): close it/);
    assert.doesNotMatch(body, /#2 |#4 /);
  } finally { f.cleanup(); }
});

test('a sync that fails after writing leaves the checkout clean', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    // tools/sync writes the files, then its commit is refused.
    writeFileSync(path.join(f.repo, '.git', 'hooks', 'pre-commit'), '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    await assert.rejects(f.job({ dryRun: true }), /tools\/sync failed/);
    assert.equal(f.git('status', '--porcelain'), '');
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('the issue compares the sync branch with the branch it was made from', async () => {
  const f = fixture();
  try {
    f.git('checkout', '-q', '-b', 'trunk');
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const r = await f.job({ dryRun: true });
    assert.ok(r.body.includes('/compare/trunk...sync/' + range(f.base, next) + ')'), r.body);
  } finally { f.cleanup(); }
});

test('the step summary says what an existing range\'s issue and branch are', () => {
  const r = { status: 'exists', branch: 'sync/a-b', issue: 7, old: 'a'.repeat(40), new: 'b'.repeat(40) };
  assert.match(summarise({ ...r, issueState: 'closed', pushed: false }), /#7 \(closed\) is for this range, and its branch is gone\./);
  assert.match(summarise({ ...r, issueState: 'open', pushed: true }), /#7 \(open\) is for this range, and \x60sync\/a-b\x60 is pushed\./);
});

test('a GET of a list follows the Link pages; other replies are returned as they are', async () => {
  const pages = {
    'https://api.invalid/repos/o/r/issues?per_page=2': { body: [{ n: 1 }, { n: 2 }], link: '<https://api.invalid/repos/o/r/issues?per_page=2&page=2>; rel="next", <https://api.invalid/x>; rel="last"' },
    'https://api.invalid/repos/o/r/issues?per_page=2&page=2': { body: [{ n: 3 }], link: '<https://api.invalid/repos/o/r/issues?per_page=2>; rel="first"' },
    'https://api.invalid/repos/o/r/issues': { body: { number: 9 }, link: '<https://api.invalid/never>; rel="next"' },
  };
  const seen = [];
  const fetchImpl = async (url, init) => {
    seen.push(init.method + ' ' + url);
    assert.equal(init.headers.Authorization, 'Bearer t');
    const p = pages[url];
    return { ok: !!p, status: p ? 200 : 404, text: async () => JSON.stringify(p?.body ?? {}), headers: { get: (h) => (h === 'link' ? p?.link ?? null : null) } };
  };
  const api = githubApi({ token: 't', apiUrl: 'https://api.invalid/', fetchImpl });
  assert.deepEqual(await api('GET', '/repos/o/r/issues?per_page=2'), [{ n: 1 }, { n: 2 }, { n: 3 }]);
  assert.deepEqual(await api('POST', '/repos/o/r/issues', { title: 't' }), { number: 9 });
  assert.equal(seen.length, 3);
  await assert.rejects(api('GET', '/missing'), /GET \/missing failed: HTTP 404/);
});

test('a dry run runs the sync even where a real run would skip it, and says what the real run would do', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const title = 'Upstream sync ' + f.base.slice(0, 8) + ' → ' + next.slice(0, 8);
    let r = await f.job({ dryRun: true }, { api: fakeApi({ issues: [{ number: 7, title, state: 'open' }] }).api });
    assert.equal(r.status, 'dry-run');
    assert.match(r.plan, /^do nothing: issue #7 \(open\) is for this range$/);
    assert.match(r.body, /# Upstream sync/, 'the sync ran');
    // A branch already pushed: the real run would only open the issue; the dry run still syncs locally.
    f.git('push', '-q', 'origin', 'main:refs/heads/' + r.branch);
    r = await f.job({ dryRun: true }, { api: fakeApi().api });
    assert.match(r.plan, /^open the issue only \(sync\/[0-9a-f-]+ is already pushed\)/);
    assert.match(r.body, /Changed by this sync: 1 changed/, 'from a local sync, not the pushed branch');
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
    assert.equal(f.git('status', '--porcelain'), '');
  } finally { f.cleanup(); }
});

test('a real run whose push fails leaves the checkout as it found it', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    writeFileSync(path.join(f.root, 'origin.git', 'hooks', 'pre-receive'), '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    const gh = fakeApi();
    await assert.rejects(f.job({}, { api: gh.api }));
    assert.deepEqual(gh.posts(), []);
    assert.equal(f.git('rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
    assert.equal(f.git('status', '--porcelain'), '');
  } finally { f.cleanup(); }
});

test('the command runs when it is reached through a linked folder (a junction on Windows)', () => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'isotope-sync-job-link-'));
  const link = path.join(root, 'ci');
  try {
    symlinkSync(here, link, process.platform === 'win32' ? 'junction' : 'dir');
    const r = spawnSync(process.execPath, [path.join(link, 'upstream-sync.mjs'), '--help'], { encoding: 'utf8' });
    assert.equal(r.status, 0, r.stderr);
    assert.match(r.stdout, /^Usage: node tools\/sync\/ci\/upstream-sync\.mjs/, 'it ran, rather than exiting 0 silently');
  } finally {
    // The link itself first, never through it.
    rmSync(link, { force: true, recursive: false });
    rmSync(root, { recursive: true, force: true });
  }
});

test('a dirty working tree is refused before anything is written', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    writeFileSync(path.join(f.repo, 'notes.txt'), 'work in progress\n');
    await assert.rejects(f.job({ dryRun: true }), /working tree is not clean/);
    assert.equal(f.git('branch', '--list', 'sync/*'), '');
  } finally { f.cleanup(); }
});

test('neutralise puts references, mentions and outside links in code, and leaves code spans and own links alone', () => {
  const own = 'https://github.com/owner/repo';
  const cases = [
    ['| [#1654](https://github.com/up/stream/pull/1654) | Fix it (#1654) |', '| \x60#1654\x60 | Fix it (\x60#1654\x60) |'],
    ['Upstream: https://github.com/up/stream.', 'Upstream: \x60https://github.com/up/stream\x60.'],
    ['thanks @someone and up/stream#12 and up/stream@abc123', 'thanks \x60@someone\x60 and \x60up/stream#12\x60 and \x60up/stream@abc123\x60'],
    ['a \x60#12 @x https://h.invalid\x60 span, and \x60\x60 a \x60 b #3 \x60\x60', 'a \x60#12 @x https://h.invalid\x60 span, and \x60\x60 a \x60 b #3 \x60\x60'],
    ['[report](https://github.com/owner/repo/blob/x/r.md) and ' + own + '/issues', '[report](https://github.com/owner/repo/blob/x/r.md) and ' + own + '/issues'],
    ['## Heading, C# and issue#5', '## Heading, C# and issue#5'],
    ['(see https://github.com/up/stream/pull/2) <https://h.invalid/x>', '(see \x60https://github.com/up/stream/pull/2\x60) <\x60https://h.invalid/x\x60>'],
    // Nothing a replacement wrapped is wrapped again (which would split the span and leave the match live).
    ['[see up/stream#12](https://x.invalid) and https://h.invalid/?q=up/stream#5', 'see \x60up/stream#12\x60 and \x60https://h.invalid/?q=up/stream#5\x60'],
    // A link whose label is a code span keeps only its label.
    ['[\x60Foo.cs\x60](https://github.com/up/stream/pull/1650) and [a \x60b\x60 #3](https://x.invalid)', '\x60Foo.cs\x60 and a \x60b\x60 \x60#3\x60'],
  ];
  for (const [input, want] of cases) assert.equal(neutralise(input, own), want, input);
});

test('a very long report is cut in the issue body at a line end, and points at the file', () => {
  // Every offset of the limit within a line, so some cuts fall inside a code span.
  for (let pad = 0; pad < 25; pad++) {
    const report = '# Upstream sync' + ' '.repeat(pad) + '\n' + '| #1654 | \x60src/a.cs\x60 |\n'.repeat(5000);
    const body = issueBody({ report, branch: 'sync/a-b', reportPath: 'docs/sync/a-b.md', serverUrl: SERVER, repoSlug: SLUG, runUrl: null, ci: { ok: true } });
    assert.ok(body.length < 65536, String(body.length));
    assert.match(body, /cut: the whole report is the file on the branch/);
    assert.ok(body.includes(SERVER + '/' + SLUG + '/blob/sync/a-b/docs/sync/a-b.md'));
    for (const line of body.split('\n')) assert.equal((line.match(/\x60/g) ?? []).length % 2, 0, 'an open code span (pad ' + pad + '): ' + line);
  }
});

test('arguments: unknown ones, and options of the other phase, are refused', () => {
  assert.deepEqual(parseArgs(['--dry-run', '--no-fetch', '--repo', 'r']), { command: 'all', dryRun: true, fetch: false, help: false, repo: 'r' });
  assert.deepEqual(parseArgs(['prepare', '--no-fetch', '--out', 'o']), { command: 'prepare', dryRun: false, fetch: false, help: false, out: 'o' });
  assert.deepEqual(parseArgs(['publish', '--dry-run', '--target', 't', '--bundle', 'b']), { command: 'publish', dryRun: true, fetch: true, help: false, target: 't', bundle: 'b' });
  assert.deepEqual(parseArgs(['plan', '--dry-run']), { command: 'plan', dryRun: true, fetch: true, help: false });
  assert.throws(() => parseArgs(['plan', '--out', 'o']), /unknown argument --out for plan/);
  assert.throws(() => parseArgs(['--push']), /unknown argument --push/);
  assert.throws(() => parseArgs(['--repo']), /needs a value/);
  assert.throws(() => parseArgs(['prepare', '--dry-run', '--out', 'o']), /unknown argument --dry-run for prepare/);
  assert.throws(() => parseArgs(['publish', '--upstream', 'u', '--target', 't']), /unknown argument --upstream for publish/);
  assert.throws(() => parseArgs(['--out', 'o']), /unknown argument --out/);
  assert.throws(() => parseArgs(['prepare']), /prepare needs --out/);
  assert.throws(() => parseArgs(['publish', '--dry-run', '--bundle', 'b']), /publish needs --target/);
});

test('the command line: a dry run writes the step outputs and summary; a failure exits 1 with the reason in the summary', () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const outFile = path.join(f.root, 'out.txt');
    const sumFile = path.join(f.root, 'summary.md');
    const env = { ...process.env, GITHUB_OUTPUT: outFile, GITHUB_STEP_SUMMARY: sumFile };
    delete env.GITHUB_TOKEN;
    const cli = (...a) => spawnSync(process.execPath, [SCRIPT, '--repo', f.repo, '--upstream', f.up, '--no-fetch', ...a], { encoding: 'utf8', env });
    let r = cli('--dry-run');
    assert.equal(r.status, 0, r.stderr);
    assert.match(readFileSync(outFile, 'utf8'), new RegExp('^status=dry-run\nbranch=sync/' + range(f.base, next) + '\nissue=\nnew=' + next + '\n$'));
    assert.match(readFileSync(sumFile, 'utf8'), /Dry run: upstream moved[^]*The issue it would open[^]*# Upstream sync/);
    assert.match(r.stdout, /dry run: a real run would push sync\//);
    r = cli();
    assert.equal(r.status, 1);
    assert.match(r.stderr, /needs GITHUB_TOKEN/);
    assert.match(readFileSync(sumFile, 'utf8'), /## Upstream sync failed/);
    assert.equal(spawnSync(process.execPath, [SCRIPT, '--bogus'], { encoding: 'utf8' }).status, 2);
    assert.ok(!existsSync(path.join(f.repo, 'docs', 'sync', range(f.base, next) + '.md')));
  } finally { f.cleanup(); }
});

// The publish phase runs with a write token, and checks the bundle a job that ran upstream's code made. Each case below
// crafts a bundle that breaks one rule and must be refused. One fixture serves them all: prepare runs in a clone (as
// in CI, where the two jobs have their own checkouts), the cases are crafted there, and publish checks them in the repo.
describe('the publish phase refuses a bundle that breaks a rule', () => {
  let f, prep, p, n = 0;
  const QUARANTINE_REFS = () => f.gitAt(f.repo, 'for-each-ref', 'refs/isotope-sync');
  const at = (...a) => f.gitAt(prep, ...a);
  before(async () => {
    f = fixture();
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    prep = f.clone('prep');
    p = await prepare({ repo: prep, upstream: f.up, fetch: false, syncScript: path.join(here, '..', 'sync.mjs'), out: path.join(f.root, 'genuine') }, { log: () => {} });
    assert.equal(p.status, 'built');
  });
  after(() => f?.cleanup());

  /** A commit made with plumbing in the prepare clone: the tree of "from" with paths added (text, or { mode, content }) or removed. */
  const craft = ({ from = p.commit, parents = [p.base], message = 'sync upstream ' + p.old.slice(0, 8) + ' → ' + p.new.slice(0, 8), add = {}, remove = [], env = {}, tree } = {}) => {
    const idx = path.join(prep, '.git', 'craft-index');
    const run = (args, input) => execFileSync('git', [...G, ...args], { cwd: prep, encoding: 'utf8', input, env: { ...process.env, GIT_INDEX_FILE: idx, ...env } }).trim();
    if (!tree) {
      run(['read-tree', from + '^{tree}']);
      for (const [rel, v] of Object.entries(add)) {
        const { mode = '100644', content } = typeof v === 'string' ? { content: v } : v;
        run(['update-index', '--add', '--cacheinfo', mode + ',' + run(['hash-object', '-w', '--stdin'], content) + ',' + rel]);
      }
      for (const rel of remove) run(['update-index', '--force-remove', rel]);
      tree = run(['write-tree']);
      rmSync(idx, { force: true });
    }
    return run(['commit-tree', tree, ...parents.flatMap((x) => ['-p', x]), '-m', message]);
  };
  /** A bundle folder: heads (branch -> commit) on the prerequisite base, and the metadata with overrides. */
  const variant = ({ commit = p.commit, heads = { [p.branch]: commit }, base = p.base, meta = {}, rawMeta } = {}) => {
    const out = path.join(f.root, 'case-' + n++);
    mkdirSync(out);
    for (const [b, sha] of Object.entries(heads)) at('update-ref', 'refs/heads/' + b, sha);
    at('bundle', 'create', '--quiet', path.join(out, BUNDLE), '^' + base, ...Object.keys(heads).map((b) => 'refs/heads/' + b));
    for (const b of Object.keys(heads)) at('update-ref', '-d', 'refs/heads/' + b);
    writeFileSync(path.join(out, META), rawMeta ?? JSON.stringify({ base: p.base, branch: p.branch, commit, new: p.new, old: p.old, reportPath: p.reportPath, ...meta }));
    return out;
  };
  const refused = (out, why, target = p.new) => {
    let left;
    try {
      assert.throws(() => verifyBundle({ repo: f.repo, dir: out, target }), (e) => e instanceof Fail && e.message.startsWith('refused the sync bundle: ') && why.test(e.message));
    } finally {
      // A bundle accepted by mistake leaves its ref, which must not fail the next case.
      left = QUARANTINE_REFS();
      if (left) f.gitAt(f.repo, 'update-ref', '-d', 'refs/isotope-sync/candidate');
    }
    assert.equal(left, '', 'no quarantine ref is left behind');
  };
  const lockAt = (commit) => at('show', commit + ':UPSTREAM.lock') + '\n';

  test('accepts the bundle prepare wrote (the control for the cases below)', () => {
    const c = verifyBundle({ repo: f.repo, dir: variant(), target: p.new });
    assert.equal(c.commit, p.commit);
    assert.equal(c.branch, p.branch);
    assert.match(c.report, /# Upstream sync/);
    f.gitAt(f.repo, 'update-ref', '-d', 'refs/isotope-sync/candidate');
  });

  test('a real run with a refused bundle pushes nothing and makes no API call', async () => {
    const gh = fakeApi();
    await assert.rejects(publish({ repo: f.repo, target: p.new, bundle: variant({ commit: craft({ add: { '.github/workflows/x.yml': 'on: push\n' } }) }), repoSlug: SLUG, serverUrl: SERVER }, { api: gh.api, log: () => {} }),
      /refused the sync bundle: it changes \.github\/workflows\/x\.yml/);
    assert.deepEqual(gh.calls, []);
    assert.equal(f.git('ls-remote', '--heads', 'origin', 'sync/*'), '');
    assert.equal(QUARANTINE_REFS(), '');
  });

  test('metadata: missing', () => {
    const out = variant();
    rmSync(path.join(out, META));
    refused(out, /no readable meta\.json/);
  });
  test('metadata: a key it should not have', () => {
    refused(variant({ meta: { push: 'refs/heads/main' } }), /meta\.json must have exactly the keys/);
  });
  test('metadata: a value that is not a commit sha', () => {
    refused(variant({ meta: { commit: 'HEAD' } }), /meta\.json: commit is not a 40-character commit sha/);
  });
  test('metadata: old is not the baseline in UPSTREAM.lock', () => {
    const old = 'a'.repeat(40);
    const range = old.slice(0, 8) + '-' + p.new.slice(0, 8);
    refused(variant({ meta: { old, branch: 'sync/' + range, reportPath: 'docs/sync/' + range + '.md' } }), /old aaaaaaaa is not the baseline in UPSTREAM\.lock/);
  });
  test('metadata: new is not the upstream commit the plan found', () => {
    refused(variant(), /new [0-9a-f]{8} is not the upstream commit the plan found \(dddddddd\)/, 'd'.repeat(40));
  });
  test('metadata: base is not the checkout\'s HEAD', () => {
    refused(variant({ meta: { base: 'b'.repeat(40) } }), /base bbbbbbbb is not this checkout's HEAD/);
  });
  test('metadata and bundle: a branch other than sync/<old>-<new>', () => {
    refused(variant({ heads: { other: p.commit }, meta: { branch: 'other' } }), /meta\.json: branch is not sync\/[0-9a-f]{8}-[0-9a-f]{8}$/);
  });
  test('metadata: a report path other than the range\'s', () => {
    refused(variant({ meta: { reportPath: 'docs/sync/other.md' } }), /reportPath is not docs\/sync\/[0-9a-f-]+\.md/);
  });
  test('bundle: missing', () => {
    const out = variant();
    rmSync(path.join(out, BUNDLE));
    refused(out, /no sync\.bundle/);
  });
  test('bundle: built on a commit this checkout does not have', () => {
    const elsewhere = craft({ from: p.base, message: 'not on main' });
    refused(variant({ commit: craft({ parents: [elsewhere] }), base: elsewhere }), /git bundle verify: .*lacks these prerequisite commits/);
  });
  test('bundle: a second head', () => {
    refused(variant({ heads: { [p.branch]: p.commit, 'sync/00000000-11111111': p.commit } }), /must hold one head, refs\/heads\/sync\/[0-9a-f-]+ at [0-9a-f]{8}; it holds .*, /);
  });
  test('bundle: its head is not the commit in the metadata', () => {
    // Another commit (commit-tree with the same tree, message, parent and second is the same commit).
    refused(variant({ heads: { [p.branch]: craft({ add: { 'src/Upstream/extra.txt': 'x\n' } }) } }), /must hold one head/);
  });
  test('bundle: a head that is not a commit (an annotated tag)', () => {
    const tag = execFileSync('git', [...G, 'mktag'], { cwd: prep, encoding: 'utf8',
      input: 'object ' + p.commit + '\ntype commit\ntag t\ntagger sync-fixture <sync-fixture@example.invalid> 0 +0000\n\nt\n' }).trim();
    // git refuses to point a branch at a tag, so the ref is written as a file (the files backend reads it as it is).
    const out = path.join(f.root, 'case-tag');
    mkdirSync(out);
    const refFile = path.join(prep, '.git', 'refs', 'heads', ...p.branch.split('/'));
    mkdirSync(path.dirname(refFile), { recursive: true });
    writeFileSync(refFile, tag + '\n');
    try { at('bundle', 'create', '--quiet', path.join(out, BUNDLE), '^' + p.base, 'refs/heads/' + p.branch); } finally { rmSync(refFile); }
    writeFileSync(path.join(out, META), JSON.stringify({ base: p.base, branch: p.branch, commit: tag, new: p.new, old: p.old, reportPath: p.reportPath }));
    refused(out, /its head is not [0-9a-f]{8}/);
  });
  test('bundle: an object git\'s fsck refuses (a ".git" folder in the vendored tree)', () => {
    const run = (args, input) => execFileSync('git', [...G, ...args], { cwd: prep, encoding: 'utf8', input }).trim();
    const blob = run(['hash-object', '-w', '--stdin'], '[core]\n');
    const dotgit = run(['mktree'], '100644 blob ' + blob + '\tconfig\n');
    const withEntry = (tree, dirs) => {
      const lines = run(['ls-tree', tree]).split('\n');
      if (!dirs.length) return run(['mktree'], [...lines, '040000 tree ' + dotgit + '\t.git'].join('\n') + '\n');
      const i = lines.findIndex((l) => l.endsWith('\t' + dirs[0]));
      lines[i] = '040000 tree ' + withEntry(lines[i].split(/\s/)[2], dirs.slice(1)) + '\t' + dirs[0];
      return run(['mktree'], lines.join('\n') + '\n');
    };
    refused(variant({ commit: craft({ tree: withEntry(p.commit + '^{tree}', ['src', 'Upstream']) }) }), /git fetch \(with fsck\): .*hasDotgit/);
  });
  test('commit: two commits on HEAD', () => {
    refused(variant({ commit: craft({ parents: [craft()] }) }), /must have one parent, this checkout's HEAD [0-9a-f]{8}; it has [0-9a-f]{8}$/);
  });
  test('commit: a merge', () => {
    refused(variant({ commit: craft({ parents: [p.base, at('rev-parse', p.base + '~1')] }) }), /must have one parent, this checkout's HEAD [0-9a-f]{8}; it has [0-9a-f]{8}, [0-9a-f]{8}$/);
  });
  test('commit: a message other than the sync\'s', () => {
    refused(variant({ commit: craft({ message: 'sync upstream ' + p.old.slice(0, 8) + ' → ' + p.new.slice(0, 8) + '\n\nthanks @someone' }) }), /the commit message is not "sync upstream/);
  });
  test('commit: made by another identity', () => {
    refused(variant({ commit: craft({ env: { GIT_AUTHOR_NAME: 'someone-else', GIT_AUTHOR_EMAIL: 'someone@example.invalid' } }) }), /not made by the identity this job commits with/);
  });
  test('paths: a change outside the vendored tree, the lock and the report', () => {
    refused(variant({ commit: craft({ add: { 'tools/sync/transforms.json': '{"rules":[]}\n' } }) }), /it changes tools\/sync\/transforms\.json \(M\); only src\/Upstream\/, UPSTREAM\.lock and docs\/sync\/[0-9a-f-]+\.md may change/);
  });
  test('paths: anything under .github/', () => {
    refused(variant({ commit: craft({ add: { '.github/workflows/ci.yml': 'on: push\n' } }) }), /it changes \.github\/workflows\/ci\.yml: nothing under \.github\/ may change/);
  });
  test('paths: a second report', () => {
    refused(variant({ commit: craft({ add: { 'docs/sync/other.md': '# other\n' } }) }), /it changes docs\/sync\/other\.md \(A\)/);
  });
  test('paths: a vendored path that is not a regular file (a link)', () => {
    refused(variant({ commit: craft({ add: { 'src/Upstream/link': { mode: '120000', content: '../../.github' } } }) }), /src\/Upstream\/link is not a regular file \(mode 120000\)/);
  });
  test('paths: UPSTREAM.lock deleted', () => {
    refused(variant({ commit: craft({ remove: ['UPSTREAM.lock'] }) }), /it changes UPSTREAM\.lock \(D\)/);
  });
  test('paths: UPSTREAM.lock not updated', () => {
    refused(variant({ commit: craft({ add: { 'UPSTREAM.lock': lockAt(p.base) } }) }), /it does not update UPSTREAM\.lock/);
  });
  test('paths: no report', () => {
    refused(variant({ commit: craft({ remove: [p.reportPath] }) }), /it has no report at docs\/sync\//);
  });
  test('lock: a baseline other than the new commit', () => {
    refused(variant({ commit: craft({ add: { 'UPSTREAM.lock': lockAt(p.commit).replace(/^baseline=.*$/m, 'baseline=' + 'c'.repeat(40)) } }) }), /UPSTREAM\.lock is not the old lock with baseline [0-9a-f]{8}/);
  });
  test('lock: a baseline_note tools/sync would not write', () => {
    refused(variant({ commit: craft({ add: { 'UPSTREAM.lock': lockAt(p.commit).replace(/^baseline_note=.*$/m, 'baseline_note=thanks @someone') } }) }), /baseline_date or baseline_note is not what tools\/sync writes/);
  });
  test('lock: another line changed', () => {
    refused(variant({ commit: craft({ add: { 'UPSTREAM.lock': lockAt(p.commit).replace(/^repo=.*$/m, 'repo=https://example.invalid/other') } }) }), /UPSTREAM\.lock is not the old lock with baseline/);
  });
  test('lock: a second baseline_note line, before the one tools/sync writes', () => {
    refused(variant({ commit: craft({ add: { 'UPSTREAM.lock': lockAt(p.commit).replace(/^baseline_note=/m, 'baseline_note=thanks @someone\nbaseline_note=') } }) }), /UPSTREAM\.lock is not the old lock with baseline/);
  });
  test('report: personal data', () => {
    refused(variant({ commit: craft({ add: { [p.reportPath]: at('show', p.commit + ':' + p.reportPath) + '\nmailto someone@mail.example.net.au\n' } }) }), /the report has personal data: line \d+ \(email\)/);
  });
  test('paths: a ".git" or ".." component, in any case or 8.3 form, whatever git\'s fsck lets through', () => {
    for (const bad of ['src/Upstream/.git/config', 'src/Upstream/.GIT/x', 'src/Upstream/.git. /x', 'src/Upstream/git~1/x', 'src/Upstream/../x', 'src/Upstream/./x', 'src/Upstream//x'])
      assert.ok(badPath(bad), bad);
    for (const good of ['src/Upstream/.gitattributes', 'src/Upstream/a.git/x', 'src/Upstream/.github/x', 'src/Upstream/..x/y', 'src/Upstream/git~x'])
      assert.ok(!badPath(good), good);
  });
  test('publish with no bundle when the branch is not pushed', async () => {
    await assert.rejects(publish({ repo: f.repo, target: p.new, dryRun: true }, { log: () => {} }), /nothing to push: sync\/[0-9a-f-]+ is not pushed, and no bundle was given/);
  });
});

test('prepare and publish in separate checkouts, as in CI: publish pushes the commit it checked', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const prep = f.clone('prep');
    const out = path.join(f.root, 'artifact');
    const p = await prepare({ repo: prep, upstream: f.up, fetch: false, syncScript: path.join(here, '..', 'sync.mjs'), out }, { log: () => {} });
    assert.equal(p.status, 'built');
    assert.equal(p.branch, 'sync/' + range(f.base, next));
    assert.equal(f.gitAt(prep, 'status', '--porcelain'), '', 'prepare leaves its checkout clean');
    assert.equal(f.gitAt(prep, 'branch', '--list', 'sync/*'), '');
    assert.equal(f.gitAt(prep, 'ls-remote', '--heads', f.root + '/origin.git', 'sync/*'), '', 'prepare pushes nothing');
    const gh = fakeApi();
    const r = await publish({ repo: f.repo, target: next, bundle: out, repoSlug: SLUG, serverUrl: SERVER }, { api: gh.api, log: () => {} });
    assert.equal(r.status, 'opened');
    assert.equal(f.remoteHead(p.branch), p.commit, 'the checked commit is what was pushed');
    assert.equal(f.git('rev-parse', p.commit + '~1'), f.git('rev-parse', 'main'));
    assert.match(gh.issues[0].body, /# Upstream sync/);
    assert.equal(f.git('for-each-ref', 'refs/isotope-sync'), '', 'the quarantine ref is gone');
    assert.equal(f.git('status', '--porcelain'), '');
  } finally { f.cleanup(); }
});

test('prepare refuses an --out folder inside the repo, which its clean-up would delete', async () => {
  const f = fixture();
  try {
    f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    await assert.rejects(prepare({ repo: f.repo, upstream: f.up, fetch: false, out: path.join(f.repo, 'out') }, { log: () => {} }), /--out must be outside the repo/);
  } finally { f.cleanup(); }
});

test('the command line, as the workflow runs it: plan, prepare --target, then publish --dry-run checks and writes nothing', () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const prep = f.clone('prep');
    const out = path.join(f.root, 'artifact');
    const outFile = path.join(f.root, 'out.txt');
    const env = { ...process.env, GITHUB_OUTPUT: outFile };
    delete env.GITHUB_TOKEN;
    const cli = (...a) => spawnSync(process.execPath, [SCRIPT, ...a], { encoding: 'utf8', env });
    const outputs = () => { const t = readFileSync(outFile, 'utf8'); rmSync(outFile); return t; };
    let r = cli('plan', '--dry-run', '--repo', f.repo, '--upstream', f.up, '--no-fetch');
    assert.equal(r.status, 0, r.stderr);
    assert.equal(outputs(), 'status=build\nbranch=sync/' + range(f.base, next) + '\nissue=\nnew=' + next + '\n');
    r = cli('prepare', '--repo', prep, '--upstream', f.up, '--no-fetch', '--target', next, '--out', out);
    assert.equal(r.status, 0, r.stderr);
    assert.equal(outputs(), 'status=built\nbranch=sync/' + range(f.base, next) + '\nissue=\nnew=' + next + '\n');
    assert.ok(existsSync(path.join(out, BUNDLE)) && existsSync(path.join(out, META)));
    r = cli('publish', '--dry-run', '--repo', f.repo, '--target', next, '--bundle', out);
    assert.equal(r.status, 0, r.stderr);
    assert.match(r.stdout, /the sync bundle is valid[^]*dry run: a real run would push sync\//);
    assert.match(outputs(), /^status=dry-run\n/);
    assert.equal(f.git('ls-remote', '--heads', 'origin', 'sync/*'), '');
    r = cli('publish', '--repo', f.repo, '--target', next, '--bundle', out);
    assert.equal(r.status, 1);
    assert.match(r.stderr, /needs GITHUB_TOKEN/);
    r = cli('prepare', '--repo', prep, '--upstream', f.up, '--no-fetch', '--target', 'e'.repeat(40), '--out', out);
    assert.equal(r.status, 1);
    assert.match(r.stderr, /--target e{40} is not a full commit sha in the upstream cache/);
  } finally { f.cleanup(); }
});

test('plan: a dry run always builds; a real run skips a range with an issue, and only publishes a pushed branch with none', async () => {
  const f = fixture();
  try {
    assert.equal((await plan({ repo: f.repo, upstream: f.up, fetch: false, dryRun: true }, { log: () => {} })).status, 'up-to-date');
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const title = 'Upstream sync ' + f.base.slice(0, 8) + ' → ' + next.slice(0, 8);
    const run = (dryRun, issues) => plan({ repo: f.repo, upstream: f.up, fetch: false, dryRun, repoSlug: SLUG }, { api: fakeApi({ issues }).api, log: () => {} });
    assert.deepEqual(await run(false, []), { status: 'build', branch: 'sync/' + range(f.base, next), old: f.base, new: next });
    assert.equal((await run(true, [{ number: 7, title, state: 'open' }])).status, 'build');
    assert.deepEqual(await run(false, [{ number: 7, title, state: 'closed' }]),
      { status: 'exists', branch: 'sync/' + range(f.base, next), issue: 7, issueState: 'closed', pushed: false, old: f.base, new: next });
    f.git('push', '-q', 'origin', 'main:refs/heads/sync/' + range(f.base, next));
    assert.equal((await run(false, [])).status, 'publish');
    await assert.rejects(plan({ repo: f.repo, upstream: f.up, fetch: false }, { log: () => {} }), /needs GITHUB_TOKEN/);
  } finally { f.cleanup(); }
});

test('plan with no upstream cache asks the lock\'s repo for its main (git ls-remote)', async () => {
  const f = fixture();
  try {
    const next = f.advance({ 'src/Lib/Yoga/Node.cs': 'class Node2 {}\n' }, 'Change (#8)');
    const lock = readFileSync(path.join(f.repo, 'UPSTREAM.lock'), 'utf8').replace(/^repo=.*$/m, 'repo=' + f.up);
    writeFileSync(path.join(f.repo, 'UPSTREAM.lock'), lock);
    const r = await plan({ repo: f.repo, dryRun: true }, { log: () => {} });
    assert.equal(r.new, next);
    assert.equal(r.status, 'build');
    writeFileSync(path.join(f.repo, 'UPSTREAM.lock'), lock.replace(/^repo=.*$/m, 'repo=' + path.join(f.root, 'nowhere')));
    await assert.rejects(plan({ repo: f.repo, dryRun: true }, { log: () => {} }), /could not read main of .*nowhere \(git ls-remote\)/);
  } finally { f.cleanup(); }
});
