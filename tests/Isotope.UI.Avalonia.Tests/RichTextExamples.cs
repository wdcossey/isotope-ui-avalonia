using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Rich text (#65), for docs/guide/writing-components.md (DocSnippetTests); RichTextTests runs it.</summary>
internal static class RichTextExamples
{
    #region rich-text
    public static Element ReleaseNote(RenderContext ctx)
    {
        var (likes, setLikes) = ctx.UseState(0);
        return VStack(
            TextBlock("Version 2 is ", Bold("out"), ". Liked ", Run($"{likes}").Foreground("SeaGreen"), " times ",
                InlineUIContainer(Button("Like", () => setLikes(likes + 1)))),
            SelectableTextBlock(
                Paragraph(Italic("New: "), "rich text, as runs, spans and line breaks."),
                Paragraph("Select this text to copy it.").FontSize(12)));
    }
    #endregion
}
