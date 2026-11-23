// Step 5: build and run upstream's own tests for the vendored paths, against a given vendored tree.
import { spawnSync } from 'node:child_process';
import path from 'node:path';

/** Reads the counts from a dotnet test summary (VSTest, or Microsoft.Testing.Platform). Null if there are none. */
export function parseTestCounts(output) {
  let m = /Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/.exec(output);
  if (m) return { failed: +m[1], passed: +m[2], skipped: +m[3], total: +m[4] };
  m = /total:\s*(\d+)\s+failed:\s*(\d+)\s+succeeded:\s*(\d+)\s+skipped:\s*(\d+)/i.exec(output);
  if (m) return { total: +m[1], failed: +m[2], passed: +m[3], skipped: +m[4] };
  return null;
}

export function runVerify(repoRoot, steps, vendoredRoot, log = console.log) {
  const results = [];
  const errors = [];
  for (const s of steps) {
    const project = path.join(repoRoot, ...s.project.split('/'));
    log('verify ' + s.name + ': dotnet test ' + s.project);
    const r = spawnSync('dotnet', ['test', project, '-nologo', '-p:VendoredRoot=' + vendoredRoot.replace(/\\/g, '/').replace(/\/?$/, '/')],
      { cwd: repoRoot, encoding: 'utf8', maxBuffer: 1 << 28 });
    const output = (r.stdout ?? '') + (r.stderr ?? '');
    if (r.error) { errors.push('verify ' + s.name + ': could not run dotnet: ' + r.error.message); continue; }
    const counts = parseTestCounts(output);
    if (r.status !== 0 || !counts || counts.failed || !counts.total) {
      const tail = output.split(/\r?\n/).filter((l) => /error|fail/i.test(l)).slice(0, 20).join('\n');
      errors.push('verify ' + s.name + ': ' + (counts ? counts.failed + ' failed, ' + counts.passed + ' passed of ' + counts.total : 'no test counts in the output') +
        ' (exit ' + r.status + ')' + (tail ? '\n' + tail : ''));
    }
    results.push({ name: s.name, ...(counts ?? { passed: 0, failed: 0, skipped: 0, total: 0 }) });
  }
  return { results, errors };
}
