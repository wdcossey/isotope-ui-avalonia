#!/usr/bin/env node
// Spike F (#12): measures the descriptor runtime port and the generated descriptors, as the report quotes them.
// Usage (from anywhere): node spikes/F-descriptors/measure.mjs
//   1. Runtime: tools/measure-transforms.mjs over the vendored runtime files the library compiles.
//   2. Generator: lines added and removed by the transform rules in each vendored generator file (git diff
//      --no-index against upstream at UPSTREAM.lock, the vendored header line left out), and the rules that hit them.
//   3. Descriptors: builds the library with the generated sources written out, and counts generated lines against
//      the hand-written records, Customize bodies and the tier 3 runtime code.
// Needs the upstream/ cache. Writes only under spikes/F-descriptors/obj/.
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseLock } from '../../tools/sync/lib/lock.mjs';
import { applyTransforms, loadTransforms } from '../../tools/sync/lib/transforms.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.join(here, '..', '..');
const obj = path.join(here, 'obj', 'measure');
rmSync(obj, { recursive: true, force: true });
mkdirSync(obj, { recursive: true });
const { baseline } = parseLock(readFileSync(path.join(repo, 'UPSTREAM.lock'), 'utf8'));
const upstream = path.join(repo, 'upstream');
const lines = (s) => s.replace(/\r\n/g, '\n').replace(/\n$/, '').split('\n').length;

// 1. Runtime files.
const runtime = [
  'Core/V1Protocol/Descriptor/ControlDescriptor.cs', 'Core/V1Protocol/Descriptor/PropEntry.cs', 'Core/V1Protocol/ChildrenStrategy.cs',
  'Core/V1Protocol/ControlRegistry.cs', 'Core/V1Protocol/Descriptor/DescriptorHandler.cs', 'Core/V1Protocol/MountContext.cs',
  'Core/V1Protocol/IElementHandler.cs', 'Core/V1Protocol/IDecoratorElementHandler.cs', 'Core/V1Protocol/V1HandlerAdapter.cs',
  'Core/V1Protocol/ItemsHostReconciliation.cs', 'Core/V1Protocol/ReportedUnmountFailureException.cs', 'Core/ChangeEchoSuppressor.cs',
  'Core/PoolPolicy.cs', 'Core/ReactorBinding.cs', 'Core/V1HandlerRegistry.cs', 'Core/Optional.cs',
];
console.log('## Runtime (tools/measure-transforms.mjs)');
console.log(execFileSync('node', [path.join(repo, 'tools', 'measure-transforms.mjs'), path.join(repo, 'src', 'Isotope.UI.Avalonia', 'UpstreamFiles.props'), ...runtime],
  { encoding: 'utf8' }).trim());

// 2. Generator files.
console.log('\n## Generator (git diff --numstat, vendored against upstream ' + baseline.slice(0, 8) + ')');
const manifest = JSON.parse(readFileSync(path.join(repo, 'src', 'Upstream', 'vendored.json'), 'utf8'));
const gen = manifest.files.filter((f) => /^src\/Reactor\.(Wrappers\.Generator|SourceMap\.Generator)\//.test(f.path));
const globRules = loadTransforms(JSON.parse(readFileSync(path.join(repo, 'tools', 'sync', 'transforms.json'), 'utf8'))).filter((r) => r.glob);
const numstat = (x, y) => {
  const a = path.join(obj, 'a.cs'), b = path.join(obj, 'b.cs');
  writeFileSync(a, x.replace(/\r\n/g, '\n'));
  writeFileSync(b, y.replace(/\r\n/g, '\n'));
  const d = spawnSync('git', ['diff', '--no-index', '--numstat', a, b], { encoding: 'utf8' }).stdout.trim();
  return d ? d.split(/\s+/).slice(0, 2).map(Number) : [0, 0];
};
let added = 0, removed = 0, upLines = 0, ownAdded = 0, ownRemoved = 0;
const rulesHit = new Set();
for (const f of gen) {
  const up = execFileSync('git', ['-C', upstream, 'show', baseline + ':' + f.path], { encoding: 'utf8', maxBuffer: 1 << 26 });
  const vendored = readFileSync(path.join(repo, 'src', 'Upstream', ...f.path.split('/')), 'utf8').replace(/^[^\n]*\n/, '');
  const [ad, rm] = numstat(up, vendored);
  // The same file with only the repo-wide rules (the rename), so the listed rules' own lines can be told apart.
  const renamed = applyTransforms(globRules.filter((r) => r.globRe.test(f.path)), f.path, up).text;
  const [oa, or] = numstat(renamed, vendored);
  added += ad; removed += rm; ownAdded += oa; ownRemoved += or; upLines += lines(up);
  f.transforms.forEach((t) => rulesHit.add(t));
  console.log(f.path + ': ' + lines(up) + ' lines; tier ' + f.tier + '; +' + ad + ' -' + rm + ' (beyond the rename: +' + oa + ' -' + or + '); rules: ' + f.transforms.join(', '));
}
const listed = [...rulesHit].filter((r) => r.startsWith('gen-'));
console.log('all ' + gen.length + ' files: ' + upLines + ' lines; +' + added + ' -' + removed + ' (beyond the rename: +' + ownAdded + ' -' + ownRemoved + '); ' + rulesHit.size + ' rules hit them (' +
  listed.length + ' gen-* rules, ' + (rulesHit.size - listed.length) + ' repo-wide)');

// 3. Generated and hand-written descriptor lines.
console.log('\n## Descriptors');
const genDir = path.join(obj, 'generated');
const build = spawnSync('dotnet', ['build', path.join(repo, 'src', 'Isotope.UI.Avalonia'), '-nologo', '-v:q', '--no-incremental',
  '-p:EmitCompilerGeneratedFiles=true', '-p:CompilerGeneratedFilesOutputPath=' + genDir], { encoding: 'utf8', cwd: repo });
if (build.status !== 0) throw new Error('library build failed:\n' + build.stdout);
const walk = (d) => readdirSync(d, { withFileTypes: true }).flatMap((e) => e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]);
const generated = walk(genDir).filter((f) => f.endsWith('.Descriptor.g.cs')).sort();
let genTotal = 0;
for (const f of generated) { const n = lines(readFileSync(f, 'utf8')); genTotal += n; console.log('generated ' + path.basename(f) + ': ' + n + ' lines'); }
console.log('generated, ' + generated.length + ' files: ' + genTotal + ' lines');
const records = readFileSync(path.join(repo, 'src', 'Isotope.UI.Avalonia', 'Seam', 'ControlElements.cs'), 'utf8');
const customize = [...records.replace(/\r\n/g, '\n').matchAll(/\n( *)private static partial ControlDescriptor[\s\S]*?;\n/g)].map((m) => lines(m[0].trim()));
console.log('hand-written records (Seam/ControlElements.cs): ' + lines(records) + ' lines, of which ' + customize.length + ' Customize bodies: ' +
  customize.reduce((s, n) => s + n, 0) + ' lines (' + customize.join(', ') + ')');
const tier3 = ['src/Isotope.UI.Avalonia/Seam/Reconciler.V1.cs', 'src/Isotope.UI.Avalonia/Seam/V1Protocol/ReactorBinding.cs',
  'src/Isotope.UI.Avalonia/Seam/V1Protocol/ControlEventPayloads.cs', 'src/Isotope.UI.Avalonia/Seam/V1Protocol/ItemsList.cs',
  'src/Isotope.UI.Avalonia.Generators/WrapperGenerator.Avalonia.cs'];
for (const f of tier3) console.log('ours ' + f + ': ' + lines(readFileSync(path.join(repo, ...f.split('/')), 'utf8')) + ' lines');
