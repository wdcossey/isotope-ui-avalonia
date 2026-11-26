using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// The icon factories (#91), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs: upstream's
// names, with Avalonia's value types. Upstream's SymbolIcon, Icon(Symbol) and Icon(string symbol) aren't here: WinUI's
// symbols are glyphs of the Segoe icon fonts, which Avalonia doesn't ship (the migration reports them).

public static partial class Factories
{
    /// <summary>
    /// An icon showing <paramref name="data"/>: <c>Icon(PathIcon("M 0,0 L 24,0 L 12,20 Z"))</c>,
    /// <c>Icon(FontIcon("\uE790", "avares://MyApp/Assets/Fonts#My Icons"))</c>, <c>Icon(ImageIcon(uri))</c>.
    /// </summary>
    public static IconElement Icon(IconData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data);
    }

    /// <summary>A vector shape from path markup, as an Avalonia <see cref="Avalonia.Controls.PathIcon"/>'s data.</summary>
    public static PathIconData PathIcon(string data) => new(data);

    /// <summary>
    /// A glyph of a font, in <paramref name="fontFamily"/> (WinUI's Segoe icon fonts when null) at
    /// <paramref name="fontSize"/> (20 when null, or the <c>.FontSize(...)</c> modifier's).
    /// </summary>
    public static FontIconData FontIcon(string glyph, FontFamily? fontFamily = null, double? fontSize = null) =>
        new(glyph, fontFamily, fontSize);

    /// <summary>
    /// A glyph of a font, by the font's name (upstream's form): null is the default, as with no font, where Avalonia's
    /// conversion of a null string to a font family would throw.
    /// </summary>
    public static FontIconData FontIcon(string glyph, string? fontFamily, double? fontSize = null) =>
        new(glyph, fontFamily, fontSize);

    /// <summary>A bitmap, by default only its shape in the icon's foreground (<paramref name="showAsMonochrome"/>).</summary>
    public static BitmapIconData BitmapIcon(Uri source, bool showAsMonochrome = true) => new(source, showAsMonochrome);

    /// <summary>An image in its own colours, as an Avalonia <see cref="Avalonia.Controls.Image"/>.</summary>
    public static ImageIconData ImageIcon(Uri source) => new(source);
}
