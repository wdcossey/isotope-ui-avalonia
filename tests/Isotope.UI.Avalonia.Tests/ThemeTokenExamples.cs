using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The theme tokens' examples, for docs/guide/theme-tokens.md: each region is a snippet of the page (DocSnippetTests
/// checks they match) and ThemeTokenTests runs it in a headless window.
/// </summary>
internal static class ThemeTokenExamples
{
    #region theme-card
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
    #endregion

    #region theme-resolve
    public static Element Swatch(RenderContext ctx)
    {
        // A brush read in the render: the theme hook re-renders the component when the theme changes.
        var dark = ctx.UseIsDarkTheme();
        IBrush? accent = ThemeRef.Resolve(Theme.Accent.ResourceKey, dark);
        return TextBlock(dark ? "Accent on dark" : "Accent on light").Background(accent);
    }
    #endregion
}
