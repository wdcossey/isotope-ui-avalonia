using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// Icons (#91): shapes (PathIcon) in sizes and colours, glyphs of a font (FontIcon), a bitmap as its shape in the
/// foreground or in its colours (BitmapIcon, ImageIcon), and one icon whose data changes kind, which mounts a new control.
/// </summary>
public static class IconsPage
{
    /// <summary>Shapes drawn for this page, as path markup in a 24 by 24 box.</summary>
    public const string Diamond = "M 12,2 L 22,12 L 12,22 L 2,12 Z",
        Check = "M 2,13 L 5,10 L 9,14 L 19,4 L 22,7 L 9,20 Z",
        Triangle = "M 0,0 L 24,0 L 12,20 Z";

    /// <summary>
    /// A ring drawn for this page (24 by 24, teal and orange), an avares resource of the assembly this page is built into
    /// (the app, or the tests that render it).
    /// </summary>
    public static readonly Uri Ring = new($"avares://{typeof(IconsPage).Assembly.GetName().Name}/Assets/ring.png");

    /// <summary>The kinds the last icon steps through, in order.</summary>
    public static readonly (string Name, IconData Data)[] Kinds =
    [
        ("a shape", PathIcon(Check)),
        ("a glyph", FontIcon("Aa", FontFamily.Default)),
        ("a bitmap", BitmapIcon(Ring)),
        ("an image", ImageIcon(Ring)),
    ];

    public static Element Render(RenderContext ctx)
    {
        var (kind, setKind) = ctx.UseState(0);
        var (green, setGreen) = ctx.UseState(false);
        var colour = green ? "SeaGreen" : "SteelBlue";
        return VStack(12,
            TextBlock("Shapes, sized with .Width and .Height and coloured with .Foreground (20 by 20 by default):"),
            SampleApp.Row(
                Icon(PathIcon(Diamond)),
                Icon(PathIcon(Check)).Width(32).Height(32).Foreground(colour),
                Icon(PathIcon(Triangle)).Width(48).Height(48).Foreground(Theme.Accent),
                Button("Change the colour", () => setGreen(!green))),
            TextBlock("Glyphs of a font: letters in the default font, and a star of Windows' Segoe icon fonts (Windows only):"),
            SampleApp.Row(
                Icon(FontIcon("Aa", FontFamily.Default, 24)),
                Icon(FontIcon("\uE734", fontSize: 24)).Foreground(colour)),
            TextBlock("A bitmap: its shape in the foreground, then its colours, then as an image:"),
            SampleApp.Row(
                Icon(BitmapIcon(Ring)).Width(32).Height(32).Foreground(colour),
                Icon(BitmapIcon(Ring, showAsMonochrome: false)).Width(32).Height(32),
                Icon(ImageIcon(Ring)).Width(48).Height(48)),
            TextBlock($"One icon showing {Kinds[kind].Name}: another kind mounts another control."),
            SampleApp.Row(
                Icon(Kinds[kind].Data).Width(32).Height(32).Foreground(colour),
                Button("Next kind", () => setKind((kind + 1) % Kinds.Length))))
            .Margin(12);
    }
}
