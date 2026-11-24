using Avalonia.Controls;
using Avalonia.Media;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Text styling (#42): upstream's TextBlock members and element-wide font modifiers, with Avalonia's names (#31;
// tools/naming/renames.json maps upstream's Weight to FontWeight) and Avalonia's value types.

// FontSize, FontWeight and FontFamily have a second source, the element-wide modifier of the same name: the reconciler
// writes the two as one value (Reconciler.ApplyTextModifiers), the element's first, so neither overwrites the other, and
// the descriptor leaves them alone.
[WrapManual("FontSize")]
[WrapManual("FontWeight")]
[WrapManual("FontFamily")]
public partial record TextBlockElement
{
    /// <summary>The font size, in DIPs; <see langword="null"/> leaves the control's own.</summary>
    /// <remarks>Also an element-wide modifier (<c>.FontSize</c> on any element): on a text block, this member wins.</remarks>
    public double? FontSize { get; init; }

    /// <summary>The font weight (upstream's <c>Weight</c>); <c>.Bold()</c> and <c>.SemiBold()</c> set it.</summary>
    /// <remarks>Also an element-wide modifier: on a text block, this member wins.</remarks>
    public FontWeight? FontWeight { get; init; }

    /// <summary>Normal, italic or oblique.</summary>
    public FontStyle? FontStyle { get; init; }

    /// <summary>The font family (<c>.FontFamily("Consolas")</c>).</summary>
    /// <remarks>Also an element-wide modifier: on a text block, this member wins.</remarks>
    public FontFamily? FontFamily { get; init; }

    public TextWrapping? TextWrapping { get; init; }

    public TextAlignment? TextAlignment { get; init; }

    /// <summary>How text that overflows is cut (Avalonia's <see cref="Avalonia.Media.TextTrimming"/>: <c>CharacterEllipsis</c>, <c>WordEllipsis</c>, …).</summary>
    public TextTrimming? TextTrimming { get; init; }

    /// <summary>The line height, in DIPs; <see langword="null"/> leaves Avalonia's (proportional to the font size).</summary>
    public double? LineHeight { get; init; }

    /// <summary>The most lines shown before the text is trimmed; 0 means no limit, and <see langword="null"/> leaves the
    /// control's own.</summary>
    /// <remarks>Upstream's is an <c>int</c>, written on every text block; nullable here, so a style's value isn't
    /// overwritten when none is given.</remarks>
    public int? MaxLines { get; init; }

    // The skip arm's comparison of the text styling members (Element.ShallowEquals).
    internal static bool TextStylingEquals(TextBlockElement x, TextBlockElement y)
        => x.FontSize == y.FontSize && x.FontWeight == y.FontWeight && x.FontStyle == y.FontStyle
            && Equals(x.FontFamily, y.FontFamily) && x.TextWrapping == y.TextWrapping && x.TextAlignment == y.TextAlignment
            && Equals(x.TextTrimming, y.TextTrimming) && x.LineHeight == y.LineHeight && x.MaxLines == y.MaxLines;

    private static partial ControlDescriptor<TextBlockElement, TextBlock> Customize(ControlDescriptor<TextBlockElement, TextBlock> d) => d;
}

public partial record ElementModifiers
{
    /// <summary>
    /// The text colour (Avalonia's <c>Foreground</c>), written on a text block or a templated control (a button, a text
    /// box, …), as upstream writes it on a WinUI Control or TextBlock.
    /// </summary>
    public IBrush? Foreground { get; init; }

    /// <summary>The font size on a text block or a templated control.</summary>
    public double? FontSize { get; init; }

    /// <summary>The font weight on a text block or a templated control.</summary>
    public FontWeight? FontWeight { get; init; }

    /// <summary>The font family on a text block or a templated control.</summary>
    public FontFamily? FontFamily { get; init; }
}
