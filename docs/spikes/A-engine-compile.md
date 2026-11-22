# Spike A: how much of the engine compiles without WinUI

Measured 2026-11-22 at upstream `28a87225` (UPSTREAM.lock) first with the .NET 10 SDK (10.0.402, C# 14), then re-run with the .NET 11 SDK (11.0.100, C# 15; 567 errors either way) and Avalonia 12.1.6. Project: `spikes/A-engine/`, a plain `net10.0` library that **links** upstream files (`EngineFiles.props`) and never copies them. Re-run with `node spikes/A-engine/run-spike.mjs [1|2|3]`. Per-file results: `spikes/A-engine/outcomes.json`. The seam and the shim surface: `spikes/A-engine/seam.json`. Errors are grouped by `tools/build-errors.mjs`.

## Method

1. **Step 0:** the 50 candidate files from HANDOVER.md, built alone (RenderContext, ChildReconciler, KeyedListDiff, Yoga minus FlexPanel, Hooks, Data, Validation; 18,025 lines).
2. **Closure:** repeatedly add the upstream file that declares each missing Reactor type, until only non-Reactor names are missing.
3. **Split the closure.** WinUI-free and lightly coupled helper files are linked verbatim. The WinUI-heavy layer (`Core/Element*.cs`, `Core/Reconciler*.cs`, `Core/V1Protocol/`, `Elements/`, `Hosting/`, `Animation/`, `Input/`, `Accessibility/`, `Yoga/FlexPanel.cs`) is **not** linked. Each type the linked files use from it gets a generated empty stub (`Shims/Stubs.g.cs`, 77 types). A stub keeps upstream's base class but not its interfaces. The stubs' missing members are then the **seam**: what our Avalonia layer must provide, under upstream's names, for the linked files to compile unchanged.
4. Then the cheapest fixes, in the order HANDOVER asked for:
   - **Step 1:** global `using` aliases onto Avalonia types (`Shims/Step1`).
   - **Step 2:** shim types for WinUI concepts that have no single Avalonia type (`Shims/Step2`).
   - **Step 3a:** a deterministic rewrite on import (`tools/rewrite-upstream.mjs` with the 3 rules in `rewrite-rules.json`). MSBuild runs it before compiling, and `#line` maps its errors back to upstream lines, so the upstream files stay untouched.
   - **Step 3b:** whatever is left counts as edits.

The step counts below come from each step's build log as it was run, with that moment's file list. `run-spike.mjs` uses the final file list for every step: it reproduces step 3b exactly (567) but gives different counts for steps 1 and 2.

Roslyn reports no method-body errors while declaration errors remain. So the counts fall, then rise once the declarations are clean; the step 3b count is the first complete picture.

## Error counts per step

| Step | Linked files | Errors | What dominated |
|---|---|---|---|
| 0: candidates only | 50 | 346 | `Element` (89), WinUI namespaces, `Reconciler`, `UIElement` |
| Closure (all upstream dependencies linked) | 165 files, 71,086 lines (80 files and 54,228 lines reference WinUI) | 2,057 | the whole WinUI layer: `UIElement` 381, `Microsoft.UI.Xaml` 292, `FrameworkElement` 231 |
| Split + stubs, no aliases | 97 | 246 | WinUI names (declarations) |
| 1: + global aliases and placeholder namespaces | 97 | 129 | fully qualified WinUI names, pickers, `WindowId`, `Navigation.*` |
| 2: + WinUI-concept shims; Navigation, Localization and LruCache linked | 115 | 44 | **public signatures exposing WinUI types** (19), fully qualified identity types |
| 3a: + mechanical import rewrite (12 files) | 115 | 4 → 1 | `UseAnnounce.cs` (a source-generated WinUI wrapper) moved to "rewrite" |
| 3b: declarations clean, method bodies checked | 116 | **567** | seam 374 (+15 constructor shapes), shim members 82, edits 96 |

## Outcome per file (step 3b)

116 upstream files and 29,167 lines are linked. `Hooks/UseAnnounce.cs` (173 lines) is set aside.

| Outcome | Files | Lines | Meaning |
|---|---|---|---|
| Compiles unmodified | 73 | 12,948 | taken verbatim, nothing else needed |
| Compiles after the mechanical rewrite | 4 | 770 | verbatim, plus a scripted rewrite on every import |
| Verbatim given the seam | 15 | 5,818 | the only errors are on members of stubbed upstream types |
| Verbatim with shims | 6 | 1,885 | also needs members on WinUI-concept shims |
| Needs edits | 18 | 7,746 | at least one error our layer can't absorb |
| Rewrite (not linked) | 1 | 173 | `Hooks/UseAnnounce.cs` |

**21,421 of 29,167 linked lines (73%) need no edits.**

By the handover's areas:

| Area | Unmodified | Rewrite, seam or shims | Needs edits |
|---|---|---|---|
| RenderContext, 3,153 L | 0 | 0 | 3,153 (12 edit lines) |
| ChildReconciler, 1,454 L | 0 | 0 | 1,454 (5 edit lines) |
| KeyedListDiff, 938 L | 0 | 938 | 0 |
| Hooks, 2,595 L | 81 | 2,190 | 324 (7 edit lines) |
| Yoga (minus FlexPanel), 5,046 L | 5,046 | 0 | 0 |
| Data, 1,070 L | 973 | 97 | 0 |
| Validation, 3,596 L | 669 | 2,363 | 564 (9 edit lines) |
| Helpers pulled in by the closure, 11,315 L | 6,179 | 2,885 | 2,251 (56 edit lines) |

### Files that need edits

The edit count is the number of distinct source lines with an error our layer can't absorb. It is a lower bound (see Limits).

| File | Lines | Edit lines | Seam errors | Shim errors |
|---|---|---|---|---|
| Core/Diagnostics/AppliedModifierMap.cs | 229 | 21 | 74 | 0 |
| Core/RenderContext.cs | 3,153 | 12 | 62 | 21 |
| Controls/PropertyGrid/TypeRegistry.cs | 337 | 11 | 20 | 0 |
| Core/Theme.cs | 347 | 8 | 2 | 5 |
| Core/ChildReconciler.cs | 1,454 | 5 | 92 | 0 |
| Diagnostics/ReactorSourceMap.cs | 272 | 5 | 17 | 0 |
| Controls/Virtualization/VirtualListElement.cs | 127 | 5 | 0 | 2 |
| Hooks/UseFocusTrap.cs | 173 | 4 | 2 | 4 |
| Controls/Validation/FormField.cs | 164 | 4 | 3 | 0 |
| Controls/Validation/ValidationVisualizer.cs | 179 | 3 | 3 | 0 |
| Hooks/Pending.cs | 89 | 2 | 4 | 0 |
| Controls/Validation/ValidationRule.cs | 221 | 2 | 2 | 0 |
| Core/Diagnostics/ReactorDiagnostics.Components.cs | 455 | 2 | 33 | 1 |
| Hooks/UseElementFocus.cs | 62 | 1 | 0 | 2 |
| Controls/Virtualization/VirtualListFactories.cs | 36 | 1 | 0 | 0 |
| Controls/PropertyGrid/PropertyGridFactories.cs | 28 | 1 | 0 | 0 |
| Core/Component.cs | 398 | 1 | 0 | 0 |
| Core/DragAttached.cs | 22 | 1 | 0 | 0 |

**Total: 89 edit lines in 18 files.** The typical causes:
- Avalonia event delegate shapes, such as `EventHandler<KeyEventArgs>` where WinUI has its own typed handlers.
- WinUI-only members on aliased types (`Control.RequestedTheme`, `ScrollViewer.ChangeView`, `XamlRoot`, `Visibility`).
- WinUI control types named fully qualified for diagnostics: `AppliedModifierMap.cs` names Border, Grid, StackPanel and RelativePanel.
- The PropertyGrid editor table, which names WinUI controls.

## The seam

**226 distinct members on 55 upstream types**, which the linked files use and our Avalonia layer must provide under the same names. On top of that, 7 types are called with constructor shapes our layer must match (`ChildDiffHint`, `ElementRef`, `ErrorBoundaryElement`, `HookOrderException`, `IntlAccessor`, `ObservableTreeTracker`, `ReswResourceProvider`). The largest:

| Type | Members | Examples |
|---|---|---|
| `ElementModifiers` | 54 | AccessKey, Accessibility, AttachedFlyout, AutomationId, AutomationName, Background, BorderBrush, BorderInlineStart, BorderThickness, CenterPoint, … |
| `Reconciler` | 33 | ApplyAmbientEnterAnimation, CallSiteChangedOnSkip, CanUpdate, CountPerfChanges, DebugElementsSkipped, DefaultAutomationNameFromCaption, DiagnosticComponent, ForceFullRenderActive, ForceRenderThroughWrapper, GetMountedElement, … |
| `ReactorWindow` | 16 | Activated, BeginDragMove, Deactivated, Dpi, DpiChanged, IsActive, NativeWindow, PersistedScope, Position, PositionChanged, … |
| `Element` | 10 | CallSite, CanSkipUpdate, ContextValues, ElementTransition, GetAttached, HasCallbacks, IsVisible, Key, Modifiers, Provide |
| `Editors` | 10 | CheckBox, Color, ColorCompact, Date, DateOffset, DateOnly, TimeOnlyEditor, TimeSpanEditor, Toggle, Uri |
| `ReactorApp` | 8 | DefaultRenderErrorHandler, DevtoolsEnabled, FindTrayIcon, FindWindow, OpenTrayIcon, OpenWindow, UIDispatcher, Windows |
| `DiagnosticText` | 8 | DelegateName, Format, FormatPlain, FriendlyTypeName, IsEditable, PropRows, TryParse, TypeName |
| `CellRenderers` | 5 | CheckMark, ColorSwatch, Date, Hyperlink, Time |
| `ComponentNames` | 5 | For, ForRenderFunction, FuncFallback, MemoFallback, RootRenderFunctionFallback |
| `T` | 4 | GetAttached, Modifiers, SetAttached, ThemeBindings |
| `ChildSlots` | 4 | AlignByIndex, At, ForIndex, Get |
| `ComponentNode` | 4 | Component, Context, Element, EnsureDiagnosticId |

- `ElementModifiers` is the modifier record on every element: Margin, Padding, Background, Width, alignment, tooltip, accessibility, focus navigation and so on.
- `Element` is the base record: Key, Modifiers, CallSite, CanSkipUpdate, GetAttached and so on.
- `Reconciler` is what ChildReconciler and RenderContext call back into: Mount, UnmountChild, UpdateChildTracked, exit transitions, skip decisions, component-node lookup.

The full list is in `seam.json`.

**Shim surface: 52 members on 14 WinUI-concept types.** These are the pickers (`FileOpenPicker`, `FolderPicker`, `FileSavePicker`), `UISettings` and `AccessibilitySettings`, `DispatcherQueue` (GetForCurrentThread, TryEnqueue), focus and key event args, `VisualTreeHelper.GetParent` and `ItemsRepeater`.

## Findings

1. **"The engine" isn't a set of files.**
   - `RenderContext.cs` is the hooks core *and* about 25 WinUI-specific hooks: window size and state, theme, accessibility settings, displays, pickers, tray, breakpoints, hot reload.
   - The base `Element` record (lines 24 to 1595 of the 8,141-line `Element.cs`) shares its file with about 160 WinUI-typed control records. Its equality code switches on concrete WinUI elements and compares `Brush` and `FontFamily` values.
   - `ChildReconciler` takes `Reconciler` and `UIElement` directly.
   - Followed to closure, the 50 candidate files pull in 165 files and 71k lines.
2. **Aliases alone can't make the engine compile.**
   - A `global using` alias covers bare names only. Upstream also uses fully qualified WinUI names, and `using WinUI = Microsoft.UI.Xaml.Controls;` aliases, which need real types in those namespaces.
   - A mechanical rewrite on import (3 regex rules, 12 files) handles those cleanly.
3. **Upstream's public hook signatures expose WinUI types:** `PickFileResult`, `FocusState`, `PickerLocationId`, `SystemBackdrop`, and `Window` parameters.
   - A verbatim `RenderContext` therefore puts WinUI-namespaced shim types into our public API, against decision 2 (Avalonia-native).
   - Those hooks have to be rewritten or hidden on our side regardless.
4. **The verbatim route is viable for most of the engine by line count:** 73% of the linked lines need no edits.
   - **The condition:** our Avalonia layer has to keep upstream's internal shapes for `Element`, `ElementModifiers`, `Reconciler`, `Component` and the other seam types.
   - That is the price of verbatim: our reconciler and element records are written fresh for Avalonia, but from the engine's side they must look like upstream's.
5. **The two hottest upstream files (Spike B) are among the ones that need edits:** `RenderContext` (12 edit lines) and `ChildReconciler` (5).
   - The edits are small, but they would be patches carried across every sync.
6. **Yoga is free:** its 5,046 lines compile unmodified with nothing else needed.

## Limits

- **The edit count is a lower bound.**
  - Stub members don't exist, so expressions that use their results aren't type-checked yet; implementing the seam will surface more mismatches.
  - Stubs drop upstream's interfaces, so conversions to those interfaces aren't checked.
  - Shim and alias semantics are only compile-checked. For example, `FrameworkElement` and `UIElement` both alias `Avalonia.Controls.Control`.
- **Nothing here ran:** no tests and no behaviour checks. Spike D does that for Yoga and FlexPanel.
- **The heavy/light split is a judgement** (file prefixes listed in Method). Moving `Component.cs` or the diagnostics files to "rewrite" would shrink both the seam and the edits.
- **The seam counts members, not effort.** `Reconciler.Mount` is one member and most of a reconciler.
- **Corrections after review (2026-11-22).** A first version of this report had three problems: stubs without their base types, error codes counted as seam members, and the two C# 15 files outside "needs edits" (since moot: with the .NET 11 SDK they link unmodified). The numbers above are re-measured with all three fixed.

## Update: .NET 11 SDK (2026-11-22)

With SDK 11.0.100 installed, `global.json` pins 11.0.100 and the spike compiles with `LangVersion 15` (still targeting `net10.0`), linking upstream's `Core/Polyfills/ClosedTypeAttributes.cs`. `Core/AsyncValue.cs` and `Core/InfiniteResource.cs` now link unmodified; the edited copies are gone. The step-3b total is unchanged (567). The tables above reflect this.
