using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

/// <summary>
/// Text the user can select and copy, with inlines (Avalonia's <see cref="Avalonia.Controls.SelectableTextBlock"/>;
/// upstream's <c>RichTextBlockElement</c>, #65). Its content is, first that is set: <see cref="Paragraphs"/> (upstream's),
/// <see cref="Inlines"/> (Avalonia's), else <see cref="Text"/>.
/// </summary>
/// <remarks>
/// Upstream's members that WinUI's RichTextBlock has and Avalonia's SelectableTextBlock lacks (<c>IsTextSelectionEnabled</c>:
/// it is always selectable; <c>TextIndent</c>, <c>LineStackingStrategy</c>, <c>OpticalMarginAlignment</c>, …) are not here.
/// </remarks>
[GenerateReactorDescriptor(typeof(SelectableTextBlock), ClearValueOnUnset = true)]
[WrapManual("Text")]
[WrapManual("Inlines")]
[WrapManual("Paragraphs")]
[WrapManual("FontSize")]
[WrapManual("FontWeight")]
[WrapManual("FontFamily")]
public partial record SelectableTextBlockElement(string Text) : Element
{
    /// <summary>The text as inlines (Avalonia's <c>Inlines</c>): runs, spans, line breaks and inline elements.</summary>
    public InlineElement[]? Inlines { get; init; }

    /// <summary>
    /// The text as paragraphs (upstream's member, from <c>SelectableTextBlock(Paragraph(...), ...)</c>), each on a line of its
    /// own. Avalonia's text has no paragraphs: each is a span of its inlines, with a line break between them. When set, it is
    /// the text, and <see cref="Inlines"/> and <see cref="Text"/> are not shown.
    /// </summary>
    public RichTextParagraph[]? Paragraphs { get; init; }

    /// <summary>The font size, in DIPs; <see langword="null"/> leaves the control's own.</summary>
    /// <remarks>Also an element-wide modifier: on a selectable text block, this member wins.</remarks>
    public double? FontSize { get; init; }

    /// <summary>The font weight.</summary>
    /// <remarks>Also an element-wide modifier: on a selectable text block, this member wins.</remarks>
    public FontWeight? FontWeight { get; init; }

    public FontStyle? FontStyle { get; init; }

    /// <summary>The font family.</summary>
    /// <remarks>Also an element-wide modifier: on a selectable text block, this member wins.</remarks>
    public FontFamily? FontFamily { get; init; }

    public TextWrapping? TextWrapping { get; init; }

    public TextAlignment? TextAlignment { get; init; }

    public TextTrimming? TextTrimming { get; init; }

    /// <summary>The line height, in DIPs; <see langword="null"/> leaves Avalonia's (proportional to the font size).</summary>
    public double? LineHeight { get; init; }

    /// <summary>The most lines shown before the text is trimmed; 0 means no limit.</summary>
    public int? MaxLines { get; init; }

    /// <summary>The brush behind selected text (upstream's <c>SelectionHighlightColor</c>).</summary>
    public IBrush? SelectionBrush { get; init; }

    /// <summary>The brush of selected text.</summary>
    public IBrush? SelectionForegroundBrush { get; init; }

    internal Action<SelectableTextBlock>[] Setters { get; init; } = [];

    // The inlines the control shows: the paragraphs with a line break between each two, else the inlines, else none (the
    // text). The same paragraphs array always gives the same inlines array.
    internal InlineElement[]? ContentInlines => Paragraphs is { } paragraphs ? Interleave(paragraphs) : Inlines;

    private static readonly LineBreakElement s_paragraphBreak = new();
    private static readonly ConditionalWeakTable<RichTextParagraph[], InlineElement[]> s_interleaved = new();

    private static InlineElement[] Interleave(RichTextParagraph[] paragraphs) =>
        s_interleaved.GetValue(paragraphs, static ps =>
        {
            var inlines = new List<InlineElement>(Math.Max(0, ps.Length * 2 - 1));
            foreach (var p in ps)
            {
                if (p is null) continue;
                if (inlines.Count > 0) inlines.Add(s_paragraphBreak);
                inlines.Add(p);
            }
            return inlines.ToArray();
        });

    // The text, inlines or paragraphs, through the reconciler's inline diff (Reconciler.Inlines.cs). FontSize, FontWeight and
    // FontFamily have a second source, the element-wide modifier of the same name: the reconciler writes the two as one
    // value, this record's first (Reconciler.ApplyTextModifiers), as on a text block.
    private static partial ControlDescriptor<SelectableTextBlockElement, SelectableTextBlock> Customize(
        ControlDescriptor<SelectableTextBlockElement, SelectableTextBlock> d)
        => d.ImperativeBridged(
            mount:  static (ctx, c, e) => ctx.Reconciler.MountTextContent(c, e.Text, e.ContentInlines, ctx.RequestRerender),
            update: static (ctx, c, o, n) => ctx.Reconciler.UpdateTextContent(c, o.Text, o.ContentInlines, n.Text, n.ContentInlines, ctx.RequestRerender),
            unmount: static (ctx, c) => ctx.Reconciler.UnmountTextContent(c));
}
