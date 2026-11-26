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

                public static object Native() => new Microsoft.UI.Xaml.Controls.SymbolIcon();

                public static Element ByName() => Icon("Home");
            }
            """;
        var left = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        // One note for all four: the Segoe glyph on Windows, or the app's own shape, and the guide's section.
        const string Note = "WinUI's symbols are glyphs of Windows' Segoe fonts, which Isotope doesn't ship: on Windows use " +
            "Icon(FontIcon(glyph, \"Segoe Fluent Icons\")) with the symbol's code point, elsewhere the app's own Icon(PathIcon(...)) " +
            "(\"WinUI symbols\" in docs/guide/writing-components.md).";
        Assert.Contains("ISOMIG005: 'Microsoft.UI.Xaml.Controls.Symbol' has no Avalonia counterpart: " + Note, left);
        Assert.Contains("ISOMIG011: 'SymbolIcon' is a WinUI control with no Isotope counterpart: " + Note, left);
        Assert.Contains("ISOMIG005: 'Microsoft.UI.Xaml.Controls.SymbolIcon' has no Avalonia counterpart: " + Note, left);
        Assert.Contains("ISOMIG010: Factory 'Icon(string symbol)' is gone in Isotope: " + Note, left);
        Assert.DoesNotContain(left, d => d.StartsWith("ISOMIG012", StringComparison.Ordinal) || d.StartsWith("ISOMIG016", StringComparison.Ordinal));
    }
}
