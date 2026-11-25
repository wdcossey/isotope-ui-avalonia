using Avalonia.Media;

namespace Isotope.UI.Core;

// Rich text (#65): the inline family of Avalonia's text, as element records a text block's Inlines (TextBlockElement,
// SelectableTextBlockElement) holds. Upstream's are plain records (RichTextInline, RichTextRun, RichTextLineBreak,
// RichTextInlineUIContainer) inside a RichTextBlock's paragraphs; these are elements, so a key and the text styling
// modifiers apply to them, and each is named after its Avalonia inline (#46; tools/naming/renames.json maps Reactor's).
// The reconciler diffs each inline list as it does a panel's children (Reconciler.Inlines.cs): by key, else by position
// among the unkeyed, updating Avalonia's inlines in place. An inline is not a control: it goes in a text block's inlines
// or a span's, never in a panel or a content slot.

/// <summary>
/// The base of every inline (Avalonia's <see cref="Avalonia.Controls.Documents.Inline"/>; upstream's <c>RichTextInline</c>).
/// Its members are Avalonia's text properties: <see langword="null"/> leaves the value it inherits from its span or text
/// block. A string is a run: <c>TextBlock("Hello, ", Bold("world"))</c>.
/// </summary>
/// <remarks>
/// <see cref="FontSize"/>, <see cref="FontWeight"/>, <see cref="FontFamily"/>, <see cref="Foreground"/> and
/// <see cref="Background"/> are also element-wide modifiers (<c>.FontSize(...)</c>, <c>.Foreground(...)</c>, …), which on an
/// inline set these members.
/// </remarks>
public abstract record InlineElement : Element
{
    /// <summary>The font size, in DIPs.</summary>
    public double? FontSize { get; init; }

    /// <summary>The font weight; <c>.Bold()</c> sets it (upstream's <c>IsBold</c>).</summary>
    public FontWeight? FontWeight { get; init; }

    /// <summary>Normal, italic or oblique; <c>.Italic()</c> sets it (upstream's <c>IsItalic</c>).</summary>
    public FontStyle? FontStyle { get; init; }

    /// <summary>The font family (<c>.FontFamily("Consolas")</c>).</summary>
    public FontFamily? FontFamily { get; init; }

    public FontStretch? FontStretch { get; init; }

    /// <summary>The text colour.</summary>
    public IBrush? Foreground { get; init; }

    /// <summary>The brush behind the text.</summary>
    public IBrush? Background { get; init; }

    /// <summary>Extra space between characters, in DIPs (upstream's <c>CharacterSpacing</c> is in 1/1000 em).</summary>
    public double? LetterSpacing { get; init; }

    /// <summary>Underline, strikethrough…: <c>.Underline()</c> and <c>.Strikethrough()</c> set it.</summary>
    public TextDecorationCollection? TextDecorations { get; init; }

    /// <summary>Where the inline sits against the line's baseline.</summary>
    public BaselineAlignment? BaselineAlignment { get; init; }

    /// <summary>A string is a run of text (a null string is no inline, which the factories drop).</summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public static implicit operator InlineElement?(string? text) => text is null ? null : new RunElement(text);
}

/// <summary>A run of text (Avalonia's <see cref="Avalonia.Controls.Documents.Run"/>; upstream's <c>RichTextRun</c>).</summary>
public record RunElement(string Text) : InlineElement;

/// <summary>Inlines grouped, sharing the span's text properties (Avalonia's <see cref="Avalonia.Controls.Documents.Span"/>).</summary>
public record SpanElement(InlineElement[] Inlines) : InlineElement;

/// <summary>Bold inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Bold"/>, a span).</summary>
public record BoldElement(InlineElement[] Inlines) : SpanElement(Inlines);

/// <summary>Italic inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Italic"/>, a span).</summary>
public record ItalicElement(InlineElement[] Inlines) : SpanElement(Inlines);

/// <summary>Underlined inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Underline"/>, a span).</summary>
public record UnderlineElement(InlineElement[] Inlines) : SpanElement(Inlines);

/// <summary>
/// A paragraph of a selectable text block (upstream's <c>RichTextParagraph</c>, from <c>Paragraph(...)</c>). Avalonia's text
/// has no paragraphs (blocks), so it is a <see cref="Avalonia.Controls.Documents.Span"/> of its inlines. In
/// <see cref="SelectableTextBlockElement.Paragraphs"/> (<c>SelectableTextBlock(Paragraph(...), Paragraph(...))</c>) each is on a
/// line of its own, with a line break between paragraphs; among other inlines it is a span like any other. Upstream's
/// paragraph layout members (<c>Margin</c>, <c>TextIndent</c>, <c>TextAlignment</c>, <c>LineHeight</c>) have no counterpart on a
/// span.
/// </summary>
public record RichTextParagraph(InlineElement[] Inlines) : SpanElement(Inlines);

/// <summary>A line break (Avalonia's <see cref="Avalonia.Controls.Documents.LineBreak"/>; upstream's <c>RichTextLineBreak</c>).</summary>
public record LineBreakElement : InlineElement;

/// <summary>
/// An element in the flow of the text (Avalonia's <see cref="Avalonia.Controls.Documents.InlineUIContainer"/>; upstream's
/// <c>RichTextInlineUIContainer</c>, from <c>InlineUI(...)</c>). The child is reconciled as a child slot: updated in place
/// across renders, so a component inside keeps its state. Avalonia lays the text out again when any of its inlines
/// changes, and takes the child out of the visual tree and puts it back as it does, so the child keeps its state but not
/// its focus.
/// </summary>
public record InlineUIContainerElement(Element? Child = null) : InlineElement;
