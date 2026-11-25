using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// Rich text (#65): upstream's rich text modifiers (src/Reactor/Elements/ElementExtensions.cs, "RichTextBlock sugar" and
// "Rich text paragraph / inline sugar"), with Avalonia's names and value types. An inline's FontSize, FontWeight,
// FontFamily, Foreground and Background are the element-wide modifiers, which set its members. Upstream's IsBold, IsItalic
// and IsStrikethrough are FontWeight, FontStyle and TextDecorations here, which .Bold(), .Italic() and .Strikethrough() set.

public static partial class ElementExtensions
{
    // ── Inlines ─────────────────────────────────────────────────────

    /// <summary>Bold (<see cref="Avalonia.Media.FontWeight.Bold"/>): <c>Run("now").Bold()</c> (upstream's <c>IsBold</c>).</summary>
    public static T Bold<T>(this T el) where T : InlineElement => el with { FontWeight = Avalonia.Media.FontWeight.Bold };

    /// <summary>Semibold (<see cref="Avalonia.Media.FontWeight.SemiBold"/>).</summary>
    public static T SemiBold<T>(this T el) where T : InlineElement => el with { FontWeight = Avalonia.Media.FontWeight.SemiBold };

    /// <summary>Italic (<see cref="Avalonia.Media.FontStyle.Italic"/>; upstream's <c>IsItalic</c>).</summary>
    public static T Italic<T>(this T el) where T : InlineElement => el with { FontStyle = Avalonia.Media.FontStyle.Italic };

    /// <summary>Underlined (Avalonia's <see cref="Avalonia.Media.TextDecorations.Underline"/>), with any decoration it has already.</summary>
    public static T Underline<T>(this T el) where T : InlineElement =>
        el with { TextDecorations = WithDecorations(DecorationsOf(el), Avalonia.Media.TextDecorations.Underline) };

    /// <summary>
    /// Struck through (Avalonia's <see cref="Avalonia.Media.TextDecorations.Strikethrough"/>; upstream's <c>IsStrikethrough</c>), with
    /// any decoration it has already: <c>.Underline().Strikethrough()</c> is both.
    /// </summary>
    public static T Strikethrough<T>(this T el) where T : InlineElement =>
        el with { TextDecorations = WithDecorations(DecorationsOf(el), Avalonia.Media.TextDecorations.Strikethrough) };

    // The decorations an inline shows: its member, else an Underline span's own underline (Avalonia's default for it).
    private static TextDecorationCollection? DecorationsOf(InlineElement el) =>
        el.TextDecorations ?? (el is UnderlineElement ? Avalonia.Media.TextDecorations.Underline : null);

    public static T FontStyle<T>(this T el, FontStyle style) where T : InlineElement => el with { FontStyle = style };

    public static T FontStretch<T>(this T el, FontStretch stretch) where T : InlineElement => el with { FontStretch = stretch };

    /// <summary>Extra space between characters, in DIPs (upstream's <c>CharacterSpacing</c> is in 1/1000 em).</summary>
    public static T LetterSpacing<T>(this T el, double spacing) where T : InlineElement => el with { LetterSpacing = spacing };

    // The decorations an inline has, and those of the added collection whose places it has none at yet.
    private static TextDecorationCollection WithDecorations(TextDecorationCollection? existing, TextDecorationCollection added)
    {
        if (existing is not { Count: > 0 }) return added;
        var merged = new TextDecorationCollection(existing);
        foreach (var decoration in added)
            if (!merged.Any(d => d.Location == decoration.Location)) merged.Add(decoration);
        return merged;
    }

    /// <summary>Decorations, replacing any it has: <c>.TextDecorations(Avalonia.Media.TextDecorations.Overline)</c>.</summary>
    public static T TextDecorations<T>(this T el, TextDecorationCollection decorations) where T : InlineElement =>
        el with { TextDecorations = decorations };

    public static T BaselineAlignment<T>(this T el, BaselineAlignment alignment) where T : InlineElement =>
        el with { BaselineAlignment = alignment };

    // ── SelectableTextBlockElement ──────────────────────────────────

    /// <summary>Bold text (<see cref="Avalonia.Media.FontWeight.Bold"/>).</summary>
    public static SelectableTextBlockElement Bold(this SelectableTextBlockElement el) => el with { FontWeight = Avalonia.Media.FontWeight.Bold };

    /// <summary>Semibold text (<see cref="Avalonia.Media.FontWeight.SemiBold"/>).</summary>
    public static SelectableTextBlockElement SemiBold(this SelectableTextBlockElement el) => el with { FontWeight = Avalonia.Media.FontWeight.SemiBold };

    public static SelectableTextBlockElement FontSize(this SelectableTextBlockElement el, double size) => el with { FontSize = size };

    public static SelectableTextBlockElement FontWeight(this SelectableTextBlockElement el, FontWeight weight) => el with { FontWeight = weight };

    public static SelectableTextBlockElement FontStyle(this SelectableTextBlockElement el, FontStyle style) => el with { FontStyle = style };

    public static SelectableTextBlockElement FontFamily(this SelectableTextBlockElement el, string family) =>
        el with { FontFamily = Avalonia.Media.FontFamily.Parse(family) };

    public static SelectableTextBlockElement FontFamily(this SelectableTextBlockElement el, FontFamily family) => el with { FontFamily = family };

    /// <summary>How the text wraps; with no argument, it wraps at the available width.</summary>
    public static SelectableTextBlockElement TextWrapping(this SelectableTextBlockElement el, TextWrapping wrapping = Avalonia.Media.TextWrapping.Wrap) =>
        el with { TextWrapping = wrapping };

    public static SelectableTextBlockElement TextAlignment(this SelectableTextBlockElement el, TextAlignment alignment) => el with { TextAlignment = alignment };

    public static SelectableTextBlockElement TextTrimming(this SelectableTextBlockElement el, TextTrimming trimming) => el with { TextTrimming = trimming };

    /// <summary>The line height, in DIPs (Avalonia's default is proportional to the font size).</summary>
    public static SelectableTextBlockElement LineHeight(this SelectableTextBlockElement el, double height) => el with { LineHeight = height };

    /// <summary>The most lines shown before the text is trimmed; 0 means no limit.</summary>
    public static SelectableTextBlockElement MaxLines(this SelectableTextBlockElement el, int maxLines) => el with { MaxLines = maxLines };

    /// <summary>The brush behind selected text (upstream's <c>SelectionHighlightColor</c>).</summary>
    public static SelectableTextBlockElement SelectionBrush(this SelectableTextBlockElement el, IBrush brush) => el with { SelectionBrush = brush };

    /// <summary>The brush of selected text.</summary>
    public static SelectableTextBlockElement SelectionForegroundBrush(this SelectableTextBlockElement el, IBrush brush) =>
        el with { SelectionForegroundBrush = brush };
}
