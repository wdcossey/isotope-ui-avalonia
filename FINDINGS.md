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
| **2. Verbatim + transforms** | Engine files that need aliases, shims, mechanical rewrites, our layer providing upstream's member names, or small edits turned into transforms: KeyedListDiff, most of Validation and Hooks, RenderContext's hooks core, ChildReconciler | The same import, plus deterministic transforms (text rules, plus an optional Roslyn step, #4). No hand patches. A transform that stops matching fails the import loudly. | 770 lines after the 3 rewrite rules, 7,703 lines verbatim given the seam and shims, and the 18 "needs edits" files (7,746 lines, 89 edit lines, 52 of them in 4 files) as candidates for transforms (Spike A) |
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
| Spike D review findings on behaviour inherited from upstream (ambient Yoga mode) | fix in our port / keep upstream parity | **keep parity**, logged as upstream-fix candidates (superseded by #9: fixed in our port as an intended divergence) | a tier 3 file is easier to watch when it diffs cleanly against upstream |

## Decisions on the open questions (2026-11-23)

1. **Three-tier import strategy: adopted.**
2. **Upstream's member names on the engine-facing seam, with Avalonia value types: adopted.**
3. **Next session:** the engine half of Spike D (a minimal Avalonia `Reconciler`/`Element`/`ElementModifiers` implementing the seam, then `UseState` plus a button, a controlled `TextBox` and a keyed list) and a `tools/sync` import script. Agreed.
4. **GitHub remote:** you will create a private repo; then add it as `origin` and push.
5. **Upstream is treated as locked (user, 2026-11-23):** substantial changes are unlikely to be accepted, so we plan as if we can't change upstream at all. We send no contributions or issues.
   - Every gap is ours to absorb: transforms on import, the tier 3 rewrite, or fixes in our own port.
   - The two inherited FlexPanel ambient-mode bugs get fixed in our port (tier 3) if we hit them, and are noted as a divergence from upstream.
   - The earlier list of upstream-change candidates is withdrawn.
6. **Targets stay `net10.0` (LTS);** the .NET 11 SDK is installed for the C# 15 compiler only. *(Since #67, 2026-11-25:
   the packages target `net9.0`, `net10.0` and `net11.0`.)*
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

## ListView on a ListBox (issue #94, 2026-11-26)

- **What landed:** Reactor's ListView is Avalonia's ListBox. `ListBoxElement.Items` is Avalonia's: strings, or elements
  reconciled as a panel's children (keyed, in place, moved, a component's state kept) through upstream's items host.
  Factories `ListBox(params Element?[])`, `ListBox(selectedIndex, onSelectedIndexChanged, items...)` (int, int?,
  Optional<int>), typed `ListBox<T>(items, keySelector, itemTemplate)` (and the IReactorKeyed form) as `ListBoxElement<T>`
  with typed OnItemClick and OnSelectionChanged, and `.SelectionMode(...)`.
- **Selection:** a controlled SelectedIndex as upstream's (written when the control differs); only the list's own
  SelectionChanged is reported (it bubbles); a keyed move keeps its items selected and reports their new indexes once the
  items are in place; a removed selected item is reported as -1. OnItemClick (kept: Avalonia has no item-click event)
  fires on a tap or double tap on an item (not a focusable control in it) and Enter or Space on a focused item, after the
  tap's selection.
- **Selection modes, measured with headless clicks:** Reactor's Single is Single, Extended is Multiple (Ctrl and Shift
  add), Multiple is Multiple | Toggle (each tap toggles); None has no counterpart (ISOMIG005 with what to do). The
  migration writes flag combinations in parentheses, in patterns too, and the first flag in nameof.
- **Measured** (`ListBoxMeasurements`, 300-high list, headless, net10.0, three invocations of three runs): 10,000 element
  rows realise 8 containers, mount in 256 to 568 ms (10,000 strings: 2.4 to 44 ms), re-render equal in 23 to 69 ms, and
  reverse by key in 283 to 429 ms keeping every row control. Each move is one `AvaloniaList.Move` (ListBoxItemsCollection);
  upstream's RemoveAt + Insert took 2.2 to 2.5 s, from a logical-children scan per edit. A moved row in view is detached
  and attached again (Avalonia's VirtualizingStackPanel recycles on Move), so focus in it is lost.
- **Unlike upstream's:** every element item is mounted (upstream mounts only the realised ones and rebuilds them when the
  items array changes, losing their state).
- **Samples** (`--write-baseline`): ISOMIG012 106 → 94, compile errors 2,912 → 2,896, diagnostics left 1,099 → 1,090
  (ISOMIG005 +3 for None, ISOMIG010 +1 for Header).
- **Tests:** 21 library tests and 3 migration tests, each proven to fail (31 deliberate breaks).
- **Not done:** lazy mounting for long element lists; GridView (a WrapPanel items panel), ItemsView, VirtualList and
  LazyVStack.

## Icons (issue #91, 2026-11-26)

- **What landed:** upstream's `IconData` union with Avalonia's value types (`PathIconData`, `FontIconData`, `BitmapIconData`,
  `ImageIconData`) and `IconElement(IconData Data)`, with Reactor's factories `Icon`, `PathIcon`, `FontIcon`, `BitmapIcon` and
  `ImageIcon` (kept entries in `renames.json`: Avalonia's core has no glyph, bitmap or image icon). A hand-written
  `IElementHandler` (`[WrapPolymorphic]` is ISOTOPEGEN004) chooses the control from the data's kind: Avalonia's `PathIcon`
  (markup parsed by `Geometry.Parse`), a text block centring the glyph, an internal bitmap icon (an Avalonia `IconElement`
  that draws the bitmap, or with `ShowAsMonochrome` masks the foreground with its opacity) and Avalonia's `Image`. The same
  kind is patched in place; another kind mounts a new control (`Reconciler.CanUpdate` declines it).
- **Avalonia 12.1.6, measured:** its core's only icon controls are `IconElement` and `PathIcon`. In the Fluent theme a
  `PathIcon` is 20 by 20 and scales its shape to its size. A glyph keeps WinUI's defaults (Segoe Fluent Icons, then Segoe
  MDL2 Assets, at 20) under its own font and size and the modifiers'; the Segoe fonts exist on Windows only. Avalonia's
  string-to-`FontFamily` conversion throws on null, so `FontIcon` and `FontIconData` also take a font name, where null is
  the default.
- **Unlike upstream's:** no `SymbolIconData`, `SymbolIcon`, `Icon(Symbol)` or `Icon(string)`: WinUI's symbols are Segoe
  glyphs, which can't be redistributed. The migration reports them (ISOMIG005, ISOMIG011, ISOMIG010) with what to use
  instead. **Decided** (developer, 2026-11-26): Isotope ships no symbol set or font, Fluent UI System Icons included;
  the guide ("WinUI symbols") says how to use Segoe Fluent Icons on Windows (`FontIcon(glyph, "Segoe Fluent Icons")`)
  or an app's own icons, and the migration's notes point there. The sample's tabs became a page list (#96), so the tab row no longer sets the window's width. A bitmap or image URI is a file or `avares://` resource;
  a command's `Icon` takes the new records but nothing draws it yet. `IconElement` clashes with
  `Avalonia.Controls.IconElement` where both namespaces are imported (CS0104).
- **Measured on upstream's samples** (`measure-samples.mjs`, 36 projects, 358 files, upstream 561c5573), main `c0b2e19`
  against #91: files that build 108 and 108; compile errors 2,936 → 2,912; diagnostics left 1,122 → 1,099; ISOMIG012
  136 → 106 (`Icon` 14, `FontIcon` 10, `PathIcon` 1, and `SymbolIcon` 5 now ISOMIG011); ISOMIG005 116 → 118 and ISOMIG016
  57 → 56 (WinUI's `SymbolIcon` type is listed); ISOMIG013 401 → 402 (`IconElement.AccessibilityHidden`).
- **Tests:** library 1,259 → 1,288 on each of net9.0, net10.0 and net11.0, migration 52 → 54; every new test proven to
  fail with its behaviour broken (31 library breaks, 14 others), on the final head.

## Writing controls guide (issue #80, 2026-11-26)

- `docs/guide/writing-controls.md`, with spike J as its worked example. Its 12 C# blocks are regions of spike J's code
  (`controls-*`): `DocSnippetTests` also reads regions from `spikes/J-third-party/` (`bin` and `obj` left out; a region
  name used twice fails), and `run.mjs` builds and tests that code against the packed packages on each framework.
- Spike J: `RatingBar` gained a plain .NET event (`Cleared`, `[WrapEvent("Cleared", Arg = "OldValue")]`), its record a
  `[WrapManual]` member (`IsReadOnly`, written in `Customize`), and `GuideExamples.cs` 5 tests of the guide's examples:
  39 of 39 tests and 8 of 8 probes on each framework (`run.mjs`, packed `0.1.0-preview.0.82`).
- Read from the generated source: the descriptor adds `Customize`'s entries before the generated ones.
- MCP: the tool serves the guide (the pack smoke now checks the resources equal `docs/guide`'s 7 pages); `get_api` leaves
  its snippets out of examples, since they use a made-up library.
- Measured: library 1259 on each of net9.0, net10.0 and net11.0, MCP 53, migration 52, from a fresh clone.

## Generator diagnostics for library records (issue #78, 2026-11-25)

Spike J's gaps 2 and 3 are closed in our generator partial (`WrapperGenerator.Avalonia.cs`, five `gen-*` transform rules).
In descriptor-only mode:
- `ISOTOPEGEN002` (error, at the member): an `On{X}` callback the descriptor never wires (no routed event, `[WrapEvent]`
  event or controlled value). A callback the author wires isn't reported: `[WrapManual]` on it, or its event or value
  `[WrapManual]` or `Exclude`d, as Isotope's own records do (`AutoCompleteBoxElement` marks its two Reactor-named callbacks).
  The record's own members are checked as the generator's event filter matches them (any accessibility, property or
  field); a delegate callback a base record declares, which the descriptor never wires, is reported too.
- `ISOTOPEGEN003` (error, at the member): a wired callback whose delegate can't take what the event passes (nothing for a
  routed event; the args or the `[WrapEvent]` Arg/Args). The event is then left unwired, so no error lands in the `.g.cs`.
- `ISOTOPEGEN004` (error, at the attribute): `[WrapDecorator]` and `[WrapPolymorphic]` aren't supported on Isotope yet
  (their handler needs engine members left out in Spike F); nothing is generated.
- A record without a `Setters` it can reach (a property or field, not another assembly's internal or a base's private one)
  of a type that converts to `Action<TControl>[]` gets a descriptor that doesn't read them, as upstream's polymorphic
  path already checks for the member; no diagnostic.
Not covered: a controlled value's `On{P}Changed` with the wrong delegate type still errors inside the generated file.
Measured: 1258 library tests on each of net9.0, net10.0 and net11.0; spike J's probes as expected on each framework.

## Samples migration gate (issue #71, 2026-11-25)

- **CI's `samples migration` job** (ubuntu-latest) runs `measure-samples.mjs --check` against
  `tools/migration/samples-baseline.json` on every push. It fails when any count differs: worse, better, or measured on
  another sample set or upstream commit. A better count fails too, so the same PR updates the baseline
  (`--write-baseline`, which writes the file and the guide's table and still fails), as `expected-total.txt` works.
- **Measured** on main `bfa57e1`: 108 of 358 files build, 2,936 compile errors, 2 of 36 projects build, 1,122 migration
  diagnostics left. These are identical on Windows (local and `windows-latest`) and `ubuntu-latest`; the "before" build
  needs no Windows (the converted projects drop `UseWinUI`).
- **Run time:** 3.2 to 5.0 min on `ubuntu-latest` (runs 304, 305, 306 on one commit; 3.6 min in run 303), from 8.1 to
  14.7 min with `dotnet format` (runs 274, 275, 278, 283, 289) and 15.6 to 17.0 min one project at a time before that.
  Since #87 the fixes run in MigrationReport's workers (`--serve`: MSBuildWorkspace, what one `dotnet format analyzers`
  run does), every sample is restored once, and `--jobs` defaults to half the processors (on 4 processors locally, 2 at
  once took 207 s, 3 took 237 s, 4 took 306 s). Locally one at a time, the before phase took 66 s (was 148 s) and the
  migrate phase 147 s (was 588 s; 94 passes of the fixes took 85 s against 456 s for 97 `dotnet format` runs); most of
  the fix time is each project's first pass, which loads the workspace. `--fixer dotnet-format` still runs `dotnet
  format`, with the baseline's counts. dotnet format fixes the rules in a hash set's order, which changes per run:
  against one of its runs, 8 files had their added usings in another order and 3 projects took one pass fewer, nothing
  else.
- **Docs-only skip:** a branch push whose changes since its merge base with main are all under `docs/` or `*.md` skips
  the measurement; the job still reports and its tests still run (run 276 skipped, run 278 measured).
- **Not gated:** per-message counts and compile errors per ID: each names a member or type, so they move with every
  rename or control. The run still prints the commonest.
- **Proven:** a commit dropping `Thickness` from `winui-types.json` failed the job (+3 compile errors), and its revert
  passed. 34 node tests for the comparison, the guide's generated table and the docs-only skip.

## Vendor elements skip like built-ins (issue #77, 2026-11-25)

- **Decided by the developer:** a library's element follows the built-ins' skip rule (upstream's, decisions after Spike F, 3).
- **Surface:** two members on `Element`. `protected virtual bool ShallowEqualsCore(Element other)`, false by default, is
  called by `ShallowEquals` for a type with no built-in arm, after the shared checks. `HasCallbacks` is
  `protected internal` (was internal); the built-ins' overrides are unchanged in behaviour.
- **Generator (rule `gen-skip-members`, our partial):** in descriptor-only mode, a record outside the library gets both
  members, over its auto-properties and fields. Callbacks (`On{X}`) are compared by presence; other delegates, elements,
  arrays, collections and records by reference; the rest by value. It writes nothing, so the element updates on every
  render, for an unreachable or hidden member, a struct holding an element, a base from another assembly, or a base that
  declares either member. The library's own records get nothing, so those with no arm (ListBox, Expander, the pickers)
  still update on every render.
- **Gap 6:** no new API. A library binds tokens through `Element.ThemeBindings`, which follows a theme switch with no
  render; `ThemeRef.Resolve(key, isDark)` covers a one-off value.
- **For the guide (#80):** a hand-written `ShallowEqualsCore` must compare every member: a member it ignores is lost when
  the element is skipped, because the next update diffs against the last render's element.
- **Measured:** library tests 1245 on net9.0, net10.0 and net11.0 (1234 before, plus 11 new; each new behaviour shown to
  fail when broken). Spike J: 34 of 34 tests and 8 of 8 probes on each framework (`node spikes/J-third-party/run.mjs`).

## Groups in a vendor's panel (issue #79, 2026-11-26)

- **A `Group` in a library's panel now mounts its children,** as in Isotope's own panels (spike J's gap 4). The generated
  `Panel` children strategy reads its children through `PanelChildren.Of` (public, beside `ItemsList` in
  `Isotope.UI.Core.V1Protocol`), which flattens with the internal `Reconciler.Flatten`, child slots included, so a group
  that grows or shrinks keeps its siblings' controls. One transform rule (`gen-panel-children-flatten`); the generator
  partial is unchanged and `Reconciler.Flatten` stays internal.
- **Isotope's own panels are unchanged:** their records already flatten, so `Of` returns the same array. The library suite
  passes 1234 of 1234 on net9.0, net10.0 and net11.0 (1227 before; 7 new in `GeneratedPanelTests`, which run the
  generator on a test record as a library's build does).
- **Spike J:** `run.mjs`, 34 of 34 tests and 8 of 8 probes on each framework (packed `0.1.0-preview.0.78`, 63 s); its
  group test now asserts the children mount, keyed and unkeyed.
- **Proven to fail** (net10.0): with the strategy not flattening, 5 of the 6 mount and flatten tests and spike J's group
  test on each framework; with a flatten that drops child slots, or one that copies the array, the nested-group, resize
  and pass-through tests (3 each); with the list branch unflattened, the list test.

## Spike J: third-party controls (issue #75, 2026-11-25)

- **A third party can wrap its own controls using only Isotope's packages.** A library built outside the repo against the
  packed packages (a plain control, a templated one and a panel; `spikes/J-third-party`) passes 34 of 34 headless tests
  on each of net9.0, net10.0 and net11.0 (`run.mjs`, 3 invocations, 55 to 56 s each). The bundled generator runs in its
  build on every framework, and the generated code uses only public API: no `InternalsVisibleTo`.
- **Like a built-in:** mount and update in place, controlled values with no echo, routed-event callbacks, keyed moves
  (0 detaches, focus kept) in Isotope's panels and its own, a component in a content slot keeping its state, error
  boundaries (the control's own throw too), and theme tokens, on its own brush properties too.
- **One difference, closed by #77:** a library's element is now skipped as a built-in one is:
  `Element.ShallowEqualsCore` (protected) and `HasCallbacks` (protected internal) are written by the bundled generator for
  a record outside the library. Measured by spike J: an unchanged element reaches its control on 1 of 11 renders (11 of
  11 before), and a rejected edit stays as on the built-in slider.
- **Traps:** an `On{Event}` for a plain `EventHandler` is silently never wired; a missing `Setters`, a `[WrapEvent]`
  type mismatch and `[WrapDecorator]` fail inside generated files; a `Group` in a library's panel threw (closed by #79); factory names
  can clash (CS0121, CS0119). 8 probes pin them (`Probes/probes.json`).
- **Recommendation:** public-surface and generator changes first, then a "Writing controls for Isotope" guide. The
  migration tool takes no library rename table; that can wait.
- **In CI:** the `packages` job runs it on the packages it just packed, on Ubuntu and Windows. Follow-ups, all closed: #77 (skip
  parity), #79 (groups in a vendor's panel) and #78 (generator diagnostics); next, the guide (#80).
- Report: `docs/spikes/J-third-party-controls.md`.

## Rich text (issue #65, 2026-11-25)

- **What landed** (the rich text family, the top of ISOMIG012 after #43), with Avalonia's names (#46):
  - Avalonia's inlines as element records: `RunElement`, `SpanElement`, `BoldElement`, `ItalicElement`,
    `UnderlineElement`, `LineBreakElement` and `InlineUIContainerElement`, under `InlineElement` (Reactor's
    `RichTextInline`, `RichTextRun`, `RichTextLineBreak`, `RichTextInlineUIContainer`). Factories `Run`, `Span`, `Bold`,
    `Italic`, `Underline`, `LineBreak` and `InlineUIContainer` (Reactor's `InlineUI`); a string is a run.
  - `TextBlockElement.Inlines` (Avalonia's member) and `TextBlock(inlines)`.
  - Reactor's `RichTextBlock` is `SelectableTextBlockElement` and `SelectableTextBlock(...)`, with `Inlines`, Reactor's
    `Paragraphs` (kept) and `SelectionBrush` (Reactor's `SelectionHighlightColor`).
  - `Paragraph(...)` keeps Reactor's name (`RichTextParagraph`): Avalonia's text has no blocks, so a paragraph is a
    `Span`, and `Paragraphs` puts a `LineBreak` between them.
- **Unlike upstream's:**
  - Inlines are elements: they take a key and the text modifiers (`.FontSize`, `.Foreground`, `.Bold()`, `.Italic()`,
    `.Underline()`, `.Strikethrough()`, `.LetterSpacing`, theme tokens, which follow the text block's theme). Upstream's
    are plain records with `IsBold`, `IsItalic` and `IsStrikethrough`; a modifier an inline doesn't take is reported once
    per inline type on the Isotope-UI event source.
  - Each inline list is diffed as a panel's children: by key, else by position among the unkeyed; a matched inline is
    updated in place (only changed values are written), a keyed one moved. Upstream rebuilds a RichTextBlock's blocks
    unless their shape is unchanged, and keeps only inline UI across renders.
  - An inline UI container's child is a child slot. The dirty path, the context check, unmount and the error boundary's
    attachment check go through inlines to their text block.
- **Avalonia, measured headless (12.1.6):**
  - Writing `TextBlock.Text`, even `""`, clears its inlines; adding an inline to a text block with text turns the text
    into a run. The reconciler writes one or the other.
  - Any change to a text block's inlines (a run's text, a brush, a move) lays the text out again and takes each inline
    UI child out of the visual tree and back: the control and its state survive, its keyboard focus doesn't. Upstream's
    demo of a slider dragged inside text whose numbers change may lose the drag on Avalonia (not checked in #73's real-window run: #99).
  - `TextDecorations.Underline` and `.Strikethrough` are shared static collections; the `Underline` span is underlined
    by default.
- **Not done:** `Hyperlink` (no hyperlink inline in Avalonia; the migration points to `InlineUIContainer(HyperlinkButton(...))`),
  `InlineUI(Func<FrameworkElement>)`, a paragraph's `Margin`, `TextIndent`, `TextAlignment` and `LineHeight`,
  `LineStackingStrategy`, and `CharacterSpacing` (reported with the conversion to `LetterSpacing`).
- **Decided** (developer, 2026-11-25):
  - `IsTextSelectionEnabled` isn't added. Avalonia's `TextBlock` can't be selected and its `SelectableTextBlock` always
    is, so the migration reports it (ISOMIG010) with what to do: on a text block, drop it if false or use a
    `SelectableTextBlock`; on a rich text block, drop it. Upstream's obsolete `Selectable()` gets the same note. A new
    `removed` kind of member entry in `tools/naming/renames.json` carries the member.
  - Inlines stay `Element`s, and paragraphs stay spans in one selectable text block: a selection can cross paragraphs,
    and a paragraph has no margin or alignment of its own.
- **Measured on upstream's samples** (`node tools/migration/measure-samples.mjs --out <empty temp folder>`: 36 projects,
  358 files, upstream 561c5573; after the fixes, on main at a2dd42d and on this branch):

  | | main | #65 |
  |---|---|---|
  | Files that build | 107 | 108 |
  | Compile errors | 3,001 | 2,936 |
  | Projects that build | 2 | 2 |
  | Migration diagnostics left | 1,176 | 1,122 |
  | ISOMIG012 (controls not ported) | 206 | 136 |

  - ISOMIG012 lost 70: `Run` 39, `Paragraph` 14, `RichTextBlock` 9, `InlineUI` 8.
  - Rich text left: `IsBold` 9 and `IsItalic` 2 (ISOMIG007, a hand fix with a note), `Hyperlink` 2 (ISOMIG012), and
    `LineStackingStrategy` 2 on a selectable text block (ISOMIG013). `IsTextSelectionEnabled` (9) is reported with what
    to do (ISOMIG010).
  - No file that built before the fixes fails after them, and no `dotnet format` run failed.
- **Tests:** 35 library tests and 4 migration tests, each proven to fail (30 deliberate breaks, and 7 for
  `IsTextSelectionEnabled`).

## MCP tool ready to publish (issue #68, 2026-11-25)

- **The tool ships in the first release** (developer, 2026-11-25). `release.yml` now publishes all four packages.
- **An MCP server package,** as the `dotnet new mcpserver` template packs one. It has the package types `DotnetTool` and
  `McpServer`, and an `.mcp/server.json` written at pack time: its name (`io.github.<owner>/<repo>`) and repository
  come from the git remote (SourceLink's), and its version from MinVer. So no file in the repository names the owner,
  just as with the nuspec's repository URL. `tools/packaging` checks it against the package, and checks that the owner's
  name appears in it only there.
- **Third-party notices:** `src/Isotope.UI.Mcp/THIRD-PARTY-NOTICES.md` lists 50 packages and 61 assemblies (counted from its table: rows, and backticked DLL names), each with its
  licence and copyright, plus the licence texts: MIT, and the MCP C# SDK's own LICENSE (an Apache-2.0 transition with an
  MIT part, held in `tools/packaging/licenses/` as at the commit the bundled 2.4.0 was built from).
  `tools/packaging/lib/notices.mjs` generates the file from the tool's restore and each package's nuspec. `pack.mjs`
  fails when the file differs from what the packed tool carries, and `--write-notices` rewrites it.
- **The tool no longer carries the desktop backends or renderers** (Win32, X11, macOS, FreeDesktop, Skia, HarfBuzz). It
  reflects over signatures that name none of them. The package went from 11.9 MB and 94 files to 10.6 MB and 84 files,
  and it lost the only two bundled packages whose copyright line names a person rather than an organisation. The packed
  tool is what runs without them: the package smoke test (its `migrate_code`, `map_reactor_name` and `get_api` for the
  desktop startup) and the three fresh-agent runs below. The 51 MCP tests run from the test project's own output, which
  still has them.
- **Tested by a fresh agent before publishing:** a `claude -p` session (Sonnet) in an empty scratch folder.
  - Its setup:
    - `--setting-sources project`, so none of the user's settings, hooks or memory;
    - `--strict-mcp-config`, so its only MCP server is the tool, installed from the locally packed package into a
      scratch tool path;
    - an allow-list of `dotnet`, file tools and the tool;
    - a `nuget.config` that takes `Isotope.*` from the scratch packages only, and a fresh `NUGET_PACKAGES`.
  - Its tasks: a counter app, and a port of a trimmed version of upstream's NumberBox sample page. Each run's build was
    checked again afterwards. Turns and cost are the CLI's own report.

  | Run | Turns | Cost (USD) | Build | What it found |
  |---|---|---|---|---|
  | 1 | 16 | 0.18 | 0 errors | `map_reactor_name("NumberBoxElement.Header")` gave other records' `Header` (Expander, ToggleSwitch); `SpinButtons` unknown; `.Range` unexplained; the header argument left as CS1501; no project file in the guide |
  | 2 | 14 | 0.18 | 0 errors | `Range` and `SpinButtons` now explained; the header argument still left as CS1501 only; the guide's project file unclear (one package or two, which version) |
  | 3 | 15 | 0.18 | 0 errors | Every error and warning the migration left came with what to do; nothing wrong or misleading. It asks for enhancements only (below) |

  - The fixes:
    - `map_reactor_name` answers `Record.Member` only from that record. When the member is missing, it says which
      Isotope record it is and that the member isn't on it.
    - The rename table has `Range` and `SpinButtons` on `NumberBoxElement`: no modifier, set the record's `Minimum`
      and `Maximum` and `ShowButtonSpinner` instead.
    - A factory parameter can now have no counterpart (`avalonia` null, with a `note` and a `position`).
      `NumberBox`'s header is the first. The migration reports it whether it's passed by name or by position.
    - The migration looks a modifier up past an unresolved link in its chain (`.Range(...).SpinButtons()`), for the
      table's modifiers only.
    - The guide has a project file.
- **Measured on upstream's samples** (`node tools/migration/measure-samples.mjs --out <empty temp folder>`, 36 projects,
  358 files, upstream 561c5573, on main after this issue): 107 files build and 3,001 compile errors are left, as after
  #46. The diagnostics left went from 1,172 to 1,176: 6 new ISOMIG010 for NumberBox's `Range`, `SpinButtons` and header
  argument, and 2 fewer ISOMIG013. The migration guide has the new counts; #71 is to make CI check them.
- **Not done** (run 3's asks): rewriting `Range`, `SpinButtons` and the header automatically, rather than reporting them; a
  `using` for the types the migration writes (it writes them in full, on purpose); one complete app example in one place.

## Target frameworks (issue #67, 2026-11-25)

- **The library and desktop packages ship for `net9.0`, `net10.0` and `net11.0`** (developer, 2026-11-25: 10 is the
  LTS, 11 the STS, and 9 stays for a few months). .NET 9 itself went out of support on 2026-11-10, as .NET 8 did:
  `net9.0` is kept for apps still moving off it, not because it is supported. So do their tests and the package smoke app. The generator and the
  migration stay `netstandard2.0`, and the MCP tool stays a `net10.0` tool with `RollForward=Major`. The sample app and
  the other projects stay `net10.0`.
- **Measured** (spike: `TargetFrameworks` `net8.0` to `net11.0`, the 11.0.100 SDK, Windows, every runtime installed):

  | | Library build errors | Library tests | Smoke app at the framework's default C# |
  |---|---|---|---|
  | `net11.0` | 0 | 1,192 of 1,192 | builds, 4 of 4 checks |
  | `net10.0` | 0 | 1,192 of 1,192 | builds, 4 of 4 checks |
  | `net9.0` | 0 | 1,192 of 1,192 (after the `Reverse` fix below) | 2 errors at C# 13; 4 of 4 at C# 14 |
  | `net8.0` | 32; 14 with a `Lock` polyfill | not run | not run |

  - `net8.0`: 18 uses of `System.Threading.Lock` (.NET 9 and later), and 14 CS9244 in three vendored descriptor-runtime
    files, which pass ref-struct contexts to `Action<…>` (`allows ref struct` came in .NET 9). Both went out of support
    on 2026-11-10. `net8.0` was left out, since those gaps need edits to vendored files.
- **On `net9.0`:**
  - The desktop startup's `ReactorApp.Run` and `CreateDefaultAppBuilder` are C# 14 extension members, which an app's
    default C# 13 can't call (CS9260). The app sets `LangVersion 14`, which needs the .NET 10 SDK or later (the desktop
    README says so). The Desktop project itself now sets `LangVersion 15`, as the library does.
  - **The .NET 10 SDK is enough** (measured): the smoke app, built outside the repo with SDK 10.0.402 (`rollForward`
    disabled) against the packed packages, builds and passes 4 of 4 checks on `net10.0` (C# 14, its default) and on
    `net9.0` (`LangVersion 14`). SDK 10 can't target `net11.0`.
  - C# 14 binds `array.Reverse()` to `MemoryExtensions.Reverse(Span<T>)`, which returns `void` (.NET 10 added an
    `Enumerable.Reverse(T[])` overload that wins). The sample's todo list now uses `Enumerable.Reverse(items)`, and the
    library README warns about it.
  - `Microsoft.Extensions.Logging.Abstractions` 11.0 supports `net10.0` and later only, so `net9.0` takes 10.0.14.
- **Packages and CI:** `tools/packaging` checks `lib/<framework>/` and a dependency group for each framework (4 new
  test cases), and runs the smoke app on each. The generator goes into `analyzers/` from one framework only (each gave
  NU5118 otherwise). CI's library job and the package jobs install the .NET 9 and 10 runtimes (11 comes with the SDK),
  and the library tests' count is checked on each framework.
- **Dropping `net9.0` later:**
  - `TargetFrameworks` in the library, desktop, library tests and smoke app, and the smoke app's `net9.0` `LangVersion 14`;
  - the library's 10.0 `Microsoft.Extensions.Logging.Abstractions` reference;
  - (if `net10.0` ever goes) the framework the library packs its generator from (`_IsotopePackGenerator`);
  - `FRAMEWORKS` in `tools/packaging/lib/checks.mjs`;
  - in `ci.yml`, the library test count check (`-eq 3` and "each of 3 frameworks"), and the `9.0.x` runtime there and in
    `release.yml`;
  - CLAUDE.md's toolchain line, `docs/releasing.md`'s `lib/net9.0`, and the `net9.0` notes in the READMEs (library,
    desktop, migration) and the migration guide.

## MCP server, phase 1 (#60, 2026-11-25)

- `isotope-mcp` (package `Isotope.UI.Mcp`, a `net10.0` dotnet tool, ModelContextProtocol 2.4.0, stdio) reads the API by
  reflection over the library and desktop DLLs it ships with and their XML docs. Doc IDs computed from reflection equal
  Roslyn's for every public method, constructor and property of both assemblies (`ApiTests.Doc_ids_from_reflection_are_the_compilers`).
- C# 14 extension blocks: reflection shows the declared members on compiler-made `<G>$…` nested types, which carry the
  docs, and the implementations on the static class, whose XML doc is an `<inheritdoc cref>` to them.
- Referencing the desktop project brings Skia, HarfBuzz and ANGLE native libraries for every RID: the Debug output
  was 592 MB with them and 33 MB without (`du -sh`). The tool drops them (it never runs Avalonia); its package is
  11,938,998 bytes, 94 files (`pack.mjs`).
- The SDK's stdio client skips lines on stdout that aren't JSON-RPC: with every log sent to stdout, 1 of the stdio tests
  failed (a proof run). The pack smoke test's own client fails on such a line.
- **Not published yet:** `release.yml` leaves the tool package out (the developer decides; it would also need a
  third-party notices file for the Apache-2.0 MCP SDK it bundles).

## Spike E: wrappers generator (issue #8, 2026-11-23)

- **Upstream's generator ports to Avalonia** in descriptor-only mode, which upstream itself uses for 74 built-in elements. A patched copy (36 added and 33 removed lines, after the rename) generated descriptors for Button, TextBox and Slider from Avalonia 12.1.6 metadata.
- **Its WinUI coupling is small:** 52 code lines in 3,920, and 45 of them are inside string literals.
- **Coverage [J]:** 45 of upstream's 69 generated WinUI controls have an Avalonia counterpart. About 17 to 20 of those would be fully generated, and about 25 to 28 generated with a hand-written `Customize`.
- **The runtime it targets** (6 files, 3,522 lines) was ported in #12; see Spike F below.
- **Report and decisions:** `docs/spikes/E-wrappers-generator.md`.

## Sync follow-ups (issues #21, #22, 2026-11-24)

- **Class components have the platform hooks** (a partial-component rule).
- **Upstream's `ColorScheme`, `GridSize` and `GridSizeExtensions` are compiled,** and our copies are gone. The first Roslyn
  rules in `transforms.json` remove the WinUI-only `ColorSchemeContext`.
- **9 more upstream tests run** (614).
- **The generator wraps a reference member on the ClearValue channel with `Optional.Of`,** so Border's `BorderBrush` is
  generated.
- **`GridSize` now behaves as upstream's:** an Auto value is checked only when Avalonia converts it, and an infinite size in
  `Parse` throws ArgumentOutOfRangeException. Kept for parity; the grid keeps its tracks if it throws.
- **Tests:** 881, 13 of them new, all proven to fail.

## Spike I: pooling and keyed moves (issue #14, 2026-11-24)

- **No control pooling.**
  - Creating 4,000 controls takes 4.0 to 4.9 ms.
  - A 1,000-row re-key in a headless window takes 249 to 273 ms through the engine, nearly all of it Avalonia's attach,
    styling, templates and layout.
  - Upstream's 32-per-type pool saves nothing. Only an unbounded pool helps (about 17% on a re-key, 50% on a mount,
    from reused templates).
  - #14 is closed without a port.
- **Keyed moves use `Controls.Move`** (#23, `Seam/MovingPanelChildCollection.cs`).
  - Upstream's RemoveAt + Insert detached every moved row and lost focus: a reverse of 1,000 rows took 136 to 137 ms in
    a headless window (measured before #23).
  - With `Controls.Move` it takes 6.7 to 7.1 ms, nothing is detached, and focus is kept (3 invocations of
    `spikes/I-pooling/Measure`).
  - A guard test fails when a sync changes upstream's `PanelChildCollection`, since our copy is invisible to `tools/sync`.
  - Report: `docs/spikes/I-pooling-and-moves.md`.

## More controls (issue #19, 2026-11-24)

- **14 more controls** with records, generated descriptors, factories and headless tests: CheckBox (plus three-state),
  ToggleSwitch, RadioButton, ComboBox, ListBox, ProgressBar (`Progress`, `ProgressIndeterminate`), Image, Border,
  ScrollViewer, Expander, Grid, WrapPanel, DatePicker, TimePicker.
- **ComboBox and ListBox are the first items controls that run;** the items-host fix needed no generator change.
- **Tests:** 45 new, all proven to fail; 856 library tests in all after merging #20.
- **Avalonia differences found:**
  - one event payload per control (a ComboBox lost its selection callback);
  - DatePicker's year bounds are set in its constructor;
  - Expander's events arrive late, so its controlled entry listens to `IsExpanded` instead.
- **Follow-ups:** vendoring upstream's `GridSize`, and a generator fix for interface-typed members (#22).

## Weekly sync job and the first real sync (issue #5, 2026-11-24)

- **The job:** `.github/workflows/upstream-sync.yml` runs weekly (Monday 05:17 UTC), and on `workflow_dispatch` with a
  `dry_run` input. A push touching the workflow or `tools/sync/**` gets a dry run.
  - **Runner:** GitHub-hosted. The `vars.SYNC_RUNNER` option was removed in #36 (public repo: no self-hosted runners).
    CI's `vars.LINUX_RUNNER` fallback was removed in #52 too: every job is GitHub-hosted.
  - **When upstream changed:** it pushes `sync/<old>-<new>` with the regenerated files, starts CI on it, and opens an issue
    with the report (upstream references are put in code spans, so nobody upstream is pinged). Duplicate ranges are skipped.
  - **The glue:** `tools/sync/ci/upstream-sync.mjs`, with 63 tests (#36).
- **The first sync that changed vendored files** (`14172dce` → `1f078436`): 7 upstream commits, 5 vendored files changed, no
  transform broke.
  - The library build broke on one new tier 3 type (2 errors), fixed with a placeholder.
  - 3 tier 3 PRs were read; none needs porting.
  - Spike A moved 567 → 569 and step 4 500 → 502, both because of that type.
  - About 4.5 minutes of agent time. It's a first data point for #5's cost question, not the developer's time.

## Avalonia type, factory and modifier names (issue #46, 2026-11-24)

- **Types, factories and modifiers take Avalonia's names** where Reactor's differ (#27 answer 1a). Callbacks keep `On` (1b).
  - Applied in `tools/naming/renames.json`: 7 types (`TabControlElement`, `TabItemData`, `NumericUpDownElement`,
    `AutoCompleteBoxElement`, `ProgressBarElement`, `StackPanelElement`, `MenuElement`), 5 factories (`TabControl`,
    `TabItem`, `NumericUpDown`, `AutoCompleteBox`, `ProgressBar`) and 3 modifiers (`HorizontalAlignment`,
    `VerticalAlignment`, `ContextFlyout`).
  - Kept with a reason: `VStack` and `HStack` (the decision's exception). Two proposed entries were kept rather than
    applied, because Avalonia's name would give another Reactor name a different meaning:
    - `MenuBarItemData`: Avalonia's `MenuItem` covers five Reactor item records, and `MenuItem(...)` builds another one.
    - `MenuFlyoutContentElement`: `MenuFlyoutElement` is Reactor's record for `MenuFlyout(target, items)`.
  - Also kept: the rest of the menu factories (`MenuBar`, `Menu`, `MenuItems`…), `ThreeState*`, `ProgressIndeterminate`
    and the flex family. That is 5 type and 14 factory entries.
- **Clashes:** the new factories hide Avalonia's types of the same name in expressions where the factories are imported
  (`TabControl.SelectedIndexProperty` is CS0119), as `Button` and `Grid` already did. A type position still names
  Avalonia's control. The modifiers don't clash, because extension methods aren't found by simple names. A test compiles
  both cases.
- **Migration:** a Reactor factory call that names only a type in the migrated project is now handled as a factory call.
  Reactor's `Progress(0.5)` with `using System;` names `System.Progress<T>`.
- **Measured** with `node tools/migration/measure-samples.mjs --out <empty temp folder> --samples <copy of upstream's
  samples>` (36 projects, 358 files, upstream 561c5573). Main (f3c3ea5) and the branch, after the fixes:

  | | main | #46 |
  |---|---|---|
  | Files that build | 107 | 107 |
  | Compile errors | 3,001 | 3,001 |
  | Projects that build | 2 | 2 |
  | Renames fixed (ISOMIG009) | 169 | 206 |
  | Migration diagnostics left | 1,169 | 1,172 |

  - The renamed names compiled before too, under Reactor's names, so the build counts don't move. The migration now
    rewrites 37 more names to Avalonia's.
  - The +3 left are `ItemsView` calls that name a type, now reported as a control not ported (ISOMIG012).
- **Tests:** `RenameTableTests` checks that every record is named after its control and every factory after its record's
  control, unless kept, and that applied modifier renames are properties of their control. With a clash test and 2
  migration tests, that is 6 new tests, all proven to fail.
- **Sync cost:** one transform rule, `test-stack-panel-element`, for upstream's `ChildReconcilerTests`.

## Packaging (issue #52, 2026-11-24)

- **Three packages:** `Isotope.UI.Avalonia` (the library, with the wrappers generator bundled as an analyzer, as
  upstream's package bundles its own), `Isotope.UI.Avalonia.Desktop` (depends on the library at the same version) and
  `Isotope.UI.Migration` (analyzer only). Each carries its README, our LICENSE, and upstream's LICENSE and
  ThirdPartyNoticeText. Authors is "The Isotope.UI.Avalonia authors".
- **Build:** `src/Directory.Build.props`/`.targets`; versions from git tags by MinVer (prefix `v`); SourceLink from the
  SDK; deterministic (8 of 8 DLLs, PDBs and XML docs byte-identical between two checkouts at different paths, by
  sha256). A plain `dotnet pack` fails on purpose, so no local path reaches a package.
- **Checks:** `tools/packaging/pack.mjs` packs and checks layout, metadata, dependencies, SourceLink, personal data and
  local paths (PDB document names decoded, all under `/_/`), then builds and runs `tests/Isotope.UI.PackageSmoke`
  against the packed packages only (a headless counter, a consumer `[GenerateReactorDescriptor]` element that only
  compiles if the bundled generator ran, the desktop default builder headless, and the migration's ISOMIG001). CI's
  `packages` job runs it on Ubuntu (61 s) and Windows (99 s).
- **Release:** `release.yml` runs only on a pushed `v*` tag; `pack` (read-only) refuses a tag that isn't `v<semver>`, isn't
  on `main` or doesn't match MinVer's version; `publish` (`id-token: write` only, environment `nuget`) uses NuGet trusted
  publishing with plain `curl`, since only `actions/*` are allowed: no API key secret. Not run yet; the setup is the
  developer's (`docs/releasing.md`).
- **Sizes:** library 567,254 bytes (symbols 138,792), desktop 10,847, migration 56,797.
- **Learned:** NuGet restores a dependency's analyzers even with `exclude="Analyzers"`; PDB document names are stored in
  parts and need decoding before a path search.
- **Also:** CI's last runner variable (`vars.LINUX_RUNNER`) was removed; every job is GitHub-hosted.

## Named text styles (issue #44, 2026-11-24)

- **Upstream's two families, with upstream's names** (kept: Avalonia has no counterparts; a new `kept` status in
  `tools/naming/renames.json`):
  - `Heading` (28, Bold, heading level 1), `SubHeading` (20, SemiBold, level 2) and `Caption` (12) set the text
    block's members, as upstream's `Dsl.cs` does; the level is Avalonia's `AutomationProperties.HeadingLevel`.
  - `Title`, `Subtitle`, `Body`, `BodyStrong`, `BodyLarge`, `TitleLarge` and `Display` are
    `TextBlock(text).ApplyStyle(key)` with WinUI's style key, as upstream's are. `Card(child)` comes from the same file.
- **WinUI's keyed Style is Avalonia's keyed ControlTheme.** `ApplyStyle(key)`, on text blocks only, binds the text
  block's `Theme` to the key's resource; a theme in the resources the control sees wins and is followed without a
  re-render. Otherwise Isotope's theme for that WinUI style applies, with WinUI's values from its `generic.xaml`
  (Windows App SDK 1.8). Avalonia.Themes.Fluent 12.1.6 defines none of these keys (measured headless).
  - Avalonia's styles outrank a control theme, so an app `Style` for every `TextBlock` that sets `FontSize` wins over
    the ramp (a test pins it; the guide says so).
- **Measured on upstream's samples:** ISOMIG012 lost 442 diagnostics (Caption 221, SubHeading 149, Heading 45, …). With
  #41–#44 and #49 together, after the migration's fixes: 107 of 358 files build (51 before the fixes) and 3,001 compile
  errors are left (13,699 before) — see `docs/guide/migrating-from-reactor.md`.
- **Tests:** 27 new library test cases and 1 migration test, each proven to fail. Guide: `docs/guide/text-styles.md`.

## Layout modifiers (issue #49, 2026-11-24)

- **Upstream's layout modifiers, with Avalonia's value types:** `Padding` (4 overloads), `WithBorder`, `BorderBrush`,
  `BorderThickness`, `CornerRadius`, `MinWidth`/`MinHeight`/`MaxWidth`/`MaxHeight`, `Center`,
  `HorizontalContentAlignment`/`VerticalContentAlignment`, `IsVisible`, and the `Spacing` and `FlexPadding` sugar.
  Upstream's names are already Avalonia's.
  - They are written where Avalonia has the property: a templated control, a `Border`, a `TextBlock` (padding only),
    and content alignment where the control declares it.
  - On a `BorderElement`, its own members and the modifiers of the same name are written as one value, the member's
    first.
- **A padded panel:** Avalonia 12's `StackPanel` and `Grid` have no `Padding`, `BorderBrush`, `BorderThickness` or
  `CornerRadius`, while WinUI's have all four.
  - A stack, grid or wrap panel with padding, a border width or rounded corners is mounted inside a `Border`
    (`PanelChrome`), which is how Avalonia pads a panel. The border holds the element's margin, size, alignment,
    background and theme tokens; the panel keeps its own members and children.
  - When that need comes or goes between renders, the panel is mounted again and components inside lose their state.
  - A flex panel ignores them, as upstream's (`FlexPadding` is Yoga's padding).
- **Not done:** the BiDi `MarginInlineStart` family. (Upstream's `HorizontalAlignment`/`VerticalAlignment` aliases of
  `HAlign`/`VAlign` are the modifiers' names since #46.)
- **Measured on upstream's samples** (`tools/migration/measure-samples.mjs`, 561c5573; before is main at e4e290f, after is
  this branch; both from fresh clones):

  | | Before | After |
  |---|---|---|
  | Compile errors | 3,727 | 3,442 |
  | Migration diagnostics | 1,885 | 1,630 |
  | ISOMIG013 (members and modifiers Isotope lacks) | 681 | 426 |
  | Files that build | 74 | 82 |
  | Projects that build | 2 | 2 |

  The ISOMIG013 groups now largest are `FullDescription` (39), `ApplyStyle` (27), `Resources` (14) and `SubtleButton` (14).
- **Tests:** 35 new, each proven to fail.

## Theme tokens (issue #41, 2026-11-24)

- **Upstream's `Theme` is brush tokens only:** `ThemeRef` and `Theme`'s 36 tokens, `Theme.Ref(key)` and
  `NotifyResourcesChanged()`. It has no spacing, corner-radius or type-ramp tokens.
  - The corner radii are `ThemeResource.CornerRadius("ControlCornerRadius")` (57 uses in the samples).
  - The type ramp is the named-style factories (#44).
- **Tier 2 for `Theme.cs`:** `ThemeRef` is `partial`, and its WinUI resolution is dropped (7 Roslyn `drop-member` rules, 2
  text rules). The tokens and the resolution cache compile as upstream's. `ThemeResourceListeners.cs` (tier 1) is
  compiled, and upstream's `ChildDiffHints` theme-bindings test runs again.
- **Tier 3 for the resolution, the bindings and `ThemeResource`.** Avalonia's Fluent theme defines none of the 36 keys
  (Avalonia.Themes.Fluent 12.1.6). So a key resolves from the resources the control sees, else from WinUI's Fluent values:
  light, dark, Windows' high-contrast colours when the platform asks for high contrast, and accent tokens from the Fluent
  theme's `SystemAccentColor` shades.
  - `Element.ThemeBindings` binds at style priority, so a brush the element sets wins.
  - A bound token follows the theme variant, resource changes and the contrast preference without a re-render.
  - `Theme.NotifyResourcesChanged()` renders each host's whole tree once, past memoization.
  - The built-in colours are WinUI's Fluent values from memory, not checked against WinUI's source; app resources win.
- **Measured on upstream's samples** (`tools/migration/measure-samples.mjs`, 561c5573, after the fixes; before is main at
  00692f0, before #42 merged):

  | | Before #41 | After #41 |
  |---|---|---|
  | Compile errors | 5,544 | 4,202 |
  | Files that build | 62 of 358 | 64 of 358 |
  | Projects that build | 2 of 36 | 2 of 36 |
  | Migration diagnostics left | 2,948 | 2,624 |
  | ISOMIG002 | 249 | 199 (no `Theme`) |
  | ISOMIG013 | 1,466 | 1,192 |

  CS0103 `Theme` (830) and `ThemeResource` (47) are gone. Chains that now bind show what follows them, e.g. 61 CS1955
  `BorderElement.CornerRadius`.
- **Tests:** 26 new, all proven to fail. Guide: `docs/guide/theme-tokens.md`.

## Text styling (issue #42, 2026-11-24)

- **Upstream's text members and modifiers, with Avalonia's names and types** (#31):
  - TextBlockElement has `FontSize`, `FontWeight` (upstream's `Weight`, renamed in `renames.json`), `FontStyle`,
    `FontFamily`, `TextWrapping`, `TextAlignment`, `TextTrimming`, `LineHeight` and `MaxLines`. Upstream's modifiers
    set them (`.Bold()`, `.SemiBold()`, `.FontSize(20)`, `.TextWrapping()`…).
  - Element-wide modifiers: `Foreground` (a colour string or an `IBrush`), `FontSize`, `FontWeight` and `FontFamily`,
    written on a TextBlock or a TemplatedControl; they do nothing on a panel, as upstream's.
- **Unlike upstream's:**
  - On a text block, a member and the modifier of the same name are written as one value, member first.
  - The element-wide font modifiers set the member when the element is a text block, so the last call wins.
  - `MaxLines` is `int?`, so a text block that doesn't set it leaves a style's value alone.
- **Not done:** `IsTextSelectionEnabled` (Avalonia's TextBlock has no selection; `SelectableTextBlock` is another
  control), `CharacterSpacing` and `TextDecorations` (shape changes; unused in the samples), `Foreground(ThemeRef)` (#41).
- **Measured on upstream's samples** (`tools/migration/measure-samples.mjs`: 36 projects, 358 files, upstream 561c5573;
  before is main at 00692f0, after is this branch; both after the migration's fixes):

  | | Before | After |
  |---|---|---|
  | Compile errors | 5,544 | 4,988 |
  | Migration diagnostics | 2,948 | 2,227 |
  | ISOMIG013 (members and modifiers Isotope lacks) | 1,466 | 745 |
  | Files that build | 62 | 62 |
  | Projects that build | 2 | 2 |

  - Every text diagnostic is gone except `IsTextSelectionEnabled` (3 → 6: more code is now analysed).
  - The ISOMIG013 group next by count is layout: `Padding` (144 across Stack, TextBlock and Flex), `WithBorder` (82),
    `Center` (50).
- **Tests:** 24 new, each proven to fail.

## Upstream sync hardening (issue #36, 2026-11-24)

- **Upstream's code runs only in a read-only job.** `upstream-sync.yml` has four jobs:
  - `plan` (contents and issues: read) reads upstream main with `git ls-remote` and decides what to do;
  - `prepare` (contents: read) syncs to that commit and uploads a git bundle and its metadata;
  - `publish` (holds the write grants; real runs only; runs no upstream code and no dotnet) checks the bundle, then pushes,
    dispatches ci and opens the issue;
  - `publish-dry-run` (read-only) runs the same check on dry runs.
- **The check:**
  - one commit on the checkout's HEAD, by the Actions bot, with the sync's message;
  - only `src/Upstream/`, `UPSTREAM.lock` (exactly as tools/sync writes it) and the range's report change, all as regular
    files;
  - fsck on the fetch, and no `.git`, `.` or `..` path parts;
  - no personal data in the report.

  ci's `--check` on the branch checks the vendored bytes themselves.
- **The glue:** 63 tests (was 22); 41 deliberate breaks each turned their tests red. A full dry run on a probe branch:
  plan 18 s, prepare 47 s, publish-dry-run 11 s.

## Next controls by migration count (issue #43, 2026-11-24)

- **What landed** (the top of ISOMIG012 without the text styles, #44), with Avalonia's names for what is new (the
  developer's naming decision of 2026-11-24, recorded `applied` in `tools/naming/renames.json`):
  - Reactor's `ScrollView` is Avalonia's `ScrollViewer`. The migration writes `(ScrollViewer(x) with {
    HorizontalScrollBarVisibility = Disabled })`, which keeps WinUI ScrollView's layout (content as wide as the view), from
    a new `with` field on a factory entry. `ScrollViewElement` is `ScrollViewerElement`; its `ContentOrientation` is
    left for a hand fix (ISOMIG007, with a note).
  - `TreeView` as Avalonia's TreeView. A node is `TreeViewItemData` (Reactor's `TreeViewNodeData`) with `Header`,
    `Items` and `IsExpanded`, built by `TreeViewItem(...)` (Reactor's `TreeNode`).
    - Nodes keep their items by header, so the user's expansions survive sorts, inserts and removals. Upstream
      rebuilds the tree on any change.
    - `OnItemInvoked` fires on a tap, or on an unmodified Enter or Space. `OnExpanding` fires on the user's
      expansions only.
  - `Viewbox`, as Avalonia's.
  - Upstream's `When`, `If`, `Expr` and `ForEach` helpers, and `Accelerator(key, modifiers)` for the existing
    `KeyboardAcceleratorData`.
- **Measured** with `node tools/migration/measure-samples.mjs --out <empty temp folder>` (upstream's 36 samples, 358
  files, upstream 561c5573). Columns are after the fixes, on main before this branch and on the branch:

  | | main | #43 |
  |---|---|---|
  | Files that build | 62 | 63 |
  | Compile errors | 5,544 | 5,394 |
  | Projects that build | 2 | 2 |
  | Migration diagnostics left | 2,948 | 2,724 |
  | ISOMIG012 (controls not ported) | 877 | 645 |

  - ISOMIG012 lost 232 diagnostics: ScrollView 139, When 31, Accelerator 22, TreeNode 20, Viewbox 9, TreeView 4,
    Expr 4 and ForEach 3.
  - Some code that compiles further now shows errors that were hidden (`TextBlockElement` members +30, `Theme` +19).
  - No file that built before the fixes fails after them, and no `dotnet format` run failed.
- **Next by count** (not in this issue): the rich text family (`Run` 39, `Paragraph` 14, `RichTextBlock` 9,
  `InlineUI` 8), icons (`Icon` 14, `FontIcon` 10), `ListView` with element items (12), and the typed
  `TreeView<T>`.
- **Measured** (headless, Avalonia 12.1.6):
  - Enter on a TreeViewItem with children expands it.
  - A TreeViewItem's `Expanded` routes Tunnel and Bubble, raised inside the `IsExpanded` write.
  - The migration fix must not add `using Avalonia.Controls.Primitives`. It makes Reactor's `Popup` and
    `UniformGrid` name Avalonia types, which hid 4 ISOMIG012 diagnostics.
- **Tests:** 16 library tests and 3 migration tests, all proven to fail.

## Migration tool (issue #33, 2026-11-24)

- **An analyzer with 16 rules (ISOMIG001–016) and code fixes,** run with `dotnet format analyzers` after the project file
  is switched to Isotope (`docs/guide/migrating-from-reactor.md`).
  - It reads `tools/naming/renames.json` (#31): entries marked `proposed` stay off until the table marks them `applied`.
  - It has two tables of its own, `winui-types.json` (71 types) and `reactor-controls.json` (103 factories Isotope lacks).
- **Measured on upstream's samples** (`tools/migration/measure-samples.mjs`: 36 projects, 358 files, upstream 561c5573;
  fixes applied until nothing changed):

  | | Before | After |
  |---|---|---|
  | Files that build | 51 | 62 |
  | Compile errors | 13,699 | 5,544 |
  | Projects that build | 1 | 2 |

- **The issue's "done when" is not met:** what is left is mostly Isotope API that doesn't exist yet, not migration work.
  Of the 2,948 migration diagnostics left:
  - 1,466 are members and modifiers (`Foreground` 375, `FontSize` 157);
  - 877 are controls not ported (`Caption` 221, `ScrollView` 139);
  - 249 are Reactor namespaces (Charting 159, `Theme` 50).

  The largest group of compile errors left is 830 uses of Reactor's `Theme` tokens.
- **Tests:** 38, all proven to fail; CI runs them in the library job.

## Avalonia member names (issue #31, 2026-11-24)

- **The public API takes Avalonia's member names** (#27 answer 1; Spike H option A). A record member is Avalonia-named
  when it is a property or event of its control, `On{Event}` (`OnClick`) or `On{Property}Changed`
  (`OnSelectedIndexChanged`). These are the names the generator already maps, so no `[WrapAlias]` is left.
- **The rename table is data:** `tools/naming/renames.json`, in the format given in `tools/naming/README.md`.
  - 36 records with their controls, and 47 member entries: 25 renames, 18 shape changes and 4 kept.
  - 32 factory parameter renames.
  - Proposed, not applied: 9 type, 5 factory and 3 modifier renames, waiting on #27's follow-up.
- **Shape changes:**
  - The button family's `Label`/`ContentElement` become one `object? Content`, as Avalonia's.
  - Expander's `Header`/`HeaderContent` become one `object? Header`.
  - CheckBox and ToggleButton have one `bool? IsChecked` and one `Action<bool?>` callback; the two-state factories
    still take an `Action<bool>`.
  - Grid's `Definition` becomes `ColumnDefinitions` + `RowDefinitions`.
- **Kept, with no Avalonia counterpart:** AutoSuggestBox's `OnQuerySubmitted` and `OnSuggestionChosen`, and a menu item's
  `KeyboardAccelerators` and `Description`.
- **Migrating our own code with the table:** the first build after the renames had 62 compile errors in the tests and
  the sample. A script that looks each error's name up in the table fixed 50; the 12 Grid ones were fixed by hand.
- **Sync cost:**
  - Two transform rules: `gen-manual-content-slot` and `test-textblock-text` (3 vendored test files move to tier 2).
  - A tier 3 port maps names through the table (`docs/sync/README.md`, and a line in each sync report's tier 3 section).
- **Tests:** 8 new, all proven to fail. `RenameTableTests` checks every record against its control, the rule and the
  table.

## Controls batch 2 leftovers (issue #29, 2026-11-24)

- **What landed:**
  - AutoSuggestBox, as Avalonia's `AutoCompleteBox`;
  - element tooltips (`.WithToolTip(element)`);
  - upstream's `Command` on the four buttons and on menu items, as Avalonia's `ICommand`;
  - menu item shortcuts and descriptions.
- **Measured** (headless probe, Avalonia 12.1.6):
  - a `Text` write raises `TextChanged` inside the write in a window, and none outside one;
  - showing a box with text raises one;
  - suggestions set after a keystroke don't open the list until `PopulateComplete()`.
- **Tests:** 47 new, all proven to fail.
- **Not done:** menu icons, access keys, the `MenuFlyout(target, items)` decorator.

## Component errors outside every boundary (issue #28, 2026-11-24)

- **In-slot fallback, as upstream's:** when a component's render or effects throw outside every boundary, that component's
  slot shows a fallback, and the rest of the tree keeps its state. Each later render retries it.
  - The fallback is the handler's element (guarded), or the exception text, or a neutral text.
- **A failed pass rolls back:** what a failing root pass mounted but never attached is torn down, and the cleanups of
  components whose mount failed run once.
- **Isolated control teardown:** a control's unmount hook that throws during the host's or a boundary's teardown is
  reported once, and the rest still runs.
- **Cost:** none measured. Spike G's 1,000-row mount, 15 runs each, alternating: median 47.26 ms on main and 45.83 ms on
  the branch.
- **Tests:** 16 new, all proven to fail.

## More controls, batch 2 (issue #26, 2026-11-24)

- **Element content** for the button family (our tier 3 `WithContent`): `Button(element, onClick)` and Expander's
  `.HeaderContent(...)`. A component inside keeps its state.
- **ToggleButton, RepeatButton, HyperlinkButton.**
- **Navigation:** TabView as Avalonia's TabControl (the vendored `TabItemsHost`, with a controlled `SelectedIndex`), and
  SplitView.
- **Menus:** `MenuBar`/`Menu`/`MenuItem` and toggle, radio and sub items; context menus via `.WithContextFlyout(...)` (`.ContextFlyout(...)` since #46).
- **Input and decoration:** the ToolTip modifier, NumberBox as NumericUpDown (upstream's `double` values, converted), and
  Separator.
- **Tests:** 49 new, all proven to fail.
- **Not done:** AutoSuggestBox, element tooltips, upstream's `MenuFlyout(target)` decorator, `Command` overloads, and menu
  icons and shortcuts.

## Error boundaries (issue #25, 2026-11-24)

- **`ErrorBoundary(child, fallback)`:** when a subtree throws (in a render, an effect, a mount or an update), the boundary
  shows `fallback(ex)` in its place, and the rest of the tree keeps its controls and state.
  - The nearest boundary catches, and a fallback that throws goes to the next boundary out.
  - Outside every boundary, a failing component is replaced in its own slot, as upstream's, and the host's handler is
    told with `IsHostLevel` false; other errors (the root's render, a control's mount) replace the host's content (#16, #28).
  - Every re-render with a new boundary element retries the child; a new key remounts it.
- **Differences from upstream** (guide, "What differs from Reactor"):
  - a retry mounts the child afresh;
  - a fallback shown again keeps its state;
  - an unchanged boundary element doesn't retry;
  - a cleanup that throws while the old child is unmounted is logged;
  - out-of-memory and stack-overflow exceptions pass through.
- **Tests:** 28 new, all proven to fail. Upstream's `ErrorBoundaryTests.cs` is vendored and 8 of its 9 tests run.
- **Cost:** about +7 ms per 1,000 rows on mount, each row in its own boundary.
- **Guide:** `docs/guide/error-boundaries.md`.

## First automated weekly sync (issue #24, 2026-11-24)

- **The `upstream-sync` job's first real run** (`1f078436` → `561c5573`, 11 upstream commits) worked as designed: it pushed
  `sync/1f078436-561c5573`, started CI on it and opened #24 with the report.
- **3 vendored files changed,** no transform rule broke, and Yoga stayed at 780.
- **9 new upstream paths,** all classified by the tier map.
- **The compiler caught one break:** upstream's `ChildReconciler` now passes a new `childIndex:` argument to our seam's
  `Reconciler.UpdateChildTracked` (4 errors). The fix was to mirror the parameter in our seam (unused: it serves staggered
  animations, which Avalonia doesn't have yet).
- **Tier 3 PRs** (compositor implicit transitions, transforms on the composition visual, assembly names): none needs porting
  yet.
- **Coordinator time:** about 5 minutes from reading the issue to a green branch (session timestamps).

## Roslyn transforms (issue #4, 2026-11-24)

- **Decision: Roslyn complements the text-based drops; it doesn't replace them.** All 88 text drops convert to Roslyn drops
  with byte-identical output, and both forms survive the same 93 upstream commits. Roslyn regeneration is slower
  (1.39–1.51 s against 0.61–0.67 s).
- **Spike A step 4 (31 Roslyn rules):** edit lines fall from 89 to 53 (18 files to 15), and errors from 567 to 500. The
  same changes written as text rules give the same numbers. The 53 lines left need the seam or tier 3, not a better engine.
- **What does pay is `syntaxCheck`:** every C# file a rule changes must still parse. 15 of the 51 changed tier 2 files are
  compiled by no project, so this is their only check. It adds about 1.25 s to `--check`.
- **Element records stay hand-written:** the three Roslyn operations can't project upstream's 166 records.
- Details: `docs/spikes/A-engine-compile.md`, "Update: Roslyn transforms".

## Platform hooks (issue #20, 2026-11-24)

- **12 hooks on Avalonia, with Reactor's names,** in our partial `RenderContext`:
  - theme: `UseColorScheme`, `UseIsDarkTheme`, `UseHighContrast`;
  - window: `UseWindow`, `UseWindowSize`, `UseBreakpoint`, `UseWindowState`, `UseIsActive`, `UseWindowPosition`, `UseDpi`,
    `UseClosingGuard`;
  - displays: `UseDisplays`.
- **Behaviour:** each reads its value during the render, re-renders only on a real change, unsubscribes on unmount, and
  follows a host moved to another window. Guide: `docs/guide/platform-hooks.md`.
- **Tests:** 52 new, all proven to fail; 811 in all.
- **Not built:**
  - `UseReducedMotion` and `UseHighContrastScheme`: Avalonia 12 exposes neither.
  - `UseIntl`: upstream's `IntlAccessor` isn't vendored.
  - Aspect ratio, covered, drag-move: no matching Avalonia API.
  - Tray, pickers and the back button: later.
- **Differences from upstream:** the theme hooks read the host's own theme (a window's theme counts), and `UseWindow` takes
  hook slots so it can follow the host.

## App host (issue #16, 2026-11-23)

- **`ReactorHostControl`** (`Isotope.UI.Hosting`) renders a root component into any Avalonia layout, coalescing re-renders
  per dispatcher pass and surfacing render errors. `ReactorApp.Run(AppBuilder, …)` runs one window. Isotope.UI.Avalonia.Desktop adds upstream's `Run<TRoot>(title, width, height)` and `Run(title, render, …)` over a default builder (#32).
- **Sample app:** `samples/Isotope.UI.Samples` (a page per area, #16 and #30, chosen from a list in an inline split view beside it, #96; every page stays mounted
  and only the selected one is shown, so each keeps its state; the window is 860 wide, since beside the list the Icons page
  wants 727 with ubuntu-latest's default font and 641 on Windows, measured headless, and the Lists page's first line
  wraps; `SampleAppTests` drive each page headless; the real-window checklist is in its README); CI builds it. Every line of the
  checklist passed in a real window on Windows (#34, 2026-11-24; the Rich text and Icons pages and the page list on
  2026-11-26 at `cf10271`, #73 and #92: a button inside text loses focus after each press, as measured headless; the
  single-colour bitmap icon's edges aren't smooth, #98); Linux and macOS haven't been run in a real window. The window and menu lines were rewritten for the list; they and the Lists
  page passed in a real window on Windows on 2026-11-26 (#100).
- **Tests:** 756 (16 new, all proven to fail). A flaky test, caused by an unobserved task exception after a dispatcher
  error, was found and fixed.

## Spike G: the vertical slice (issue #2, 2026-11-23)

- **The premise works on Avalonia.** `UseState`, `UseReducer` (both forms), `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`
  and `UseContext` behave as upstream's engine defines them (14 tests).
  - One difference from React: upstream runs a parent's effects before its child mounts. The tests pin upstream's order.
- **605 of upstream's engine tests run against the library:** 40 vendored test files; 35 unchanged (548 tests), 5 through
  rules (57 tests, 8 tier 3 tests dropped).
- **Controls and layout:**
  - The controlled TextBox is tested (2 tests).
  - The keyed list keeps each row's controls, state and typed text through insert, remove, reorder and reverse (5 tests).
  - FlexPanel is library code over the vendored Yoga (21 tests).
- **Examples:** a counter and a todo app, driven by headless input and rendered to `docs/spikes/img/G-counter.png` and
  `G-todo.png`.
- **Total:** 711 tests; the 45 new ones were all proven to fail when broken.
- **Timings** (1,000 keyed rows, headless, medians over 3 runs): mount 37.5–40.2 ms; same-key re-render 1.2–1.6 ms;
  reverse 47.6–78.0 ms; one row's state change 0.34–0.45 ms.
- **Leaks:**
  - Avalonia 12 binds `Dispatcher.UIThread` to the first thread that reads it (fixed in the seam).
  - Avalonia clamps `DesiredSize`, so FlexPanel's CSS minimum size is lost (#15).
  - A keyed move detaches the control; Avalonia's `Controls.Move` doesn't, and is about 20 times faster (#14, Spike I; #23).
- **Next:** an app host (#16). Report: `docs/spikes/G-vertical-slice.md`.

## Decisions after Spike F (2026-11-23)

1. **`ISOTOPEGEN001`** (a record member with no Avalonia counterpart) **is a build error.** A silent drop is what it exists
   to catch.
2. **Control pooling is ported later,** and only if a measurement shows it pays (#14).
3. **Controlled values keep upstream's rule:** a TextBox element equal to the last render's is skipped, so a rejected edit
   stays in the box until the element changes; when it changes for any reason, the controlled value is written back if
   the box has drifted from it (upstream spec 047 §8). #2 pins both halves.
4. **Element factories (#17):** `Component(render)` stays as an Isotope addition (upstream only has `RenderEachTime`), and
   the guide says so. Factory names are **explicit**: `TextBlock`, not `Text`, because `Text` could mean many things. Shorthand names may be
   added later as an option (#13).
5. **Also kept as recommended:** TextBox's first-write suppression token, recording the element on every mounted
   control, and the generator project's `netstandard2.0` target (Roslyn requires it).

## Spike F: descriptor runtime and generated controls (issue #12, 2026-11-23)

- **The runtime compiles on Avalonia through transform rules only.** Upstream's 6 descriptor-runtime files (3,522 lines) are
  all tier 2: 13 member drops, 633 lines blanked and 18 rewritten. None is tier 3.
  - 7 files they depend on are vendored too (1,354 lines).
  - Two are rewritten for Avalonia in 67 lines (`ReactorBindingT.cs`, `ControlEventPayloads.cs`).
  - The reconciler gains 17 members under upstream's names.
- **Declaration errors** before the port: 75 at step 0, 72 at step 1 (`spikes/F-descriptors/run-spike.mjs`).
- **The generator is vendored:** 15 files, 4,411 lines. Our 20 `gen-*` rules (36 lines added, 41 removed, in 3 files) sit on
  top of the rename.
- **Button, TextBlock, StackPanel, TextBox and Slider now mount through generated descriptors:** 219 generated lines in 5
  files, against 35 hand-written `Customize` lines.
- **Tests:** the seam's 48 tests pass unchanged, plus 14 new: TextBox echo suppression, Slider pairing and bounds, records,
  and the generator diagnostic `ISOTOPEGEN001` for a member with no Avalonia counterpart. 62 in all.
- **Avalonia difference:** `TextBox` raises `TextChanged` after a write, even outside a window. The first controlled write
  therefore carries a suppression token too.
- **Report:** `docs/spikes/F-descriptor-runtime.md`.

## Decisions after the seam (2026-11-23)

1. **Namespaces are `Isotope.UI.*`.** One transform rule renames upstream's `Microsoft.UI.Reactor.*`, so the package ID and the
   namespaces match. `Avalonia.UI.Isotope` was considered but rejected, because the package ID can't start with `Avalonia.`.
2. **`RenderContext` is made `partial` by a rule**, for our implementation of the tier 3 hooks.
3. **Text-based member drops stay** until #4.
4. **Merged branches are deleted from the remote**; only `main` remains.
5. **Control layer (from Spike E):**
   - Descriptors are **generated** by a vendored copy of upstream's wrappers generator, and our changes to it are carried as **transform rules**.
   - Element records are **hand-written** for now; projecting them from upstream's `Element.cs` is decided with #4.
   - Records that mirror upstream's use **Reactor's member names** (mapped with `[WrapAlias]`), and Avalonia-only controls use Avalonia's. Avalonia's names everywhere, with a migration tool from Reactor, may come later (#13). *Superseded 2026-11-24 (#27 answer 1, #31): Avalonia's member names.*

## Seam (issue #1, 2026-11-23)

- **Upstream's `ChildReconciler.cs`, `Internal/KeyedListDiff.cs` and `RenderContext.cs` compile against our Avalonia
  seam with transforms only:**
  - Spike A's edit lines for them went from 12 + 5 + 0 to **0 + 0 + 0**.
  - `KeyedListDiff` needs no rule.
  - `ChildReconciler` needs one rule (two `using` lines).
  - `RenderContext` needs 37 member drops: 889 lines of tier 3 hooks, while the hooks core is untouched.
- **Tests:** 48 headless tests of the seam and the hooks core (render, state, effects, context, memo, keyed reorder, the
  render-loop guard, off-thread re-renders) and 12 tests of the rewrite tool. All 60 were proven to fail with their
  behaviour broken.
- **Library on vendored upstream (#11, 2026-11-23):**
  - `tools/sync` is the only transform engine. `transforms.json` has 78 rules: the library's 74 (7 find/replace, 67 member
    drops), the WinUIEx notice, `partial-render-context`, `event-provider-isotope` and `namespace-isotope`.
  - 167 files are vendored (116 tier 1, 51 tier 2). The library compiles 65 of them (16,062 lines): 64 transformed, 140
    lines rewritten, 1,023 blanked (`node tools/measure-transforms.mjs src/Isotope.UI.Avalonia/UpstreamFiles.props`).
  - It builds and passes its 48 tests without `upstream/`. Spike A stays at 567 and Yoga at 780.

## Tracking

Open work is tracked in GitHub Issues (since 2026-11-23): #1 seam types (done 2026-11-23), #2 vertical slice, #3 `tools/sync` (done 2026-11-23: 155 files vendored, first sync to `14172dce`), #4 Roslyn transforms, #5 first real sync, #6 CI, #7 PR process (hands-on), #8 wrappers-generator research, #9 FlexPanel ambient-mode divergence, #10 personal-data check in `tools/sync` (done 2026-11-23).

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
