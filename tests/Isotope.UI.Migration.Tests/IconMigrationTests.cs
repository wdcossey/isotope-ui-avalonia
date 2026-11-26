using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>
/// The migration of Reactor's icons (#91): Icon and its path, font, bitmap and image data keep Reactor's names and build
/// as they are. WinUI's symbols aren't in Isotope: Symbol (ISOMIG005) and SymbolIcon (ISOMIG011) are reported with what
/// to use instead.
/// </summary>
public class IconMigrationTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void Reactor_s_icons_keep_their_names_and_build()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render(Action save, bool copied) =>
                [
                    Icon(PathIcon("M 0,0 L 24,0 L 12,20 Z")),
                    Icon(FontIcon("\uE790")),
                    Button(Icon(FontIcon(copied ? "\uE790" : "B", fontSize: 16)), save),
                    Icon(new FontIconData("A", "Consolas", 12)),
                    Icon(BitmapIcon(new Uri("avares://App/Assets/logo.png"), showAsMonochrome: false)),
                    Icon(ImageIcon(new Uri("avares://App/Assets/photo.png"))),
                    new IconElement(new PathIconData("M 0,0 L 1,1 Z")),
                ];

                public static IconData Pick(bool path) => path ? PathIcon("M 0,0 L 1,1 Z") : new ImageIconData(new Uri("avares://App/a.png"));
            }
            """;
        Assert.Empty(Migrate.Analyze(reactor));
        Assert.Empty(Migrate.Errors(reactor));
    }

    [Fact]
    public void Symbol_and_SymbolIcon_are_reported_with_what_to_use_instead()
    {
        const string reactor = Usings + """
            using Microsoft.UI.Xaml.Controls;

            static class C
            {
                public static Element Render() => Icon(Symbol.Home);

                public static IconData Add() => SymbolIcon("Add");
            }
            """;
        var left = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        Assert.Contains(left, d => d.StartsWith("ISOMIG005: 'Microsoft.UI.Xaml.Controls.Symbol' has no Avalonia counterpart", StringComparison.Ordinal)
            && d.Contains("Icon(FontIcon(glyph, fontFamily))", StringComparison.Ordinal));
        Assert.Contains(left, d => d.StartsWith("ISOMIG011: 'SymbolIcon' is a WinUI control with no Isotope counterpart", StringComparison.Ordinal)
            && d.Contains("Icon(PathIcon(...))", StringComparison.Ordinal));
        Assert.DoesNotContain(left, d => d.StartsWith("ISOMIG012", StringComparison.Ordinal));
    }
}
