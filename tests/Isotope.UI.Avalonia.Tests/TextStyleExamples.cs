using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;
using AvaloniaTextBlock = Avalonia.Controls.TextBlock;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The named text styles' examples, for docs/guide/text-styles.md: each region is a snippet of the page (DocSnippetTests
/// checks they match) and TextStyleTests runs it in a headless window.
/// </summary>
internal static class TextStyleExamples
{
    #region text-styles-article
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
    #endregion

    #region text-styles-override
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
    #endregion
}
