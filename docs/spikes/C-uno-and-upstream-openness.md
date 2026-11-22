# Spike C: how Uno did it, and how open upstream is to platform seams

Researched 2026-11-22 by a research agent (Opus): read-only git on `upstream`, read-only `gh`, web fetches. **[V]** = read at the source, **[U]** = unverified, **[J]** = judgement.

## Summary

- **The claim holds, with a correction:** Microsoft has not retargeted Reactor. Uno Platform maintains a **fork**, `unoplatform/Uno.microsoft-ui-reactor` (branch `uno`), shipped as NuGet `Uno.Microsoft.UI.Reactor` and enabled in Uno 7.0 by the `Reactor` UnoFeature. Upstream itself is still Windows-only. [V]
- **Uno compiles upstream source nearly untouched:** at the fork's merge base the Uno project compiles 287 of 337 `src/Reactor` files (86,594 of 103,810 lines, 83.4%) and 82 of 141 `Reactor.Advanced` files; only 5 of those source files differ from Microsoft's (+88/−16). Uno's own code: 15 files, 2,902 lines. [V, measured]
- **They sync by git merge with upstream history, not by copying files.** A nightly bot merges upstream `main`; clean merges auto-merge. Even so the fork is **102 commits / 13 days behind**, and the current sync PR has been a **conflicted draft since 2026-11-11**, stuck on `global.json` and `Reconciler.cs`. [V]
- **Upstream welcomes ports hosted elsewhere, not seams inside upstream.** The main maintainer asked Uno to host the port itself (offering to accept a one-line change, which never landed); specs reject an abstractions layer (spec 062) and make non-Win32 windowing a non-goal (spec 036 N1); MAUI support in-repo was declined (#1408). [V]

## 1. The claim

| Claim | Source | Status |
|---|---|---|
| InfoQ, 2026-11-16: "Uno Platform 7.0 Adds WebGPU Backend, Apple TV and Reactor Support…": Reactor on desktop, browser, Android, iOS, in preview via the `Reactor` UnoFeature. (URL slug says 6-7; the article is about 7.0.) | https://infoq.com/news/2026/11/uno-platform-6-7-targets | [V] via a summarising fetch |
| Uno blog: "With 7.0, Reactor apps run on Uno Platform too…", "in preview", "an automated sync brings the latest changes from the Microsoft.UI.Reactor repository over with little effort." | https://platform.uno/blog/uno-platform-7-0/ | [V] |
| Docs: `UnoFeatures=Reactor` or the `unoapp-reactor` template; Uno.Sdk 7.0, .NET 10; Uno targets only (no Windows App SDK head, error UNOB0031). | https://platform.uno/docs/articles/features/using-reactor.html | [V] |
| SDK feature PR merged 2026-10-28; package version pinned in `packages.json`. | https://github.com/unoplatform/uno/pull/25745 | [V] |
| NuGet `Uno.Microsoft.UI.Reactor`: 31 prerelease versions (0.1.0-dev.74 … 0.2.0-dev.8), 2,928 downloads. | NuGet API | [V] |

## 2. Source changes, the fork, and how it tracks upstream

**History.** Upstream PR #1079 (opened 2026-08-06 by an Uno engineer, +4,027/−1, "a retargeting, not a fork"). On 2026-10-06 the main maintainer: "could we move forward with this and get it into Uno itself? We can still accept the minor change in Reactor to make the analyzers happy." Closed unmerged 2026-11-17. The fork was created 2026-10-13 (fork PR #1, +4,458/−5). [V]

**Fork state** (`microsoft:main...uno`, cross-checked with `git rev-list`) [V]:

| Measure | Value |
|---|---|
| Ahead of upstream `main` | 233 commits (40 merges) |
| Behind | 102 commits; merge base 686b0ec9 (2026-11-09) |
| Microsoft source files patched | 5 in `src/Reactor` (+88/−16): `Reconciler.cs` (+7/−1), `Reconciler.Mount.cs` (+4/−1, `return null` → `return default`), `AnimationHelper.cs` (+6/−6), `TransitionEngine.cs` (+5/−8), new `Animation/AnimationCapabilities.cs` (66 lines) |
| Other Microsoft files patched | README, `global.json` (SDK floor 10.0.400 + Uno.Sdk pin), `Directory.Packages.props`, `Reactor.Wrappers.Generator.csproj` |
| Upstream source compiled | `src/Reactor` 287/337 files (83.4% of lines); `Hosting/` only 14 of 63 files (1,102 of 18,301 lines) |
| Uno's own code | `src/Reactor.Uno`: 15 files, 2,902 lines (hosting, windowing, pickers, shims, 2 `global using` aliases) |

**Mechanics** (fork's `upstream-sync.yml`, `MAINTAINING.md`, `label-upstream-files.yml`) [V]:
- Nightly (04:00 UTC) merge of upstream `main` into `automation/upstream-sync`; clean → PR auto-merges as a merge commit; conflicted → committed with markers, opened as a draft labelled `conflicts`. `EXCLUDED_UPSTREAM_PRS` skips chosen upstream PRs (`git merge -s ours`).
- Rule: "Don't modify Microsoft's files." Workaround order: remove the file from the build → shim → `GlobalUsings.cs` alias → build setting. Real fixes go upstream. A patch ledger with a "Remove when" column. No `#if` blocks in shared source.
- Guards: PR label `warning/touches-upstream`; Microsoft's CI workflows disabled after each sync; build checks `VerifySharedSourceRoots` (an included upstream folder vanished — once a move into `Reactor.Advanced` silently dropped 74 files with a green build) and `VerifyHostingTriage` (an unclassified new `Hosting/` file).

**How well it works** [V]: since the fork's base (2026-10-07) upstream merges landed twice (85 commits on 11-09, 3 on 11-10). Sync PR #42 (100 commits) is conflicted on `global.json` (upstream #1490 on 2026-11-12 moved the pin to SDK 11.0.100 for C# 15) and `Reconciler.cs`. The nightly failed every night 10-15 → 11-10 (27 failures; cause [U]). Before launch, upstream drift broke the port four times (new WinRT/WinUI-typed references in shared files) plus two new `Hosting/` files needing triage.

**Not supported on Uno** (fork README) [V]: custom title bar, ListBox, ParallaxView, MapControl, SemanticZoom, AnnotatedScrollBar, high-contrast detection, tray/jump list/taskbar, window persistence, backdrop, display enumeration, drag-move/aspect lock, docking, in-app devtools, Win2D. Partial: implicit/spring animations, RichTextBlock/Markdown, some gesture and access-key flags, Charting/DataGrid. Not shipped: analyzers and source generators. Works: hooks and reconciler core ("full parity"), Yoga/Flex, core controls, theming, virtualized lists, multi-window desktop, DPI, pickers, Hot Reload.

## 3. Upstream openness to platform seams

| Evidence | Status |
|---|---|
| `git log --grep` "uno": 1 of 2,392 commits, and it *removes* Uno code paths from vendored docking code. avalonia / maui / cross-platform / "platform abstraction": 6 commits, none a seam. | [V] |
| One merged PR from an Uno engineer (#1456): a general controlled-input bug fix, not a seam. | [V] |
| The offered one-line `return default` change was never sent: `Reconciler.Mount.cs:486` still `return null;` on `origin/main`. | [V] |
| #1408 (MAUI): "Reactor isn't built on a cross-platform UI layer today… several engineer-months… keeping MAUI support out of this repo makes sense for now." | [V] |
| Spec 062 §4.1 "A wrapper inherently names WinUI"; §4.2 "The authoring seam is welded to the runtime"; §5.1 rejects a `Reactor.Abstractions` package and an "`IRuntimeOps`-style interface". | [V] `upstream/docs/specs/062-…md` |
| Spec 036 N1: "Windowing on non-Win32 (Reactor is WinUI 3, desktop only)." | [V] |
| Platform-looking interfaces in `src/Reactor`: `IHookDispatcher` (wraps the WinUI dispatcher for tests/headless), `IChartingHostBridge`, `IReactorDiagnosticHost`, `IReactorDevtoolsHost` — all for tests or packaging, none isolates WinUI. | [V] |
| No roadmap file; README: "Status: Experimental… public API surface may change without notice." | [V] |

## 4. Upstream's own docs on the boundary

- **Architecture overview** (`upstream/docs/guide/architecture-overview.md`): "a declarative shell over WinUI… a new authoring surface that happens to materialize WinUI". No platform layer among its subsystems; WinUI types in its signatures. [V]
- **Extending Reactor controls** (`extending-reactor-controls.md`, `control-reconciler-protocol.md`): the per-control seam is an `Element` record + a descriptor or handler typed on a WinUI control (`TControl : UIElement`) + a child strategy + `ControlRegistry.Register<TElement,TControl>`. This is the layer we rewrite. [V]
- **`docs/research/compare/avalonia.md`** (707 lines): a competitive analysis grading Avalonia 11-12 (Layout A−, Styling A−, Platform Reach A−, Component Architecture B "a generation behind function-as-component", Error Handling C+). Nothing about porting Reactor; `overview.md` calls Reactor's Windows-only reach "Unbridgeable without Avalonia/Uno". Upstream sees Avalonia as a competitor, not a target. [V]

## 5. Reading [J]

- A core/platform split proposed to upstream is **not realistic** as a refactor. Realistic: small behaviour-neutral fixes that shrink a patch set. Plan for no help from upstream.
- Uno's sync works because it keeps WinUI's API, so 83% compiles as-is. With an Avalonia-native API our verbatim share is capped at the engine subset (Spike A).
- Even with 5 patched files and a nightly bot, Uno is 13 days behind and stuck 11 days on 2 conflicts, one of them a toolchain bump. **Budget recurring human merge time;** the bot finds conflicts, it doesn't resolve them.
- Worth copying (MIT): include rules + "our paths" list; patch ledger with "remove when"; workaround order (include rules → shims → aliases → edits last); build checks against silently-empty includes and unclassified new files; CI on changes to shared upstream folders; a skip list for upstream PRs.
- For files we *rewrite*, upstream changes won't break our build: we need a per-sync report of upstream changes to them.
- Uno already gives Reactor on Linux/macOS/WASM; our distinct value is the Avalonia-native API and ecosystem, not reach.

## Not done

Uno's private tracker isn't accessible; the October nightly failures' logs weren't read; InfoQ was read through a summarising fetch; nothing was built.
