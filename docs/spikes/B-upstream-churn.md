# Spike B: how fast upstream moves, and where

Measured 2026-11-22 by a research agent (Opus) with read-only git on `upstream` (`origin/main` = `14172dce`, history from 2026-03-18). Scripts and their full outputs (per-week and per-month tables for every area): `B-scripts/` (`results.md`, `results2.md`, `extra_out.txt`, `conflict_proxy_out.md`). **(judgement)** marks readings rather than measurements.

## Method

- Mainline commits only: `git -c diff.renameLimit=50000 log origin/main --first-parent --diff-merges=first-parent -M --numstat`. 1,244 mainline commits over the whole history (2,392 in all); the last merge commit was 2026-08-01, everything since is a squash merge.
- Window: 2026-08-22 to 2026-11-22 = ISO weeks W35-W47, 334 commits. Lines = added + deleted.
- Bulk and generated files excluded (`package-lock.json`, stress-perf traces, vendored Monaco, `*.api.txt`, generated Yoga/Md4c tests, search index, baselines): 49.6k of 309k lines in the window. None is an engine or WinUI file.
- Areas (sizes at `origin/main`):

| Area | What | Files | Lines |
|---|---|---|---|
| E1 | `Core/RenderContext.cs` (no partials, ever) | 1 | 3,153 |
| E2 | `Core/ChildReconciler.cs`, `Core/Internal/KeyedListDiff.cs` | 2 | 2,392 |
| E3 | `Yoga/` except `FlexPanel.cs` | 10 | 5,046 |
| E4 | `Hooks/` | 12 | 2,768 |
| E5 | `Data/`, `Controls/Validation/` | 25 | 4,667 |
| **E** | engine candidates | **50** | **18,026** |
| W1 | `Core/Reconciler*.cs` + `V1Protocol/Reconciler.TemplatedTree.cs` | 9 | 15,300 |
| W2 | `Core/V1Protocol/` + `Core/V1HandlerRegistry.cs` | 51 | 12,495 |
| W3 | `Core/Element*.cs`, `Elements/` | 21 | 15,921 |
| W4 | `Hosting/` | 64 | 18,488 |
| W5 | `Yoga/FlexPanel.cs` | 1 | 978 |
| O1 | rest of `src/Reactor` | 155 | 27,130 |

## Results

### Window totals (13 weeks)

| Area | Commits | Added / deleted | Lines per week | Churn ÷ size |
|---|---|---|---|---|
| E1 RenderContext | 16 | 622 / 218 | 63 | 27% |
| E2 child diffing | 14 | 975 / 234 | 91 | 51% |
| E3 Yoga | 1 | 665 / 90 | 57 | 15% |
| E4 Hooks | 6 | 80 / 46 | 9 | 5% |
| E5 Data/Validation | 4 | 2,056 / 81 | 161 | 46% (one PR, #1270) |
| **E (distinct commits)** | **35** | **4,398 / 669** | **381** | **28%** |
| W (distinct commits) | 96 | 17,224 / 3,062 | 1,527 | 32% |
| O1 | 65 | 9,698 / 928 | 800 | 39% |
| tests | 202 | 136,288 / 12,190 | 11,176 | |
| docs | 153 | 23,851 / 7,620 | 2,369 | |

The engine is 14% of `src/Reactor` churn and 16.6% of its size: it is not a quiet core. W5 (FlexPanel) had no commits.

### Trend (lineage view, following renames)

| Period | All commits/week | E commits/week | E lines/week | W lines/week |
|---|---|---|---|---|
| 03-18 to 05-21 | 51.8 | 6.6 | 1,708 | 6,472 |
| 05-22 to 08-21 | 32.6 | 1.9 | 276 | 4,679 |
| 08-22 to 11-22 | 25.1 | 2.6 | 381 | 1,527 |

E commits per month: Mar 11, Apr 33, May 21, Jun 15, Jul 5, Aug 5, Sep 1, Oct 16, Nov (to 22nd) 14. Engine churn is about 4.5× below the March-May build-out but rising again (6 engine commits in W35-W40, 29 in W41-W47). **(judgement)** From the subjects, the rise is keyed-child fixes and diagnostics/perf/picker features reaching into RenderContext, not new engine features.

### Hot files and coupling

| File | Commits (13 wk) | +/- | Also touched W |
|---|---|---|---|
| Core/RenderContext.cs | 16 | 622 / 218 | 12 |
| Core/ChildReconciler.cs | 12 | 670 / 209 | 11 |
| Core/Internal/KeyedListDiff.cs | 3 | 305 / 25 | 3 |
| Yoga (7 files, one PR #1466, a facebook/yoga resync) | 1 | 665 / 90 | 0 |
| Controls/Validation (6 files, mostly PR #1270) | 3 | 2,011 / 73 | 3 |
| Hooks (8 files) | 1-3 each | 80 / 46 | 4 |

26 of 50 engine files changed. 28 of the 35 engine commits touched RenderContext or ChildReconciler. **69% of engine commits (24 of 35) also changed WinUI-layer files; only 4 (11%) touched no other source code** (12 of 89, 13%, over the whole history). The self-contained ones: the Yoga sync (#1466), two RenderContext allocation fixes (#1580, #1604), a keyed-reorder fix (#1611). 25 of 35 also touched O1 (Component.cs, ElementFactory.cs, ElementPool.cs, InfiniteResource.cs, diagnostics).

### Renames, moves, splits

- Last 13 weeks: 33 renames in 4 commits, none in an engine or WinUI file.
- Whole history: engine files moved only in the repo-wide reorganisations of 2026-03-18 and 2026-04-16/17 (Patch → Duct → Microsoft.UI.Reactor, `Reactor/` → `src/Reactor/`), all detected at 99-100% similarity by `-M`. **No engine file renamed, moved, split or deleted since 2026-04-17.**
- New files do appear in engine folders (8 so far, all new code, e.g. KeyedListDiff, UseMemoCells, ValidationRenderScope on 2026-09-28).
- The WinUI layer and O1 keep splitting into partials (Reconciler.DragDrop/.Gestures/.KeyedItemsBinding/.TemplatedTree/.RichTextScrollAnchor/.Diagnostics; ReactorDiagnostics.*, ReactorSourceMap.*).

### How commits land

| Measure | Window | Whole history |
|---|---|---|
| Mainline commits | 334 | 1,244 |
| Squash commits with `(#NNN)` | 334 | 865 |
| Median / p75 / p90 lines per commit | 210 / 835 / 2,081 | 302 / 1,028 / 2,502 |
| Median files per commit | 6 | 5 |
| Commits touching src/Reactor | 121 | 392 |
| Commits per calendar day | 3.6 | 4.98 |
| Authors | 16 | 31 |

Top authors in the window: one maintainer 190, dependabot 85.

### First real sync delta: `28a87225` → `14172dce`

Two commits (#1648, #1653): 21 test files (+6,939/−4), 1 doc, 5 CI/build files. **Nothing in `src/`.** An engine-restricted sync of this delta is empty.

### Simulated weekly pull of the engine paths

`git diff -M --numstat` between the last mainline commit before each Monday, restricted to the E paths:

| Week | Commits | Files | +/- | Hunks | Hunks ≤3 lines from a WinUI line |
|---|---|---|---|---|---|
| W35 | 2 | 3 | 37 / 20 | 15 | 1 |
| W36 | 2 | 2 | 20 / 6 | 9 | 6 |
| W37-W39 | 0 | 0 | 0 | 0 | 0 |
| W40 | 2 | 7 | 2,119 / 110 | 115 | 6 |
| W41 | 2 | 6 | 51 / 14 | 16 | 0 |
| W42 | 1 | 1 | 29 / 1 | 3 | 0 |
| W43 | 7 | 2 | 377 / 117 | 62 | 12 |
| W44 | 5 | 2 | 732 / 118 | 77 | 17 |
| W45 | 6 | 12 | 789 / 133 | 99 | 0 |
| W46 | 3 | 4 | 58 / 5 | 16 | 1 |
| W47 | 5 | 3 | 66 / 25 | 22 | 1 |

10 of 13 weeks non-empty; **median 2 files and 63 lines a week**, mean 371, max 2,229. 44 of 434 hunks (10%) were within 3 lines of a WinUI-referencing line; for ChildReconciler, 37 of 105 (35%).

## Reading (judgement)

- A typical week: 0-3 squash PRs touch engine files, usually 1-4 files and under 100 lines, nearly always RenderContext or ChildReconciler. About 3 weeks a quarter bring 500-2,200 lines concentrated in one area.
- Text conflicts should be rare if aliases and shims carry the WinUI differences; ChildReconciler is the exception.
- **The real cost is semantic:** 69% of engine PRs also change the WinUI layer, so a clean text merge can leave our Avalonia reconciler/handlers out of step. Each pull means reading 2-3 PRs a week; squash merges with PR numbers make that tractable.
- Path-based sync is safe today: include whole directories, flag added/renamed/deleted paths, follow renames with `-M`.
- Yoga can be taken verbatim.

## Limits

- No real merge conflicts measured (no patched files existed yet); section "weekly pull" uses a proximity proxy.
- Week boundaries at midnight UTC-8.
