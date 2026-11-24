# Theme tokens

Reactor's `Theme` tokens, on Avalonia: semantic brushes such as `Theme.PrimaryText`, `Theme.CardBackground` and
`Theme.Accent`, with Reactor's names. They follow the light and dark themes and high contrast, without a re-render.

Every code block on this page is a region of the test project, run by `ThemeTokenTests` in a headless window.

## Using them

A token goes where a brush would, through the `ThemeRef` overloads of `Background`, `Foreground`, `BorderBrush`,
`WithBorder` and a split view's `PaneBackground`:

<!-- snippet: theme-card -->
```csharp
public static Element Card(RenderContext ctx)
{
    var (count, setCount) = ctx.UseState(0);
    return Border(
            VStack(8,
                TextBlock("Theme tokens").Foreground(Theme.PrimaryText),
                TextBlock($"Clicked {count} times").Foreground(Theme.SecondaryText),
                Button("Click", () => setCount(count + 1))))
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke);
}
```

- `Theme` holds upstream's 36 tokens: text (`PrimaryText`, `SecondaryText`, `TertiaryText`, `DisabledText`,
  `AccentText`), accent fills, surfaces (`SolidBackground`, `CardBackground`, `LayerFill`, `SubtleFill`, `SmokeFill`),
  control fills, strokes (`CardStroke`, `SurfaceStroke`, `DividerStroke`, `ControlStroke`…) and the signal colours
  (`SystemSuccess`, `SystemCaution`, `SystemCritical`… and their backgrounds).
- `Theme.Ref("TextOnAccentFillColorPrimaryBrush")` names any other key.
- With `using static Isotope.UI.Core.Theme;`, the tokens are bare names: `.Foreground(SecondaryText)`.

## Where the values come from

A token is a resource key (`Theme.PrimaryText` is `TextFillColorPrimaryBrush`, WinUI's name). For a control it
resolves, in this order:

1. **The resources the control sees,** for its theme variant: its own, its ancestors', the window's, the
   application's and its styles'. An app overrides a token by defining its key there, for one theme or both.
2. **Isotope's built-in values,** which are WinUI's Fluent values for light and dark. Avalonia's Fluent theme doesn't
   define these keys.
   - The accent tokens take the Fluent theme's `SystemAccentColor` shades, which follow the platform's accent colour.
   - In high contrast (the platform's contrast preference, as `UseHighContrast` reads it), the built-in values are
     Windows' High Contrast Black or White colours, for a dark or light theme.

A bound token is resolved again when the control's theme variant changes, when the resources it sees change, and
when the contrast preference changes. No component re-renders. A brush the element sets itself, such as
`.Background(Brushes.Red)` or a border's `BorderBrush`, wins over a token, as a local value does in Avalonia.

## Reading a token in a render

`ThemeRef.Resolve(key, isDark)` returns the brush (Avalonia's `IBrush`) from the application's resources and the
built-in values. Pair it with `UseIsDarkTheme()`, which re-renders the component when the theme changes:

<!-- snippet: theme-resolve -->
```csharp
public static Element Swatch(RenderContext ctx)
{
    // A brush read in the render: the theme hook re-renders the component when the theme changes.
    var dark = ctx.UseIsDarkTheme();
    IBrush? accent = ThemeRef.Resolve(Theme.Accent.ResourceKey, dark);
    return TextBlock(dark ? "Accent on dark" : "Accent on light").Background(accent);
}
```

After editing the application's resources in a way that isn't a theme change, call `Theme.NotifyResourcesChanged()`:
every live host renders its whole tree once, past memoized components, as upstream's does.

## Other theme resources

`ThemeResource` (upstream's helper) reads a resource by key from the application's resources and its styles, for the
app's theme: `ThemeResource.CornerRadius("ControlCornerRadius")` and `ThemeResource.CornerRadius("OverlayCornerRadius")`
are the Fluent theme's radii for controls and for flyouts and cards. `ThemeResource.Brush(key)` falls back to the
built-in tokens, as a `ThemeRef` does. `Double`, `Thickness` and `Get<T>` read other values. Each returns Avalonia's
type, and throws `KeyNotFoundException` for a missing key (`Get<T>` returns its default instead). Unlike a bound
token, a value read this way doesn't follow a theme change on its own.

## Coming from Reactor

- **The values.** WinUI resolves the keys from its own theme resources; here they come from the app's Avalonia
  resources, then from Isotope's copy of WinUI's Fluent values. The look matches WinUI's, not Avalonia Fluent's own
  control colours.
- **High contrast** uses Windows' default high-contrast colours: Avalonia exposes the contrast preference, not the
  user's high-contrast palette.
- **Style priority.** Upstream applies a token as a synthesized `{ThemeResource}` style; here it is a binding at
  Avalonia's style priority. In both, a brush set on the element wins.
- **Borders on panels.** Avalonia's panels (`VStack`, `Grid`…) have no border, so a stack, grid or wrap panel given
  `WithBorder` or `BorderBrush` is mounted inside a `Border` that takes them (#49, [Writing components](writing-components.md)).
  A property a control doesn't have, or whose type isn't a brush, is skipped, as is `Foreground` on a `Border`.
- **Not ported yet:** per-control resource overrides (`.Resources(r => r.Set(key, themeRef))`), and the type-ramp
  factories (`Title`, `Body`, `Caption`…, #44). Upstream's `Theme` has no spacing or corner-radius tokens: its corner
  radii are `ThemeResource.CornerRadius(...)`, above.
