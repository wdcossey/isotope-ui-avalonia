using Isotope.UI.Core;

namespace Isotope.UI;

// Rich text (#65): our own version (tier 3) of upstream's rich text factories (src/Reactor/Elements/Dsl.cs: RichTextBlock,
// Paragraph, Run, InlineUI), with Avalonia's names (#46): RichTextBlock is SelectableTextBlock and InlineUI is
// InlineUIContainer, and Avalonia's Span, Bold, Italic, Underline and LineBreak join them. Like a container's children, a
// null inline is dropped, so cond ? Run("x") : null works as one, and an array passed in is kept as it is: don't change it
// after passing it (a text block whose inlines array is the last render's is skipped).

public static partial class Factories
{
    // ── Rich text ───────────────────────────────────────────────────

    /// <summary>Text made of inlines: <c>TextBlock("Hello, ", Bold("world"), "!")</c> (a string is a run).</summary>
    public static TextBlockElement TextBlock(params InlineElement?[] inlines) => new("") { Inlines = Inlines(inlines) };

    /// <summary>
    /// Text the user can select and copy, as an Avalonia <see cref="Avalonia.Controls.SelectableTextBlock"/> (upstream's
    /// <c>RichTextBlock</c>).
    /// </summary>
    public static SelectableTextBlockElement SelectableTextBlock(string text) => new(text);

    /// <summary>Selectable text made of inlines: <c>SelectableTextBlock("Hello, ", Bold("world"))</c>.</summary>
    public static SelectableTextBlockElement SelectableTextBlock(params InlineElement?[] inlines) => new("") { Inlines = Inlines(inlines) };

    /// <summary>
    /// Selectable text made of paragraphs (upstream's <c>RichTextBlock(paragraphs)</c>), each on a line of its own:
    /// <c>SelectableTextBlock(Paragraph(Run("One")), Paragraph(Run("Two")))</c>.
    /// </summary>
    public static SelectableTextBlockElement SelectableTextBlock(params RichTextParagraph?[] paragraphs) =>
        new("") { Paragraphs = DropNulls(paragraphs) };

    /// <summary>
    /// A paragraph of a selectable text block (upstream's): a span of its inlines on a line of its own. Avalonia's text has
    /// no paragraphs, so this keeps Reactor's name.
    /// </summary>
    public static RichTextParagraph Paragraph(params InlineElement?[] inlines) => new(Inlines(inlines));

    /// <summary>A run of text (Avalonia's <see cref="Avalonia.Controls.Documents.Run"/>).</summary>
    public static RunElement Run(string text) => new(text);

    /// <summary>Inlines grouped under shared text properties (Avalonia's <see cref="Avalonia.Controls.Documents.Span"/>).</summary>
    public static SpanElement Span(params InlineElement?[] inlines) => new(Inlines(inlines));

    /// <summary>Bold inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Bold"/>): <c>Bold("important")</c>.</summary>
    public static BoldElement Bold(params InlineElement?[] inlines) => new(Inlines(inlines));

    /// <summary>Italic inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Italic"/>).</summary>
    public static ItalicElement Italic(params InlineElement?[] inlines) => new(Inlines(inlines));

    /// <summary>Underlined inlines (Avalonia's <see cref="Avalonia.Controls.Documents.Underline"/>).</summary>
    public static UnderlineElement Underline(params InlineElement?[] inlines) => new(Inlines(inlines));

    /// <summary>A line break (Avalonia's <see cref="Avalonia.Controls.Documents.LineBreak"/>).</summary>
    public static LineBreakElement LineBreak() => new();

    /// <summary>
    /// An element in the flow of the text (Avalonia's <see cref="Avalonia.Controls.Documents.InlineUIContainer"/>; upstream's
    /// <c>InlineUI</c>): <c>TextBlock("Volume ", InlineUIContainer(Slider(volume, 0, 100, setVolume)))</c>. The child keeps
    /// its state across renders.
    /// </summary>
    public static InlineUIContainerElement InlineUIContainer(Element child) => new(child);

    private static InlineElement[] Inlines(InlineElement?[] inlines) => DropNulls(inlines);

    // As FilterChildren: the caller's array as is when nothing is dropped (inline lists are diffed element by element).
    private static T[] DropNulls<T>(T?[] items) where T : class =>
        (T[])(object)(Array.IndexOf(items, null) < 0 ? items : Array.FindAll(items, i => i is not null));
}
