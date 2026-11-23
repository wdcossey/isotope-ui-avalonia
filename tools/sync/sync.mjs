#!/usr/bin/env node
// tools/sync: vendors tier 1 and 2 upstream files into src/Upstream (after transforms), checks them, and writes
// the sync report. Run "node tools/sync/sync.mjs --help" for usage. Tier map: tiers.json. Transforms:
// transforms.json. Settings: sync.json. Personal-data allowlist: personal-data.json. Tests: node tools/sync/test/all.mjs.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { run } from './lib/run.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
process.exitCode = run(process.argv.slice(2), { defaultRepo: path.join(here, '..', '..') });
