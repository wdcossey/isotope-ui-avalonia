using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>Layout modifiers (#49): upstream's layout modifiers bind on Isotope, with nothing to migrate.</summary>
public class LayoutMigrationTests
{
    [Fact]
    public void Layout_modifiers_bind()
    {
        const string reactor = """
            using Avalonia.Layout;
            using Isotope.UI;
            using Isotope.UI.Core;
            using static Isotope.UI.Factories;

            static class C
            {
                public static Element[] Render() =>
                [
                    VStack(TextBlock("a").Padding(8).Center()).Padding(12, 8).WithBorder("#808080").CornerRadius(8).Spacing(4),
                    Border(TextBlock("b")).Padding(top: 4).WithBorder("Gray", 2).CornerRadius(4, 4, 0, 0),
                    Button("Go", () => { }).MinWidth(80).MaxWidth(200).CornerRadius(4).Padding(4)
                        .HorizontalContentAlignment(HorizontalAlignment.Left),
                    TextBox("x").MinHeight(32).BorderThickness(0, 0, 0, 1).BorderBrush("Red").IsVisible(true),
                    FlexRow(TextBlock("c")).FlexPadding(8, 4).MaxHeight(100).MinHeight(20),
                ];
            }
            """;
        Assert.Empty(Migrate.Analyze(reactor).Describe());
        Assert.Empty(Migrate.Errors(reactor));
    }
}
