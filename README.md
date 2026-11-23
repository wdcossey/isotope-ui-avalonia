# Isotope.UI.Avalonia

React-style function components and hooks for **Avalonia 12**, built on the engine of
[microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) (MIT), with an Avalonia-native API.

> **Status: research and spikes. Nothing is released and no packages are published.**
> An independent project, not affiliated with or endorsed by Microsoft or the Avalonia project.

## What it is

Microsoft.UI.Reactor brings a React-style model to WinUI 3: immutable element records, hooks (`UseState`, `UseEffect`, …),
keyed child diffing and a Yoga flexbox layout. It only targets WinUI. Isotope takes its platform-neutral engine and puts an
Avalonia 12 element, reconciler and hosting layer under it, using Avalonia's own types and vocabulary in the public API.

Package IDs use the `Isotope.` prefix, because `Avalonia.` is reserved on NuGet.

## How it stays in step with upstream

Upstream gets several commits a day. The approach, measured in four spikes (see [FINDINGS.md](FINDINGS.md)):

- **Three tiers of upstream code.**
  - **Tier 1, verbatim:** files that compile unmodified, such as Yoga and most of the data layer.
  - **Tier 2, verbatim plus deterministic transforms:** most of the engine, including hooks, child diffing and validation.
  - **Tier 3, rewritten for Avalonia and watched:** elements, the reconciler, control handlers and hosting.
- **Vendor and regenerate.** `tools/sync` copies tier 1 and 2 files from a git-ignored cache of upstream, at the commit
  pinned in [`UPSTREAM.lock`](UPSTREAM.lock), and applies the transform rules.
  - Vendored files are never edited by hand, and CI fails if they drift.
  - A sync is a regeneration, not a merge. Tier 3 gets a report of the upstream PRs to port by hand.
- **Upstream is treated as read-only.** We change nothing there and depend on no upstream changes.

## Repository layout

| Path | What |
|---|---|
| `FINDINGS.md` | The research results, the sync recommendation and the decisions taken |
| `docs/PROCESS.md` | How work runs: issues, branches, self-review, merging |
| `docs/spikes/` | Spike reports: A (engine compile), B (upstream churn), C (Uno and upstream), D (FlexPanel on Avalonia) |
| `docs/LESSONS.md`, `docs/agent-brief.md` | Traps already hit; the brief for parallel agents |
| `spikes/A-engine/` | Measures how much of upstream's engine compiles without WinUI |
| `spikes/D-flex/` | `FlexPanel` on Avalonia 12 over upstream's unmodified Yoga, with upstream's Yoga tests |
| `tools/` | Build-error grouping, the import rewrite (and `tools/sync` once built) |
| `UPSTREAM.lock` | The upstream commit everything is built and compared against |
| `upstream/` | Git-ignored cache of the upstream repo (you create it, see below) |

## Getting started

Prerequisites:
- the **.NET 11 SDK** (11.0.100 or later, pinned in `global.json`; projects target `net10.0`, the LTS);
- Node.js 20 or later;
- git.

Python 3 is optional, needed only for the Spike B scripts.

```sh
git clone <this repo> isotope && cd isotope
# Upstream cache, full history (Spike B needs it), checked out at the pinned commit:
git clone https://github.com/microsoft/microsoft-ui-reactor upstream
git -C upstream checkout "$(sed -n 's/^baseline=\([0-9a-f]*\).*/\1/p' UPSTREAM.lock)"

dotnet test spikes/D-flex/Reactor.Avalonia.Flex.Tests      # 794 tests: upstream's Yoga tests + FlexPanel
node spikes/A-engine/run-spike.mjs 3                        # Spike A: groups the engine's compile errors (567 recorded)
```

Run `dotnet` from the repo root, not from inside `upstream/`. Upstream's own `global.json` selects a different test runner.

## Working on it

- **Tasks** are GitHub Issues, with labels for priority, type and `self-drive`/`hands-on`/`blocked`.
- **Process:** [docs/PROCESS.md](docs/PROCESS.md). Agents (Claude Code) also read [CLAUDE.md](CLAUDE.md).
- **No personal data in the repo:** no names, emails, usernames, local paths or upstream contributors' names in files,
  outputs, screenshots or issues.

## Licence

To be decided for this project's own code. Upstream code vendored here stays under upstream's MIT licence, and its
copyright notice has to be kept with it.
