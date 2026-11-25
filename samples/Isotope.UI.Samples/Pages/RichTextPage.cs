using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// Rich text (#65): a text block made of styled inlines, an element in the flow of the text, keyed inlines that move when
/// reordered, and selectable paragraphs (Reactor's RichTextBlock).
/// </summary>
public static class RichTextPage
{
    /// <summary>The coloured words, in their first order.</summary>
    public static readonly string[] Colours = ["Red", "Green", "Blue"];

    public static Element Render(RenderContext ctx)
    {
        var (likes, setLikes) = ctx.UseState(0);
        var (order, setOrder) = ctx.UseState(Colours);
        return VStack(12,
            TextBlock("Rich text is ", Bold("bold"), ", ", Italic("italic"), ", ", Underline("underlined"), ", ",
                Run("struck through").Strikethrough(), " and ", Run("coloured").Foreground("SeaGreen"), ", in one text block.")
                .TextWrapping(),
            TextBlock("An element in the text: ", InlineUIContainer(Button("Like", () => setLikes(likes + 1))), $" liked {likes} times."),
            // Keyed runs: a rotation moves each run, it doesn't rewrite them.
            TextBlock(order.Select(c => (InlineElement?)Run(c + " ").Foreground(c).Bold().WithKey(c)).ToArray()),
            Button("Rotate the colours", () => setOrder([.. order[1..], order[0]])),
            SelectableTextBlock(
                Paragraph(Bold("Selectable text. "), "Drag over these lines to select them; Ctrl+C copies them."),
                Paragraph("Each paragraph is on a line of its own.").FontSize(12))
                .TextWrapping())
            .Margin(12);
    }
}
