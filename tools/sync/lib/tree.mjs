// Writing, reading and comparing a vendored tree on disk.
import { mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync, existsSync, lstatSync } from 'node:fs';
import path from 'node:path';

export function writeTree(root, files) {
  for (const [rel, buf] of files) {
    const dst = path.join(root, ...rel.split('/'));
    mkdirSync(path.dirname(dst), { recursive: true });
    writeFileSync(dst, buf);
  }
}

/** Every file under root as a forward-slash relative path. Refuses to follow links. */
export function listTree(root) {
  const out = [];
  if (!existsSync(root)) return out;
  const walk = (dir, rel) => {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, e.name);
      const r = rel ? rel + '/' + e.name : e.name;
      if (lstatSync(full).isSymbolicLink()) throw new Error('unexpected link in the vendored tree: ' + r);
      if (e.isDirectory()) walk(full, r);
      else out.push(r);
    }
  };
  walk(root, '');
  return out.sort();
}

/** Differences between generated files and the tree on disk. */
export function compareTree(root, files) {
  const onDisk = new Set(listTree(root));
  const problems = [];
  for (const [rel, buf] of [...files].sort(([a], [b]) => (a < b ? -1 : 1))) {
    if (!onDisk.has(rel)) { problems.push('missing: ' + rel); continue; }
    onDisk.delete(rel);
    if (!readFileSync(path.join(root, ...rel.split('/'))).equals(buf)) problems.push('differs: ' + rel);
  }
  for (const rel of onDisk) problems.push('not generated (remove it, or add a tier map rule): ' + rel);
  return problems;
}

/** Replaces root with exactly the generated files. */
export function replaceTree(root, files) {
  listTree(root); // throws on links before anything is deleted
  rmSync(root, { recursive: true, force: true });
  writeTree(root, files);
}
