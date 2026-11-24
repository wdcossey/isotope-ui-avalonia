using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>The migration of the controls #43 added: Reactor's names become the Avalonia names the table marks applied.</summary>
public class NextControlsMigrationTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void ScrollView_becomes_ScrollViewer_and_its_content_orientation_is_reported()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => ScrollView(child: TextBlock("x"));
                public static Element Sideways() => ScrollView(TextBlock("y")) with { ContentOrientation = default };
            }
            """;
        var before = Migrate.Analyze(reactor).Describe();
        Assert.Contains("ISOMIG009: Factory 'ScrollView' is 'ScrollViewer' in Isotope", before);

        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("ScrollViewer(content: TextBlock(\"x\"))", fixedText);
        Assert.Contains("public static Element Sideways() => ScrollViewer(TextBlock(\"y\")) with { ContentOrientation = default };", fixedText);
        // Left for a hand fix, with the table's note: the orientation is the scroll bar visibilities.
        Assert.Contains(Migrate.Analyze(fixedText).Describe(), d =>
            d.StartsWith("ISOMIG007: 'ScrollViewerElement.ContentOrientation'", StringComparison.Ordinal) && d.Contains("Disabled horizontal scroll bar", StringComparison.Ordinal));
    }
}
