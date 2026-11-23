# Reactor → Avalonia: findings

Research project: an Avalonia 12 version of microsoft/microsoft-ui-reactor (MIT, WinUI 3 only).

**Updated 2026-11-22 with measured results from Spikes A to D.** Upstream baseline: `28a87225` (UPSTREAM.lock). The first version of this file (a grep survey) is in git history at `2c2baf3`; its estimates are replaced below.

Detailed reports:

| Report | Topic |
|---|---|
| `docs/spikes/A-engine-compile.md` | how much of the engine compiles without WinUI |
| `docs/spikes/B-upstream-churn.md` | how fast upstream moves, and where |
| `docs/spikes/C-uno-and-upstream-openness.md` | how Uno did it; how open upstream is |
| `docs/spikes/D-flex-slice.md` | FlexPanel on Avalonia over upstream's Yoga |

## Recommendation: how to stay in sync

**A separate repo that imports upstream files through a scripted, deterministic pipeline, sorted into three tiers, pulled weekly with a per-PR report.** Not a git-merge fork, not a hard fork, and not a core/platform split proposed upstream.

| Tier | What | How it syncs | Size today (measured) |
|---|---|---|---|
| **1. Verbatim** | Yoga, most of Data, the helper files that compile unmodified (including Navigation and Localization), upstream's Yoga tests | The import script diffs baseline → new upstream commit on these paths, copies them (or keeps linking them), builds, and runs upstream's own tests for them. | 74 files, 12,957 lines compile unmodified (Spike A); 780 upstream Yoga tests pass unmodified (Spike D) |
| **2. Verbatim + transforms** | Engine files that need aliases, shims, mechanical rewrites, our layer providing upstream's member names, or small edits turned into transforms: KeyedListDiff, most of Validation and Hooks, RenderContext's hooks core, ChildReconciler | The same import, plus deterministic transforms (today 3 regex rules; next a Roslyn-based "drop member / rename type" step). No hand patches. A transform that stops matching fails the import loudly. | 770 lines after the 3 rewrite rules, 7,703 lines verbatim given the seam and shims, and the 18 "needs edits" files (7,746 lines, 89 edit lines, 52 of them in 4 files) as candidates for transforms (Spike A) |
| **3. Rewritten, watched** | Element vocabulary, Reconciler, V1Protocol handlers, Hosting, FlexPanel, and RenderContext's ~25 WinUI hooks (window, theme, pickers, tray…) | Written fresh for Avalonia. The weekly sync report lists every upstream PR that touched them, because the compiler can't flag behaviour changes there. | the WinUI-heavy layer stubbed in Spike A: 66 files, 43,428 lines (split table in the Spike A report); 69% of engine PRs also change this layer (Spike B) |

**Why this shape:**

- **Upstream's engine is not a quiet core** (Spike B).
  - Engine files take 14% of `src/Reactor` churn: 35 commits and 5,067 lines in 13 weeks.
  - 28 of those 35 commits hit `RenderContext.cs` or `ChildReconciler.cs`, the two hottest engine files that need edits here (12 and 5 edit lines, Spike A). Six Hooks and Validation files need 16 more edit lines between them.
  - So the hot files need a mechanism that survives weekly change without hand patches. That is what transforms are for.
- **Text sync is cheap; semantic sync is the cost.**
  - A weekly engine-only pull is a median of 2 files and 63 lines; 10 of 13 weeks had any change at all.
  - But 69% of engine PRs also change the WinUI layer we rewrite, and only 11% touch no other source code.
  - Each pull means reading 2 or 3 PRs a week and porting their WinUI half by hand. *(Judgement: 1 to 3 hours a week while upstream keeps its current pace.)*
- **A git-merge fork like Uno's doesn't fit us** (Spike C).
  - Uno compiles 83% of `src/Reactor` with 5 patched files *because* it keeps WinUI's API.
  - With an Avalonia-native API, our tier 3 is a rewrite, and a merge-based fork would conflict on every upstream change to it.
  - Even Uno's best case is 13 days behind and has been stuck 11 days on 2 conflicts, one of them upstream's .NET 11 SDK bump.
- **A hard fork throws away about 380 engine lines a week** (Spike B): keyed-child fixes, a validation rework (#1270, the largest single share), and allocation, diagnostics and picker changes in RenderContext. We would be re-deriving those fixes ourselves.
- **Proposing a core/platform split upstream isn't realistic** (Spike C).
  - Upstream's specs reject an abstractions layer (062) and make anything but Win32 a non-goal (036).
  - Uno's port was sent off to be hosted elsewhere.
  - Decided 2026-11-23: we treat upstream as locked and send nothing upstream.
- **Paths are stable enough for path-based import:** no engine file has been renamed, moved, split or deleted since 2026-04-17 (Spike B). The import script still has to flag new and renamed files, and fail on includes that silently match nothing (a trap Uno hit: 74 files dropped with a green build).

**The price of tiers 1 and 2:** our Avalonia layer has to present upstream's *member names* to the engine: 226 members on 55 types, mostly `ElementModifiers` (54), `Reconciler` (33), `ReactorWindow` (16) and `Element` (10) (Spike A, `spikes/A-engine/seam.json`). The *value types* can be Avalonia's (`Thickness`, `IBrush`), so this fits decision 2 (Avalonia-native API) everywhere except RenderContext's public WinUI-typed hooks, which go to tier 3.

## Spike A: how much of the engine compiles without WinUI (measured)

A `net10.0` library links upstream files, never copies them. The WinUI-heavy layer is replaced by generated stubs; then come aliases, shims, a mechanical import rewrite, and finally edits.

- **The 50 candidate files (18,025 lines) aren't self-contained.** Followed to closure they pull in 165 files and 71,086 lines, 80 of which reference WinUI. The engine's input type is upstream's own `Element`, which shares `Element.cs` (8,141 lines) with about 160 WinUI-typed records.
- **With the WinUI layer stubbed,** 117 upstream files (29,176 lines) were linked:

| Outcome | Files | Lines |
|---|---|---|
| Compiles unmodified | 74 | 12,957 |
| After the mechanical rewrite (3 regex rules) | 4 | 770 |
| Verbatim, given our layer provides the seam | 15 | 5,818 |
| Verbatim plus WinUI-concept shims | 6 | 1,885 |
| Needs edits (89 edit lines) | 18 | 7,746 |
| Rewrite (`Hooks/UseAnnounce.cs`, a generated WinUI wrapper) | 1 | 173 |

- **73% of the linked lines need no edits.** Yoga's 5,046 lines are completely free.
- **The seam is 226 members on 55 types; the shim surface is 52 members on 14 WinUI-concept types** (pickers, `UISettings`, `DispatcherQueue`, focus/key args…).
- **Upstream's public hooks expose WinUI types** (`PickFileResult`, `FocusState`, `SystemBackdrop`, `Window` parameters), so a verbatim RenderContext would leak WinUI-named shims into our public API.
- **C# 15 files compile unmodified with the .NET 11 SDK** (pinned since 2026-11-22). Under the earlier net10 pin each `closed` hierarchy needed an edit; the step 3b total was the same either way (567).
- **The edit count is a lower bound:** implementing the seam will type-check more expressions.

## Spike B: how upstream moves (measured, 13 weeks to 2026-11-22)

- 334 mainline commits, all squash merges with PR numbers (3.6 a day).
- Engine (E) 35 commits and 5,067 lines; WinUI layer 96 commits and 20,286 lines; the rest of `src/Reactor` 10,626 lines; tests 148,478 lines.
- Engine churn is well down from the March to May build-out (6.6 commits a week) to 2.6 a week now, but rising again within the window: 6 commits in W35 to W40, 29 in W41 to W47.
- Hot files: RenderContext (16 commits), ChildReconciler (12), KeyedListDiff (3). Yoga had one PR, a facebook/yoga resync. FlexPanel had none.
- Coupling: 69% of engine commits also touch the WinUI layer.
- First real sync delta (`28a87225` → `14172dce`): 2 commits, nothing in `src/`.

## Spike C: Uno, and upstream's openness (researched)

- **The "Reactor runs on Uno" claim holds:** Uno Platform 7.0 ships it, in preview. Microsoft didn't retarget Reactor, though: **Uno maintains a fork** (`unoplatform/Uno.microsoft-ui-reactor`), shipped as `Uno.Microsoft.UI.Reactor`.
- **Its sync mechanics:** a nightly merge bot, a "don't modify Microsoft's files" rule with a patch ledger, build checks against silently-empty includes, and triage of new `Hosting/` files. Worth copying (MIT).
- **Upstream's stance:** the maintainers asked Uno to host the port itself. Specs 062 and 036 reject in-repo abstraction and non-Win32 windowing. Upstream's own `docs/research/compare/avalonia.md` treats Avalonia as a competitor.

## Spike D (reduced): FlexPanel on Avalonia 12 (built and run)

- Upstream Yoga (10 files, 5,046 lines) is linked verbatim under a 415-line Avalonia port of `FlexPanel.cs` (978 lines upstream).
- **780 of upstream's Yoga tests (35 of 36 files, linked verbatim) pass on net10.0 with no WinUI.**
- **14 Avalonia headless FlexPanel tests pass**, 794 in all. 11 of 11 behaviours were proven to fail when broken. A clean clone of `main` at `7f5670f` on SDK 11.0.100 passes 794 of 794, and re-runs Spike A to exactly 567 errors.
- **Leaks:**
  - An explicit `Width` beats flex-grow at Arrange (the same as upstream on WinUI).
  - A child's own Width, Height, Margin or Visibility change doesn't reach the panel by itself in Avalonia, where WinUI re-measures the parent. The port registers those properties with `AffectsParentMeasure`; review pass 2 found this.
  - Avalonia 12 lays out on the render tick, so headless tests pump the dispatcher.
  - Avalonia.Headless.XUnit needs xunit v3 3.2.x, while upstream is on 4.0.3.
- **Not run:** the hooks, controlled `TextBox` and keyed-list half of the slice. It needs our layer to implement the Spike A seam first. See "Decisions taken while you were away".

## Still true from the first survey

- **WinUI is in the public contract:** `IElementHandler<TElement,TControl> where TControl : UIElement`; `Reconciler.Mount` returns `UIElement?`; `Element.cs` records carry WinUI types. There is no plug-in route.
- **Friction points for the rewritten layer:**
  - The property model: DependencyProperty → StyledProperty/DirectProperty; change-echo suppression; pooled-control reset.
  - Control gaps: NavigationView, TabView, ItemsView, ItemsRepeater and InfoBar have no 1:1 Avalonia equivalents.
  - Styling and theming: Avalonia selectors and ControlTheme vs ThemeResource.
  - Hosting: window, title bar, backdrop and MSIX are Windows-specific.
- **Other upstream projects:** Reactor.Advanced (44k lines, Win2D/WinAppSDK), Devtools 9.6k, Analyzers 22k, Cli 11k, Wrappers.Generator 3.5k. All are out of scope so far.
- `src/Reactor.Wrappers.Generator` might generate much of the element/handler layer from Avalonia metadata. Still unexamined.

## Decisions taken while you were away (from 2026-11-22 23:09)

| What | Options | Chosen | Why |
|---|---|---|---|
| Spike D scope | full slice (hooks, TextBox, keyed list, FlexPanel) / FlexPanel and Yoga only | **FlexPanel and Yoga only** | The engine half needs the 226-member seam implemented; it wouldn't fit, and a half-done seam would measure nothing |
| Review depth for spike tooling (A) | two passes (high, medium) / one pass at medium as chore | **one pass, medium** | measurement tooling, not product code; all 9 findings fixed and re-measured |
| Review of the B and C report docs | one medium pass / checked against the agents' output | **checked by hand, pass waived** | they transcribe agent measurements; recorded in the merge commit |
| Spike D review | per PROCESS: high then medium | **as PROCESS** | it ports product code with tests |
| xunit version for Spike D | upstream's 4.0.3 / 3.2.2 | **3.2.2** | what Avalonia.Headless.XUnit 12.1.6 is built against; upstream tests ran unchanged |
| Screenshots | real window / headless render | **headless render** | a real window is ask-first |
| SDK after you installed 11.0.100 | keep the net10 pin / pin 11.0.100 | **pinned 11.0.100**, targets stay `net10.0` | you had said you would install it; it was open question 3; upstream and Uno both hinge on it |
| Spike D review findings on behaviour inherited from upstream (ambient Yoga mode) | fix in our port / keep upstream parity | **keep parity**, logged as upstream-fix candidates | a tier 3 file is easier to watch when it diffs cleanly against upstream |

## Decisions on the open questions (2026-11-23)

1. **Three-tier import strategy: adopted.**
2. **Upstream's member names on the engine-facing seam, with Avalonia value types: adopted.**
3. **Next session:** the engine half of Spike D (a minimal Avalonia `Reconciler`/`Element`/`ElementModifiers` implementing the seam, then `UseState` plus a button, a controlled `TextBox` and a keyed list) and a `tools/sync` import script. Agreed.
4. **GitHub remote:** you will create a private repo; then add it as `origin` and push.
5. **Upstream is treated as locked (user, 2026-11-23):** substantial changes are unlikely to be accepted, so we plan as if we can't change upstream at all. We send no contributions or issues.
   - Every gap is ours to absorb: transforms on import, the tier 3 rewrite, or fixes in our own port.
   - The two inherited FlexPanel ambient-mode bugs get fixed in our port (tier 3) if we hit them, and are noted as a divergence from upstream.
   - The earlier list of upstream-change candidates is withdrawn.
6. **Targets stay `net10.0` (LTS);** the .NET 11 SDK is installed for the C# 15 compiler only.
7. **Upstream model: vendor and regenerate.** There is no private fork and no submodule.
   - `upstream/` is a git-ignored cache.
   - Tier 1 and tier 2 files are vendored into our repo after transforms, and never edited by hand. CI regenerates them from the pinned commit and fails on any difference.
   - A sync is a regeneration, not a merge. A transform that stops matching fails loudly, and the tier 3 paths get a report of the upstream PRs that touched them.
   - Why not a fork: a fork of a public repo can't be private on GitHub, and a merge-based fork only fits a tree that mirrors upstream's (Uno's case, not ours).
   - Why not a submodule: `UPSTREAM.lock` already pins the commit, and a submodule adds friction and keeps builds dependent on upstream.
   - The harness is #3, and #6 runs the regeneration check.

## Repo state at the end of the session

- Everything is merged into `main` (this file included); no other branches or worktrees were left. The clean run above is of `7f5670f`, the commit before this file.
- Merged: the repo setup, Spike A (plus review fixes), the Spike B and C reports, the .NET 11 SDK pin, and Spike D (plus two review rounds).
- `upstream/` is still at `28a87225` with a clean status. Its `origin/main` has been fetched to `14172dce`.

## Decisions after the seam (2026-11-23)

1. **Namespaces are `Isotope.UI.*`.** One transform rule renames upstream's `Microsoft.UI.Reactor.*`, so the package ID and the
   namespaces match. `Avalonia.UI.Isotope` was considered but rejected, because the package ID can't start with `Avalonia.`.
2. **`RenderContext` is made `partial` by a rule**, for our implementation of the tier 3 hooks.
3. **Text-based member drops stay** until #4.
4. **Merged branches are deleted from the remote**; only `main` remains.

## Seam (issue #1, 2026-11-23)

- **Upstream's `ChildReconciler.cs`, `Internal/KeyedListDiff.cs` and `RenderContext.cs` compile against our Avalonia
  seam with transforms only:**
  - Spike A's edit lines for them went from 12 + 5 + 0 to **0 + 0 + 0**.
  - `KeyedListDiff` needs no rule.
  - `ChildReconciler` needs one rule (two `using` lines).
  - `RenderContext` needs 37 member drops: 889 lines of tier 3 hooks, while the hooks core is untouched.
- **The library** `src/Isotope.UI.Avalonia` links 65 upstream files (16,062 lines) with 74 rules: 7 find/replace and 67
  member drops. Measured with `node tools/measure-transforms.mjs src/Isotope.UI.Avalonia/UpstreamFiles.props
  src/Isotope.UI.Avalonia/upstream-transforms.json upstream/src/Reactor`.
- **Tests:** 48 headless tests of the seam and the hooks core (render, state, effects, context, memo, keyed reorder, the
  render-loop guard, off-thread re-renders) and 12 tests of the rewrite tool. All 60 were proven to fail with their
  behaviour broken.
- **Still to come:** the library links upstream at build time with its own rule file, while `tools/sync` vendors with
  another. Unifying them is #11.

## Tracking

Open work is tracked in GitHub Issues (since 2026-11-23): #1 seam types (done 2026-11-23), #2 vertical slice, #3 `tools/sync` (done 2026-11-23: 155 files vendored, first sync to `14172dce`), #4 Roslyn transforms, #5 first real sync, #6 CI, #7 PR process (hands-on), #8 wrappers-generator research, #9 FlexPanel ambient-mode divergence.

## Not done

- The engine half of Spike D (see above).
- No real merge conflicts were measured: nothing has been synced yet. Spike B used a proximity proxy.
- The wrappers generator is unexamined.

## Token use

| Work | Tokens |
|---|---|
| Spike B agent | 164k |
| Spike C agent | 204k |
| Spike A review | about 80k |
| Spike D reviews | pass 1 about 79k, pass 2 about 71k |
| SDK-switch review | about 54k |
| FINDINGS review | about 67k |
| This session's own work | not measured |

## Sources

- https://github.com/microsoft/microsoft-ui-reactor (clone at `upstream/`)
- https://platform.uno/blog/uno-platform-7-0/ and https://platform.uno/docs/articles/features/using-reactor.html
- https://github.com/unoplatform/Uno.microsoft-ui-reactor
- https://infoq.com/news/2026/11/uno-platform-6-7-targets
- https://docs.avaloniaui.net/docs/avalonia12-breaking-changes, https://avaloniaui.net/blog/avalonia-12, https://avaloniaui.net/blog/release-12-1
