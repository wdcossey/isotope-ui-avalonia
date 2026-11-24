# Platform hooks

Hooks that read the platform a component runs on: its theme, its window and its displays. They keep
Microsoft.UI.Reactor's names, return Avalonia's types, and work wherever Avalonia runs (nothing here is Windows-only).
Each one is a method of `RenderContext`, called at the top of a component like any other hook.

Every code block on this page is a region of the test project, run by `PlatformHooksTests` in a headless window.

## How they behave

- **The first render is right.** The value is read during the render, not first in an effect.
- **They re-render only on a real change.** A hook subscribes to the platform after the render is applied, and when the
  platform signals it reads the value again: the component re-renders only if it differs from what it rendered.
  `UseBreakpoint` re-renders only when the width crosses the breakpoint, not on every resize.
- **They unsubscribe on unmount**, and follow the host: a `ReactorHostControl` moved to another window re-subscribes there.
  A host taken out of its window (or whose window closed) without being disposed watches nothing app-wide until it
  joins a window again, so the application's events don't keep it alive.
- **They work outside a window too.** With no window (a test host, or a host not yet in a window) each returns
  upstream's fallback, and it takes the same hook slots, so a component can move between the two.

## The hooks

| Hook | Returns | Re-renders when | Outside a window |
|---|---|---|---|
| `UseColorScheme()` | `ColorScheme.Light` or `Dark`: the host's theme variant (it inherits the window's and the app's) | the theme variant changes | the app's theme |
| `UseIsDarkTheme()` | `UseColorScheme() == ColorScheme.Dark` | as above | as above |
| `UseHighContrast()` | `true` when the platform asks for high contrast (`PlatformColorValues.ContrastPreference`) | the preference changes | the app's platform settings |
| `UseWindow()` | the Avalonia `Window` the host is in | the host moves to another window, or out of one | `null` |
| `UseWindowSize()`, `UseWindowSize(topLevel)` | the top level's client size in DIPs, `(Width, Height)` | it resizes | `(0, 0)` |
| `UseBreakpoint(minWidth)`, `UseBreakpoint(topLevel, minWidth)` | `true` when the width is at least `minWidth` | the width crosses `minWidth` | `false` |
| `UseWindowState()` | Avalonia's `WindowState` | the state changes | `Normal` |
| `UseIsActive()` | `true` while the window is active | it activates or deactivates | `true` |
| `UseWindowPosition()` | the window's position in DIPs, `(X, Y)` | it moves, or its scaling changes | `(0, 0)` |
| `UseDpi()` | 96 x the top level's `RenderScaling` | the scaling changes | 96 |
| `UseDisplays()` | the top level's screens, as `DisplayInfo` snapshots (`Id`, `IsPrimary`, `BoundsDip`, `WorkAreaDip`, `Dpi`) | a display changes | empty |
| `UseClosingGuard(canClose)` | nothing: while mounted, a `canClose` that returns `false` (or throws) cancels the window's close | | no-op |

The "window" is the host's top level; for a host in a popup (a flyout, a tooltip), the window that opened the popup.
On mobile and in the browser, where the top level is a single view and not a
`Window`, the size, breakpoint and DPI hooks still work, `UseWindow` returns `null`, and the state, activation,
position and closing hooks return their fallbacks.

## A layout that adapts

<!-- snippet: platform-adaptive -->
```csharp
public static Element Adaptive(RenderContext ctx)
{
    var wide = ctx.UseBreakpoint(640);   // re-renders when the window's width crosses 640
    var dark = ctx.UseIsDarkTheme();     // the window's (and the app's) theme variant
    var (width, height) = ctx.UseWindowSize();
    var summary = TextBlock($"{width:0} x {height:0}, {(dark ? "dark" : "light")}");
    return wide
        ? HStack(8, summary, TextBlock("Details beside"))
        : VStack(8, summary, TextBlock("Details below"));
}
```

## Keeping unsaved work

Guards stack: any guard that returns `false` cancels the close, and later guards aren't asked. The guard of the latest
render is the one asked, so it can read the render's state.

<!-- snippet: platform-closing-guard -->
```csharp
public static Element Editor(RenderContext ctx)
{
    var (draft, setDraft) = ctx.UseState("");
    var (saved, setSaved) = ctx.UseState("");
    // The window doesn't close while there are unsaved changes.
    ctx.UseClosingGuard(() => draft == saved);
    return VStack(
        TextBox(draft, setDraft),
        Button(draft == saved ? "Saved" : "Save", () => setSaved(draft)));
}
```

## Coming from Reactor

What differs from upstream's hooks:

- **Avalonia's types:** `UseWindow` returns Avalonia's `Window` (upstream: `ReactorWindow`), `UseWindowState` Avalonia's
  `WindowState` (the same four states), and the explicit overloads take a `TopLevel` (upstream: a WinUI `Window`).
  `DisplayInfo` keeps upstream's members with Avalonia's `Rect`; its `Id` is the display's platform handle.
- **The host's window, also in a `ReactorHostControl`.** Upstream's window hooks return their fallbacks in a
  `ReactorHostControl` (it has no `ReactorWindow`); here they read the window the host control is in.
- **The theme is the host's, not only the app's.** Upstream's `UseColorScheme` reads the application's theme and misses
  a window's own; here a window's `RequestedThemeVariant` counts too.
- **Outside a window**, `UseDpi` returns 96 (upstream: the system DPI) and `UseDisplays` is empty (upstream lists the
  displays without a window): Avalonia reaches screens and scaling through a top level.
- **`UseBreakpoint`** re-renders only when the width crosses the breakpoint (upstream: on every resize), and is
  `false` outside a window for any `minWidth` (upstream: `true` for a `minWidth` of 0 or less).
- **`UseWindow`** follows the host, so it takes hook slots like the others (upstream's is a plain read, with no slot).
- **`UseClosingGuard`** keeps one registration while mounted and asks the latest render's predicate (upstream
  re-registers when the predicate changes).
- **Class components** (`Component`) don't have these hooks as methods yet; function components do.

Not on Avalonia (yet):

- `UseReducedMotion` and `UseHighContrastScheme`: Avalonia 12 exposes neither a reduced-motion preference nor the high
  contrast scheme's name.
- `UseIntl`: upstream's `IntlAccessor` isn't vendored.
- `UseWindowAspectRatio`, `UseIsCovered`: no Avalonia API. `UseWindowDragMove`: Avalonia's `BeginMoveDrag` needs the
  pointer event, so it would take a different shape.
- `UseSystemBackButton` (Avalonia has `TopLevel.BackRequested`), `UseOpenWindow`, `UseTrayIcon` (Avalonia has
  `TrayIcon`) and the file pickers (Avalonia has `StorageProvider`): later issues.
