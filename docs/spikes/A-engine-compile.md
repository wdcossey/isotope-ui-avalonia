# Spike A: how much of the engine compiles without WinUI

Measured 2026-11-22 at upstream `28a87225` (UPSTREAM.lock) with the .NET 10 SDK (10.0.402, C# 14) and Avalonia 12.1.6. Project: `spikes/A-engine/` — a plain `net10.0` library that **links** upstream files (`EngineFiles.props`), never copies them. Re-run: `node spikes/A-engine/run-spike.mjs [1|2|3]`. Per-file results: `spikes/A-engine/outcomes.json`; the seam: `spikes/A-engine/seam.json`. Errors grouped by `tools/build-errors.mjs`.

## Method

1. **Step 0** — the 50 candidate files from HANDOVER.md (RenderContext, ChildReconciler, KeyedListDiff, Yoga minus FlexPanel, Hooks, Data, Validation; 18,025 lines) alone.
2. **Closure** — repeatedly add the upstream file that declares each missing Reactor type, until only non-Reactor names are missing.
3. **Split the closure**: WinUI-free helpers and lightly-coupled helpers are linked verbatim; the WinUI-heavy layer (`Core/Element*.cs`, `Core/Reconciler*.cs`, `Core/V1Protocol/`, `Elements/`, `Hosting/`, `Animation/`, `Input/`, `Accessibility/`, `Yoga/FlexPanel.cs`) is **not** linked. Every type the linked files use from it gets a generated empty stub (`Shims/Stubs.g.cs`, 78 types). The stubs' missing members are then the **seam**: what our Avalonia layer must provide, under upstream's names, for the linked files to compile unchanged.
4. Then the cheapest fixes in order, as HANDOVER asked: **step 1** global `using` aliases onto Avalonia types (`Shims/Step1`); **step 2** shim types for WinUI concepts with no single Avalonia type (`Shims/Step2`); **step 3a** a deterministic rewrite on import (`tools/rewrite-upstream.mjs` + `rewrite-rules.json`, 3 rules, run by MSBuild before compile so upstream files stay untouched); **step 3b** what is left = edits.

The counts in the table come from the build logs of each step as it was run, with the file list of that moment. `run-spike.mjs` uses the final file list for every step, so it reproduces step 3b exactly (572) but gives 88 for step 1 and 14 for step 2.

Roslyn reports no method-body errors while declaration errors remain, so the step counts fall and then rise once declarations are clean; the step-3b number is the first complete picture.

## Error counts per step

| Step | Linked files | Errors | What dominated |
|---|---|---|---|
| 0: candidates only | 50 | 346 | `Element` (89), WinUI namespaces, `Reconciler`, `UIElement` |
| Closure (all upstream deps linked) | 165 files, 71,086 lines (80 files / 54,228 lines reference WinUI) | 2,057 | the whole WinUI layer: `UIElement` 381, `Microsoft.UI.Xaml` 292, `FrameworkElement` 231 |
| Split + stubs, no aliases | 97 | 246 | WinUI names (declarations) |
| 1: + global aliases, placeholder namespaces | 97 | 129 | fully-qualified WinUI names, pickers, `WindowId`, `Navigation.*` |
| 2: + WinUI-concept shims, + Navigation/Localization/LruCache linked | 115 | 44 | **public signatures exposing WinUI types** (19), fully-qualified identity types |
| 3a: + mechanical import rewrite (12 files) | 115 | 4 → 1 | `UseAnnounce.cs` (source-generated WinUI wrapper) moved to "rewrite" |
| 3b: declarations clean, method bodies checked | 114 | **572** | seam 404, shim members 82, edits 86 |

## Outcome per file (step 3b)

28,617 lines in 114 linked upstream files, plus `Hooks/UseAnnounce.cs` (173 lines) set aside.

| Outcome | Files | Lines | Meaning |
|---|---|---|---|
| Compiles unmodified | 71 | 12,398 | taken verbatim, nothing else needed |
| Compiles after the mechanical rewrite | 4 | 770 | verbatim + a scripted rewrite on every import |
| Verbatim given the seam | 15 | 5,818 | errors only on members of stubbed upstream types |
| Verbatim with shims | 6 | 1,885 | also needs members on WinUI-concept shims |
| Needs edits | 18 | 7,746 | at least one error our layer can't absorb |
| Rewrite (not linked) | 1 | 173 | `Hooks/UseAnnounce.cs` |
| Edited copies for C# 14 | 2 | 550 | `Core/AsyncValue.cs`, `Core/InfiniteResource.cs`: 2 lines (`closed`) — forced by the net10 SDK pin |

By the handover's areas:

| Area | Unmodified | Rewrite/seam/shims | Needs edits |
|---|---|---|---|
| Yoga (minus FlexPanel), 5,046 L | **5,046** | 0 | 0 |
| Data, 1,070 L | 973 | 97 | 0 |
| Validation, 3,596 L | 669 | 2,363 | 564 |
| Hooks, 2,595 L linked | 81 | 2,190 | 324 |
| KeyedListDiff, 938 L | 0 | 938 (11 seam errors) | 0 |
| ChildReconciler, 1,454 L | 0 | — | 1,454 (5 edit lines; 94 seam errors) |
| RenderContext, 3,153 L | 0 | — | 3,153 (8 edit lines; 66 seam + 21 shim errors) |
| Helpers pulled in by the closure, 10,765 L | 5,629 | 2,885 | 2,251 |

### Files that need edits

The edit count is the number of distinct source lines with an error our layer can't absorb (a lower bound: see Limits).

| File | Lines | Edit lines | Seam errors | Shim errors |
|---|---|---|---|---|
| Core/RenderContext.cs | 3153 | 8 | 66 | 21 |
| Core/ChildReconciler.cs | 1454 | 5 | 94 | 0 |
| Core/Diagnostics/ReactorDiagnostics.Components.cs | 455 | 2 | 33 | 1 |
| Core/Component.cs | 398 | 1 | 0 | 0 |
| Core/Theme.cs | 347 | 8 | 2 | 5 |
| Controls/PropertyGrid/TypeRegistry.cs | 337 | 11 | 20 | 0 |
| Diagnostics/ReactorSourceMap.cs | 272 | 1 | 23 | 0 |
| Core/Diagnostics/AppliedModifierMap.cs | 229 | 21 | 74 | 0 |
| Controls/Validation/ValidationRule.cs | 221 | 2 | 2 | 0 |
| Controls/Validation/ValidationVisualizer.cs | 179 | 3 | 3 | 0 |
| Hooks/UseFocusTrap.cs | 173 | 4 | 2 | 4 |
| Controls/Validation/FormField.cs | 164 | 4 | 3 | 0 |
| Controls/Virtualization/VirtualListElement.cs | 127 | 5 | 0 | 2 |
| Hooks/Pending.cs | 89 | 2 | 4 | 0 |
| Hooks/UseElementFocus.cs | 62 | 1 | 0 | 2 |
| Controls/Virtualization/VirtualListFactories.cs | 36 | 1 | 0 | 0 |
| Controls/PropertyGrid/PropertyGridFactories.cs | 28 | 1 | 0 | 0 |
| Core/DragAttached.cs | 22 | 1 | 0 | 0 |

**Total: 81 edit lines in 18 files.** Typical causes: Avalonia event delegate shapes (`EventHandler<KeyEventArgs>` vs WinUI's typed handlers), WinUI-only members on aliased types (`Control.RequestedTheme`, `ScrollViewer.ChangeView`, `XamlRoot`, `Visibility`), WinUI control types named fully-qualified for diagnostics (`AppliedModifierMap.cs`: Border, Grid, StackPanel, RelativePanel), and the PropertyGrid editor table naming WinUI controls.

## The seam

**231 distinct members on 61 upstream types** that the linked files use and our Avalonia layer must provide under the same names. Largest:

| Type | Members | Examples |
|---|---|---|
| `ElementModifiers` | 54 | AccessKey, Accessibility, AttachedFlyout, AutomationId, AutomationName, Background, BorderBrush, BorderInlineStart, BorderThickness, CenterPoint, ContextFlyout, CornerRadius, … |
| `Reconciler` | 33 | ApplyAmbientEnterAnimation, CallSiteChangedOnSkip, CanUpdate, CountPerfChanges, DebugElementsSkipped, DefaultAutomationNameFromCaption, DiagnosticComponent, ForceFullRenderActive, ForceRenderThroughWrapper, GetMountedElement, GetOrCreateReactorState, HasActiveContextValues, … |
| `ReactorWindow` | 16 | Activated, BeginDragMove, Deactivated, Dpi, DpiChanged, IsActive, NativeWindow, PersistedScope, Position, PositionChanged, RegisterAspectRatioOverride, RegisterClosingGuard, … |
| `Element` | 11 | CS8121, CallSite, CanSkipUpdate, ContextValues, ElementTransition, GetAttached, HasCallbacks, IsVisible, Key, Modifiers, Provide |
| `Editors` | 10 | CheckBox, Color, ColorCompact, Date, DateOffset, DateOnly, TimeOnlyEditor, TimeSpanEditor, Toggle, Uri |
| `ReactorApp` | 8 | DefaultRenderErrorHandler, DevtoolsEnabled, FindTrayIcon, FindWindow, OpenTrayIcon, OpenWindow, UIDispatcher, Windows |
| `DiagnosticText` | 8 | DelegateName, Format, FormatPlain, FriendlyTypeName, IsEditable, PropRows, TryParse, TypeName |
| `ComponentNames` | 5 | For, ForRenderFunction, FuncFallback, MemoFallback, RootRenderFunctionFallback |
| `CellRenderers` | 5 | CheckMark, ColorSwatch, Date, Hyperlink, Time |
| `ChildSlots` | 4 | AlignByIndex, At, ForIndex, Get |
| `ComponentNode` | 4 | Component, Context, Element, EnsureDiagnosticId |
| `RootComponentSource` | 4 | Component, FuncContext, IsAlive, RenderFunc |

`ElementModifiers` (54) is the modifier record on every element (Margin, Padding, Background, Width, alignment, tooltip, accessibility, focus navigation…); `Element` (11) is the base record (Key, Modifiers, CallSite, CanSkipUpdate, GetAttached…); `Reconciler` (33) is what ChildReconciler and RenderContext call back into (Mount, UnmountChild, UpdateChildTracked, exit transitions, skip decisions, component-node lookup). Full list: `seam.json`.

**Shim surface: 57 members on 17 WinUI-concept types** — pickers (`FileOpenPicker`/`FolderPicker`/`FileSavePicker`: 36), `UISettings`/`AccessibilitySettings` (5), `DispatcherQueue` (GetForCurrentThread, TryEnqueue), focus and key event args, `VisualTreeHelper.GetParent`, `ItemsRepeater`.

## Findings

1. **"The engine" isn't a set of files.** RenderContext.cs is the hooks core *and* ~25 WinUI-specific hooks (window size/state, theme, accessibility settings, displays, pickers, tray, breakpoints, hot reload). The base `Element` record (lines 24-1595 of the 8,141-line `Element.cs`) shares its file with ~160 WinUI-typed control records, and its equality code switches on concrete WinUI elements and compares `Brush`/`FontFamily`. ChildReconciler takes `Reconciler` and `UIElement` directly. Followed to closure, the 50 candidate files pull in 165 files and 71k lines.
2. **Aliases alone can't make them compile.** A `global using` alias covers bare names only; upstream also uses fully-qualified WinUI names and `using WinUI = Microsoft.UI.Xaml.Controls;` aliases, which need real types in those namespaces. A mechanical rewrite on import (3 regex rules) handles those cleanly, in 12 files.
3. **Upstream's public hook signatures expose WinUI types** (`PickFileResult`, `FocusState`, `PickerLocationId`, `SystemBackdrop`, `Window` parameters). Verbatim RenderContext therefore puts WinUI-namespaced shim types into our public API, against decision 2 (Avalonia-native). Those ~25 hooks must be rewritten (or hidden) on our side regardless.
4. **The verbatim route is viable for most of the engine by line count:** 20,871 of 28,617 linked lines (73%) need no edits — **but only if our Avalonia layer keeps upstream's internal shapes** for `Element`/`ElementModifiers`/`Reconciler`/`Component` and ~57 other types (231 members). That is the price of verbatim: our reconciler and element records are written fresh for Avalonia but must look like upstream's from the engine's side.
5. **The two hottest upstream files (Spike B) are the two that need edits:** RenderContext (8 edit lines) and ChildReconciler (5). The edits are small; they would be patches carried across every sync.
6. **Yoga is free:** 5,046 lines compile unmodified with nothing else.

## Limits

- **Lower bound on edits.** Stub members don't exist, so expressions that use their results aren't type-checked yet; implementing the seam will surface more mismatches. Shim and alias semantics (e.g. `FrameworkElement` and `UIElement` both aliased to `Avalonia.Controls.Control`) are compile-checked only.
- Nothing here ran: no tests, no behaviour check (Spike D does that for a subset).
- The heavy/light split is a judgement (file prefixes listed in Method); moving `Component.cs` or the diagnostics files to "rewrite" would shrink both the seam and the edits.
- The seam counts members, not effort: `Reconciler.Mount` is one member and most of a reconciler.
