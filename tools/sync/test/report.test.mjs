import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sanitizeSubject, prNumber } from '../lib/report.mjs';
import { parseLock, updateLock } from '../lib/lock.mjs';
import { makeFixture, SAMPLE } from './fixture.mjs';

test('subjects lose source branches, handles and emails', () => {
  assert.equal(sanitizeSubject('Merge pull request #1004 from someorg/someone-fix-thing'), 'Merge pull request #1004');
  assert.equal(sanitizeSubject("Merge branch 'someone/feature' into main"), 'Merge branch (source branch omitted)');
  assert.equal(sanitizeSubject("Merge remote-tracking branch 'origin/someone/x'"), 'Merge remote-tracking branch (source branch omitted)');
  assert.equal(sanitizeSubject('Fix layout, thanks @someone-else (#12)'), 'Fix layout, thanks (handle removed) (#12)');
  assert.equal(sanitizeSubject('Revert change by a.person@example.com'), 'Revert change by (email removed)');
  assert.equal(sanitizeSubject('a | b'), 'a \\| b');
  assert.equal(sanitizeSubject('Revert "Merge pull request #5 from someone/feature"'), 'Revert "Merge pull request #5"');
  assert.equal(sanitizeSubject('someone/feature-branch (#12)'), '(subject was a branch name, omitted) (#12)');
  assert.equal(sanitizeSubject('Fix stale Margin/Padding edges (#1532)'), 'Fix stale Margin/Padding edges (#1532)', 'ordinary slashes stay');
});

test('branch names and repo owners inside a subject are removed; ordinary slashes stay', () => {
  assert.equal(sanitizeSubject('User/someone/blank test app for perf (#506)'), '(branch name omitted) test app for perf (#506)');
  assert.equal(sanitizeSubject('Port someone/fix-layout to main'), 'Port (owner omitted)/fix-layout to main');
  assert.equal(sanitizeSubject('Pick up someone/fix-layout (#9)'), 'Pick up (owner omitted)/fix-layout (#9)', 'a lowercase hyphenated name reads as a branch');
  assert.equal(sanitizeSubject('ci: bump someone/action from 1 to 2 (#9)'), 'ci: bump (owner omitted)/action from 1 to 2 (#9)');
  assert.equal(sanitizeSubject('Sync to someone/yoga@41118bd (#3)'), 'Sync to (owner omitted)/yoga@41118bd (#3)');
  assert.equal(sanitizeSubject('Fixes someone/repo#12'), 'Fixes (owner omitted)/repo#12');
  assert.equal(sanitizeSubject('Revert someone/topic/sub-part'), 'Revert (name omitted)');
  assert.equal(sanitizeSubject('See https://github.com/someone/repo/pull/3'), 'See https://github.com/(path omitted)');
  assert.equal(sanitizeSubject('See http://someone:token@host.com/x and https://someone@host.com'), 'See http://host.com/(path omitted) and https://host.com');
  assert.equal(sanitizeSubject('Merge branch someone/topic'), 'Merge branch (owner omitted)/topic');
  assert.equal(sanitizeSubject('Cherry-pick from someone/Topic'), 'Cherry-pick from (owner omitted)/Topic');
  assert.equal(sanitizeSubject('Fixes someone/repo!12'), 'Fixes (owner omitted)/repo!12');
  assert.equal(sanitizeSubject('Port Dev/Someone/Fix (#77)'), 'Port (name omitted) (#77)', 'three capitalised parts can be a branch');
  assert.equal(sanitizeSubject('Revert someone/topic/fix.v2'), 'Revert (name omitted)', 'only known file extensions make a path');
  assert.equal(sanitizeSubject('Sync git@github.com:someone/repo.git (#3)'), 'Sync github.com:(path omitted) (#3)');
  assert.equal(sanitizeSubject('See ssh://git@github.com/someone/repo'), 'See ssh://github.com/(path omitted)');
  const local = ['C', ':', '\\', 'Us', 'ers', '\\someone\\x'].join('');
  assert.equal(sanitizeSubject('See ' + local + ' and /home/someone (#4)'), 'See (local path or email removed) and (local path or email removed) (#4)');
  for (const keep of ['Fix ListView/GridView stale closures (#836)', 'Add min/max to grid tracks (#1106)', 'Bump qs in /tests/perf/App (#1437)',
    'Link docs/guide/getting-started.md (#328)', 'Fix +/-1 px wobble (#172)', 'Control/Panel-level modifiers (#1015)',
    'Release 2024/01/02'])
    assert.equal(sanitizeSubject(keep), keep);
});

test('the report is checked like vendored files before it is written', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    const next = f.commit({ 'src/Lib/Elements/a.person@contoso.com.cs': 'class A {}\n' }, 'Add a file (#5)');
    const r = f.sync(next);
    assert.equal(r.code, 1);
    assert.match(r.err, /the sync report has personal data; nothing was written[^]*report line \d+ \(email\)/);
    assert.ok(!f.exists('docs/sync'), 'no report written');
    assert.match(f.read('UPSTREAM.lock'), new RegExp('baseline=' + f.base));
  } finally { f.cleanup(); }
});

test('PR numbers come from squash and merge subjects', () => {
  assert.equal(prNumber('Stop each keyframe channel (#1646)'), 1646);
  assert.equal(prNumber('Merge pull request #1004 from x/y'), 1004);
  assert.equal(prNumber('No number here'), null);
});

test('the lock keeps comments and unknown keys, and rejects short shas', () => {
  const text = '# c\nrepo=r\nbaseline=' + 'a'.repeat(40) + '\nother=1\n';
  assert.equal(updateLock(text, { baseline: 'b'.repeat(40), baseline_date: 'd' }), '# c\nrepo=r\nbaseline=' + 'b'.repeat(40) + '\nother=1\nbaseline_date=d\n');
  assert.throws(() => parseLock('repo=r\nbaseline=abc\n'), /40-character/);
});

test('the tier 3 report links PRs and carries no author names or source branches', () => {
  const f = makeFixture(SAMPLE);
  try {
    assert.equal(f.sync(f.base).code, 0);
    f.commit({ 'src/Lib/Elements/Button.cs': 'class Button { bool on; }\n' }, 'Merge pull request #12 from someorg/someone-button');
    f.commit({ 'src/Lib/Yoga/Node.cs': 'class Node { }\n' }, 'Faster nodes (#13)');
    const last = f.commit({ 'src/Other/Thing.cs': 'class Thing { }\n' }, 'Unwatched change (#14)');
    const r = f.sync(last);
    assert.equal(r.code, 0, r.err);
    const report = f.read('docs/sync/' + f.base.slice(0, 8) + '-' + last.slice(0, 8) + '.md');
    const tier3 = report.split('## Tier 3')[1].split('## Tier 1 and 2')[0];
    const tier12 = report.split('## Tier 1 and 2')[1];
    assert.match(tier3, /\[#12\]\(https:\/\/example\.invalid\/upstream\/pull\/12\) \| \d{4}-\d\d-\d\d \| Merge pull request #12 \| \x60src\/Lib\/Elements\/Button\.cs\x60/);
    assert.doesNotMatch(tier3, /#13|#14/);
    assert.match(tier12, /\[#13\]/);
    assert.doesNotMatch(tier12, /#12|#14/);
    assert.doesNotMatch(report, /someone|someorg|sync-fixture-author/);
    assert.doesNotMatch(report, /\b[A-Z]:[\\/]|Users[\\/]|\/tmp\//i, 'no local paths');
    assert.match(report, /3 first-parent commits/);
  } finally { f.cleanup(); }
});
