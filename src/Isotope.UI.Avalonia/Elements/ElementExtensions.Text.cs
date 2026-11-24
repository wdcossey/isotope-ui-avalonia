using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3) of upstream's text modifiers (src/Reactor/Elements/ElementExtensions.cs, #42): upstream's
// names and overloads, Avalonia's value types. As upstream's, the element-wide ones (Foreground, FontSize, FontWeight,
// FontFamily) are modifiers that reach a text block or a templated control, and the TextBlockElement ones set the
// record's members and keep its type for chaining. Not here yet: IsTextSelectionEnabled (Avalonia's TextBlock has no
// selection; SelectableTextBlock is another control), CharacterSpacing and TextDecorations (their Avalonia counterparts,
// LetterSpacing in DIPs and a TextDecorationCollection, change shape), and Foreground(ThemeRef) (#41).
// On a record derived from TextBlockElement, FontSize and FontFamily bind to the element-wide overloads (an identity
// conversion beats the base-class one), so they set the modifier, which the members of the same name win over.

public static partial class ElementExtensions
{
    // ── Element-wide ────────────────────────────────────────────────

    /// <summary>The text colour from a colour string Avalonia parses (<c>"#FF0000"</c>, <c>"Red"</c>).</summary>
    public static T Foreground<T>(this T el, string color) where T : Element => el.Foreground(Brush.Parse(color));

    /// <summary>The text colour of a text block or a templated control; a null brush leaves the control's own.</summary>
    public static T Foreground<T>(this T el, IBrush? brush) where T : Element =>
        el.WithModifiers(m => m with { Foreground = brush });

    /// <summary>The font size of a text block or a templated control, in DIPs.</summary>
    public static T FontSize<T>(this T el, double size) where T : Element =>
        el.WithModifiers(m => m with { FontSize = size });

    /// <summary>The font weight of a text block or a templated control: <c>.FontWeight(FontWeight.SemiBold)</c>.</summary>
    public static T FontWeight<T>(this T el, FontWeight weight) where T : Element =>
        el.WithModifiers(m => m with { FontWeight = weight });

    /// <summary>The font family of a text block or a templated control, by name: <c>.FontFamily("Consolas")</c>.</summary>
    public static T FontFamily<T>(this T el, string family) where T : Element => el.FontFamily(Avalonia.Media.FontFamily.Parse(family));

    public static T FontFamily<T>(this T el, FontFamily family) where T : Element =>
        el.WithModifiers(m => m with { FontFamily = family });

    // ── TextBlockElement ────────────────────────────────────────────

    /// <summary>Bold text (<see cref="Avalonia.Media.FontWeight.Bold"/>).</summary>
    public static TextBlockElement Bold(this TextBlockElement el) => el with { FontWeight = Avalonia.Media.FontWeight.Bold };

    /// <summary>Semibold text (<see cref="Avalonia.Media.FontWeight.SemiBold"/>).</summary>
    public static TextBlockElement SemiBold(this TextBlockElement el) => el with { FontWeight = Avalonia.Media.FontWeight.SemiBold };

    public static TextBlockElement FontSize(this TextBlockElement el, double size) => el with { FontSize = size };

    // Not in upstream, which has only the element-wide FontWeight: here the member wins over the modifier on a text block,
    // so without it .Bold().FontWeight(FontWeight.Light) would stay bold.
    public static TextBlockElement FontWeight(this TextBlockElement el, FontWeight weight) => el with { FontWeight = weight };

    public static TextBlockElement FontStyle(this TextBlockElement el, FontStyle style) => el with { FontStyle = style };

    /// <summary>How the text wraps; with no argument, it wraps at the available width.</summary>
    public static TextBlockElement TextWrapping(this TextBlockElement el, TextWrapping wrapping = Avalonia.Media.TextWrapping.Wrap) =>
        el with { TextWrapping = wrapping };

    public static TextBlockElement TextAlignment(this TextBlockElement el, TextAlignment alignment) => el with { TextAlignment = alignment };

    /// <summary>How overflowing text is cut: <c>.TextTrimming(TextTrimming.CharacterEllipsis)</c>.</summary>
    public static TextBlockElement TextTrimming(this TextBlockElement el, TextTrimming trimming) => el with { TextTrimming = trimming };

    public static TextBlockElement FontFamily(this TextBlockElement el, string family) =>
        el with { FontFamily = Avalonia.Media.FontFamily.Parse(family) };

    public static TextBlockElement FontFamily(this TextBlockElement el, FontFamily family) => el with { FontFamily = family };

    /// <summary>The line height, in DIPs (Avalonia's default is proportional to the font size).</summary>
    public static TextBlockElement LineHeight(this TextBlockElement el, double height) => el with { LineHeight = height };

    /// <summary>The most lines shown before the text is trimmed (see <see cref="TextTrimming(TextBlockElement, Avalonia.Media.TextTrimming)"/>); 0 means no limit.</summary>
    public static TextBlockElement MaxLines(this TextBlockElement el, int maxLines) => el with { MaxLines = maxLines };
}
