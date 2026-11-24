using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>The migration of upstream's named text styles (#44): the factories and ApplyStyle keep their names and build.</summary>
public class TextStyleMigrationTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void Named_text_styles_and_Card_keep_their_names_and_build()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => Card(VStack(
                    Heading("Settings"),
                    SubHeading("Display"),
                    Title("t"), Subtitle("s"), Body("b"), BodyStrong("bs"), BodyLarge("bl"), TitleLarge("tl"), Display("d"),
                    Caption(content: "Updated today").Foreground(Theme.SecondaryText),
                    TextBlock("Note").ApplyStyle("CaptionTextBlockStyle")));
            }
            """;
        var fixedText = Migrate.Fix(reactor);
        // The named argument takes the Avalonia parameter name, as TextBlock's; the names themselves are kept.
        Assert.Contains("Caption(text: \"Updated today\")", fixedText);
        Assert.Contains("Heading(\"Settings\")", fixedText);
        Assert.Contains(".ApplyStyle(\"CaptionTextBlockStyle\")", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }
}
