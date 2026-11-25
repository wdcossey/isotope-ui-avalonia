#!/usr/bin/env node
// Prints measure=true or measure=false for CI's samples job (#71), for "$GITHUB_OUTPUT": false only for a branch push
// whose changes since its merge base with origin/main are all docs (lib/changes.mjs says which). Run from the checkout;
// it reads GITHUB_EVENT_NAME and GITHUB_REF. Git runs without a shell, file names are never printed, and any failure
// prints measure=true.
//
//   node tools/migration/should-measure.mjs [--base-branch <name>] >> "$GITHUB_OUTPUT"
//
// --base-branch compares with another branch than main (proving the skip on a branch before it is merged).
import { spawnSync } from 'node:child_process';
import { decide } from './lib/changes.mjs';

const git = (...args) => {
  const r = spawnSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error || r.status !== 0) throw new Error('git ' + args[0] + ' exited ' + (r.error ? r.error.code : r.status));
  return r.stdout;
};

let decision;
try {
  const argv = process.argv.slice(2);
  const base = argv[0] === '--base-branch' && argv.length === 2 ? argv[1] : argv.length === 0 ? 'main' : null;
  if (!base || !/^[A-Za-z0-9][\w./-]*$/.test(base) || base.includes('..')) throw new Error('usage: should-measure.mjs [--base-branch <name>]');
  const facts = { event: process.env.GITHUB_EVENT_NAME, ref: process.env.GITHUB_REF };
  if (facts.event === 'push' && facts.ref !== 'refs/heads/main' && facts.ref !== 'refs/heads/' + base && facts.ref?.startsWith('refs/heads/')) {
    try {
      facts.shallow = git('rev-parse', '--is-shallow-repository').trim() === 'true';
      if (!facts.shallow) {
        // The checkout keeps no credentials; main is public, so it fetches without them.
        const remote = 'refs/remotes/origin/' + base;
        if (spawnSync('git', ['rev-parse', '--verify', '--quiet', remote]).status !== 0)
          git('fetch', '--no-tags', 'origin', '+refs/heads/' + base + ':' + remote);
        const mb = spawnSync('git', ['merge-base', remote, 'HEAD'], { encoding: 'utf8' });
        facts.mergeBase = mb.status === 0 ? mb.stdout.trim() : null;
        // --no-renames lists a renamed file's old path too, so code moved under docs/ still measures.
        if (facts.mergeBase) facts.files = git('diff', '--no-renames', '--name-only', '-z', facts.mergeBase, 'HEAD').split('\0').filter(Boolean);
      }
    } catch (e) {
      facts.error = e.message;
    }
  }
  decision = decide(facts);
} catch (e) {
  decision = { measure: true, reason: 'could not decide (' + e.message + '): measured' };
}
process.stderr.write('samples: ' + decision.reason + '\n');
process.stdout.write('measure=' + (decision.measure ? 'true' : 'false') + '\n');
