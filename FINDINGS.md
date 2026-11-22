# Reactor -> Avalonia feasibility findings (2026-11-22)

Goal: research project. Assess feasibility of an Avalonia (v12.x) fork/port of microsoft/microsoft-ui-reactor (MIT, WinUI 3 only).
Upstream clone (read-only reference, shallow): <repo root>\upstream
Method: static read/grep only. Nothing built or prototyped. Numbers are crude (grep for WinUI namespaces).

## Verdict
- Not supported out of the box; cannot be done by just extending the library with a handler package.
- Feasible as a port/fork: ~26k of 107k lines in src/Reactor are WinUI-free; ~81k lines (174 of 344 files) touch WinUI.

## Measurements (src/Reactor)
- Portable: hooks, context, state, AsyncValue, Command, Callbacks, child diff hints/slots, validation stack, Yoga flexbox (6k lines; only Rect/Size types to swap), much pure logic in Data/Animation.
- WinUI-bound: Reconciler.* mount/update, V1 handler protocol, pooling, theming/resource lookup, accessibility, Hosting (18k lines), navigation, drag/drop, flyouts.
- Other projects: Reactor.Advanced 44k lines (Win2D/WinAppSDK), Devtools 9.6k, Analyzers 22k, Cli 11k, Wrappers.Generator 3.5k.

## Why it can't just be extended
1. WinUI types in public contract: IElementHandler<TElement,TControl> has 'where TControl : UIElement'; internal IV1HandlerEntry and Reconciler.Mount() return UIElement. Element.cs uses WinUI Orientation/Brush/HorizontalAlignment/FontWeight/Thickness/CornerRadius/FrameworkElement 200+ times.
2. Project is net10.0-windows10.0.22621.0 with UseWinUI=true (even portable files compile inside it). LangVersion 15, .NET 11 SDK pinned in global.json.
3. Advanced/Devtools depend on Win2D / WinAppSDK.

## What helps
- Handler protocol (src/Reactor/Core/V1Protocol: IElementHandler, ControlRegistry, Reg*) is the right seam: immutable Element records vs. per-control mount/update handlers.
- Avalonia 12 targets net10.0 (same as Reactor), compiled bindings by default, 12.1 added TableView. Avalonia has no hooks/function components, so Reactor adds something new. Community prior art: Avalonia.Markup.Declarative.
- Upstream has its own docs/research/compare/avalonia.md.
- MIT licence.

## Friction points
- Property model: DP -> StyledProperty/DirectProperty; ReactorAttached tagging, change-echo suppression, pooled-control reset need rethinking.
- Control gaps: NavigationView, TabView, ItemsView, ItemsRepeater, InfoBar have no 1:1 Avalonia equivalents (SplitView, TabControl...); virtualization/TreeView handlers need Avalonia versions.
- Styling/theming: Avalonia selectors + ControlTheme vs ResourceDictionary/ThemeResource.
- Dispatcher: DispatcherQueue -> Dispatcher.UIThread (easy, touches render scheduling).
- Hosting: window/titlebar/backdrop/MSIX is Windows-specific.
- Layout: Yoga is custom, probably fits Avalonia Measure/Arrange; integration unchecked.
- Tests: Reactor.Tests (13k+ xUnit, headless) most reusable; SelfTests/AppTests WinUI-bound.

## Proposed next step (not started)
Vertical slice: new plain-net10.0 core project with the portable files; define an IHost/IControlAdapter seam + minimal Avalonia reconciler; implement UseState, Button, TextBlock, StackPanel, TextBox as a counter demo. Shows whether hooks/diff layer lifts cleanly and where the abstraction leaks.

## Open questions for the user
- API shape: Avalonia-native vocabulary (Avalonia.Media.Brush, Thickness) vs WinUI-style names for source compatibility?
- Build the slice, or first produce a per-control mapping table (~60 elements)?

## Sources
- https://docs.avaloniaui.net/docs/avalonia12-breaking-changes
- https://avaloniaui.net/blog/avalonia-12
- https://avaloniaui.net/blog/release-12-1
- https://github.com/microsoft/microsoft-ui-reactor
