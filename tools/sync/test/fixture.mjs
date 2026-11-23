// Test fixtures: a throwaway upstream git repo and a throwaway copy of our repo's sync settings, in a temp dir.
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, readFileSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { run } from '../lib/run.mjs';

const G = ['-c', 'core.autocrlf=false', '-c', 'commit.gpgsign=false', '-c', 'init.defaultBranch=main'];

export function makeFixture({ tiers, transforms = { rules: [] }, files, watch = ['src/'] }) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'isotope-sync-test-'));
  const up = path.join(root, 'up');
  const repo = path.join(root, 'repo');
  mkdirSync(up);
  const git = (dir, ...args) => execFileSync('git', [...G, ...args], { cwd: dir, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git(up, 'init', '-q');
  git(up, 'config', 'user.name', 'sync-fixture-author');
  git(up, 'config', 'user.email', 'sync-fixture-author@example.invalid');

  const put = (base, rel, content) => {
    const p = path.join(base, ...rel.split('/'));
    mkdirSync(path.dirname(p), { recursive: true });
    writeFileSync(p, content);
  };
  /** files: {path: content, or null to delete}; returns the new commit's sha. */
  const commit = (changes, message) => {
    for (const [rel, content] of Object.entries(changes)) {
      if (content === null) rmSync(path.join(up, ...rel.split('/')));
      else put(up, rel, content);
    }
    git(up, 'add', '-A');
    git(up, 'commit', '-q', '-m', message);
    return git(up, 'rev-parse', 'HEAD');
  };
  const base = commit(files, 'Initial import (#1)');

  const settings = (t = tiers, tr = transforms) => {
    put(repo, 'tools/sync/tiers.json', JSON.stringify(t, null, 2));
    put(repo, 'tools/sync/transforms.json', JSON.stringify(tr, null, 2));
  };
  put(repo, 'tools/sync/sync.json', JSON.stringify({ vendorDir: 'src/Upstream', reportDir: 'docs/sync', watch, verify: [] }));
  settings();
  put(repo, 'UPSTREAM.lock', '# fixture\nrepo=https://example.invalid/upstream\nbaseline=' + base + '\nbaseline_date=2000-01-01\nbaseline_note=fixture\n');

  const sync = (...args) => {
    const out = [];
    const err = [];
    const code = run([...args, '--repo', repo, '--upstream', up, '--no-fetch', '--no-checkout'],
      { log: (s) => out.push(s), err: (s) => err.push(s), defaultRepo: repo });
    return { code, out: out.join('\n'), err: err.join('\n') };
  };
  const vendored = (rel) => path.join(repo, 'src', 'Upstream', ...rel.split('/'));
  const read = (rel) => readFileSync(path.join(repo, ...rel.split('/')), 'utf8');
  const exists = (rel) => existsSync(path.join(repo, ...rel.split('/')));
  return { root, up, repo, base, commit, sync, settings, vendored, read, exists, put: (rel, c) => put(repo, rel, c),
    git: (...a) => git(up, ...a), cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

/** A small upstream tree and tier map used by several tests. */
export const SAMPLE = {
  files: {
    'LICENSE': 'MIT\n',
    'src/Lib/Yoga/Node.cs': 'namespace Y;\nclass Node {}\n',
    'src/Lib/Yoga/Panel.cs': 'using Microsoft.UI.Xaml;\nclass Panel {}\n',
    'src/Lib/Core/Engine.cs': 'class Engine { Microsoft.UI.Xaml.UIElement e; }\n',
    'src/Lib/Core/Plain.cs': 'class Plain {}\n',
    'src/Lib/Elements/Button.cs': 'class Button {}\n',
    'src/Lib/Elements/Text.cs': 'class Text {}\n',
    'src/Other/Thing.cs': 'class Thing {}\n',
    'docs/readme.md': '# docs\n',
  },
  tiers: {
    rules: [
      { path: 'LICENSE', tier: 1 },
      { path: 'src/Lib/Yoga/Panel.cs', tier: 3 },
      { glob: 'src/Lib/Yoga/*.cs', tier: 1 },
      { path: 'src/Lib/Core/Engine.cs', tier: 2 },
      { path: 'src/Lib/Core/Plain.cs', tier: 1 },
      { glob: 'src/Lib/Elements/**', tier: 3 },
      { glob: 'src/Other/**', tier: 'none' },
    ],
  },
  transforms: {
    rules: [{ id: 'drop-ns', find: 'Microsoft\\.UI\\.Xaml\\.(UIElement)\\b', replace: '$1', files: ['src/Lib/Core/Engine.cs'] }],
  },
};
