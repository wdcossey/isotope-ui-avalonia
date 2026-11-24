using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>Text styling (#42): upstream's text modifiers bind on Isotope, and TextBlockElement's Weight is FontWeight.</summary>
public class TextStylingMigrationTests
{
    [Fact]
    public void Text_modifiers_bind_and_Weight_becomes_FontWeight()
    {
        const string reactor = """
            using Avalonia.Media;
            using Isotope.UI;
            using Isotope.UI.Core;
            using static Isotope.UI.Factories;

            static class C
            {
                public static Element[] Render() =>
                [
                    TextBlock("Title").FontSize(20).SemiBold().Foreground("#808080"),
                    TextBlock("Body").Bold().FontFamily("Consolas").TextWrapping().TextTrimming(TextTrimming.CharacterEllipsis),
                    TextBlock("Note") with { Weight = FontWeight.Light },
                    Button("Go", () => { }).Foreground("Red").FontSize(12).FontWeight(FontWeight.Bold),
                ];
            }
            """;
        Assert.Equal(["ISOMIG006: 'TextBlockElement.Weight' is 'FontWeight' in Isotope"], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("TextBlock(\"Note\") with { FontWeight = FontWeight.Light }", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }
}
