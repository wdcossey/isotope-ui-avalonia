# Text styles

Reactor's named text styles, on Avalonia: the type-ramp factories (`Title`, `Body`, `Caption`…), `ApplyStyle` and
`Card`, with Reactor's names. Avalonia has no factories like these, so the names stay.

Every code block on this page is a region of the test project, run by `TextStyleTests` in a headless window.

## Using them

Each factory returns a text block, so the text modifiers chain on it:

<!-- snippet: text-styles-article -->
```csharp
public static Element Article(RenderContext ctx)
{
    var (likes, setLikes) = ctx.UseState(0);
    return Card(
        VStack(8,
            Title("Release notes"),
            Caption("Updated today").Foreground(Theme.SecondaryText),
            Body("Text styles follow WinUI's type ramp."),
            Button($"Like ({likes})", () => setLikes(likes + 1))));
}
```

| Factory | Style | Size (DIPs) | Weight |
|---|---|---|---|
| `Display` | `DisplayTextBlockStyle` | 68 | SemiBold |
| `TitleLarge` | `TitleLargeTextBlockStyle` | 40 | SemiBold |
| `Title` | `TitleTextBlockStyle` | 28 | SemiBold |
| `Subtitle` | `SubtitleTextBlockStyle` | 20 | SemiBold |
| `BodyLarge` | `BodyLargeTextBlockStyle` | 18 | Normal |
| `BodyStrong` | `BodyStrongTextBlockStyle` | 14 | SemiBold |
| `Body` | `BodyTextBlockStyle` | 14 | Normal |
| `Heading` | fixed values, automation heading level 1 | 28 | Bold |
| `SubHeading` | fixed values, automation heading level 2 | 20 | SemiBold |
| `Caption` | fixed values | 12 | Normal |

- **The type-ramp factories** are `TextBlock(text).ApplyStyle(key)`, with WinUI's style key. Their styles also wrap the
  text and trim it with an ellipsis, as WinUI's `BaseTextBlockStyle` does.
- **`Heading`, `SubHeading` and `Caption`** set the text block's own members, as upstream's do. They don't wrap.
  `Heading` and `SubHeading` set Avalonia's `AutomationProperties.HeadingLevel`, so screen readers announce them
  as headings.
- **The element wins.** A member or modifier set on the element wins over the style. In `Title("x").FontSize(20)` the
  size is 20 and the weight is still SemiBold.
- **`ApplyStyle(key)`** styles any text block from a key: WinUI's other TextBlock styles (`CaptionTextBlockStyle`,
  `BaseTextBlockStyle`, `HeaderTextBlockStyle`, `SubheaderTextBlockStyle`, `BodyLargeStrongTextBlockStyle`) or a
  key of the app's own. A key that changes between renders applies on the next render.
- **`Card(child)`** is a border with WinUI's card look: an 8 DIP corner radius, 16 DIP padding, and the
  `Theme.CardBackground` fill with a 1 DIP `Theme.CardStroke` border, which follow the theme
  ([theme tokens](theme-tokens.md)).

## Where the values come from

A style key names a `ControlTheme` (Avalonia's keyed style, the control's `Theme`). For a text block, it resolves in
this order:

1. **The resources the text block sees,** for its theme variant: its own, its ancestors', the window's, and the
   application's with its styles'. A `ControlTheme` there for a `TextBlock` wins.
2. **Isotope's theme for that WinUI style,** with WinUI's values (from WinUI's `generic.xaml`, Windows App SDK 1.8).
   Avalonia's Fluent theme defines none of these keys.

The key is watched. When the resources change, or the theme variant changes (a key defined in `ThemeDictionaries`),
the text block takes the new theme without a re-render. A key that resolves to nothing keeps the text block's default
look and warns once on the `Isotope-UI` event source, once the text block is in a logical tree (a window, or an open
popup). A resource of the key that isn't a `ControlTheme` for a `TextBlock` is ignored, and warns once too.

Avalonia's styles outrank a control theme. An app style for every `TextBlock` (a `Style` with the selector `TextBlock`
that sets `FontSize`) wins over the type ramp, where WinUI's keyed style would replace an implicit one. To change the
ramp, define a theme of its key instead.

An app replaces a style by defining a theme of its key:

<!-- snippet: text-styles-override -->
```csharp
public static void UseLargerTitles(Application app)
{
    // Every Title(...) in the app takes this theme instead of Isotope's.
    app.Resources["TitleTextBlockStyle"] = new ControlTheme(typeof(AvaloniaTextBlock))
    {
        Setters =
        {
            new Setter(AvaloniaTextBlock.FontSizeProperty, 32.0),
            new Setter(AvaloniaTextBlock.FontWeightProperty, FontWeight.Bold),
        },
    };
}
```

The app's theme replaces Isotope's whole, so the wrapping and trimming go too unless it sets them.
`AvaloniaTextBlock` is `using AvaloniaTextBlock = Avalonia.Controls.TextBlock;`: with `using static
Isotope.UI.Factories;`, a bare `TextBlock` names the factory.

## Coming from Reactor

- **A ControlTheme, not a WinUI Style.** An app overrides a style with an Avalonia `ControlTheme` for `TextBlock` under
  the same key. WinUI's font-size keys (`TitleTextBlockFontSize` and the like) aren't read.
- **Applied on every render.** Upstream applies `ApplyStyle` at mount only. Here a changed key applies on the next
  render, and a removed one clears the theme.
- **Text blocks only.** Upstream's `ApplyStyle` takes any element. Here it is on text blocks, so
  `Button(...).ApplyStyle("AccentButtonStyle")` doesn't compile: Avalonia's Fluent theme has no keyed button themes.
- **What WinUI's styles set that Avalonia lacks:** `LineStackingStrategy`, `TextLineBounds` and
  `OpticalMarginAlignment` have no Avalonia counterpart. WinUI's styles set the system's UI font (`XamlAutoFontFamily`);
  Isotope's set no font family, so a text block keeps the one it inherits (Avalonia's default, or the app's).
