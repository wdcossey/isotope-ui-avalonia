using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>
/// The migration of Reactor's rich text (#65): RichTextBlock and InlineUI take Avalonia's names, the RichText* records the
/// names of Avalonia's inlines, and Paragraph and Run stay. IsBold and its kin are left for a hand fix, with the table's note.
/// </summary>
public class RichTextMigrationTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void Reactor_s_rich_text_takes_the_Avalonia_names_and_builds()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(Action like) => RichTextBlock(new[]
                {
                    Paragraph(Run("Press "), InlineUI(Button("me", like)), Run(" in the text.")),
                    Paragraph(Run("A second paragraph.")),
                }) with { SelectionHighlightColor = null };

                public static RichTextBlockElement Code(string line) =>
                    RichTextBlock("") with { Paragraphs = [new RichTextParagraph([new RichTextRun(line) { FontFamily = "Consolas" }])] };

                public static RichTextInline[] Blank() => new RichTextInline[] { Run(" "), new RichTextLineBreak() };

                public static RichTextInlineUIContainer Embedded(Element child) => InlineUI(child);
            }
            """;
        var before = Migrate.Analyze(reactor).Describe();
        Assert.Contains("ISOMIG009: Factory 'RichTextBlock' is 'SelectableTextBlock' in Isotope", before);
        Assert.Contains("ISOMIG009: Factory 'InlineUI' is 'InlineUIContainer' in Isotope", before);
        Assert.Contains("ISOMIG009: Type 'RichTextRun' is 'RunElement' in Isotope", before);
        Assert.DoesNotContain(before, d => d.StartsWith("ISOMIG012", StringComparison.Ordinal));

        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("SelectableTextBlock(new[]", fixedText);
        Assert.Contains("Paragraph(Run(\"Press \"), InlineUIContainer(Button(\"me\", like)), Run(\" in the text.\")),", fixedText);
        Assert.Contains("}) with { SelectionBrush = null };", fixedText);
        Assert.Contains("public static SelectableTextBlockElement Code(string line) =>", fixedText);
        Assert.Contains("SelectableTextBlock(\"\") with { Paragraphs = [new RichTextParagraph([new RunElement(line) { FontFamily = \"Consolas\" }])] };", fixedText);
        Assert.Contains("public static InlineElement[] Blank() => new InlineElement[] { Run(\" \"), new LineBreakElement() };", fixedText);
        Assert.Contains("public static InlineUIContainerElement Embedded(Element child) => InlineUIContainer(child);", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }

    [Fact]
    public void IsBold_IsItalic_IsStrikethrough_and_CharacterSpacing_are_left_for_a_hand_fix_with_the_table_s_note()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(bool isNew) => RichTextBlock(new[]
                {
                    Paragraph(Run("Bold") with { IsBold = true }, Run("italic") with { IsItalic = isNew }),
                    Paragraph(new RichTextRun("gone") { IsStrikethrough = true }, Run("wide").CharacterSpacing(50)),
                });
            }
            """;
        var left = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        Assert.Contains(left, d => d.StartsWith("ISOMIG007: 'RichTextRun.IsBold'", StringComparison.Ordinal) && d.Contains(".Bold()", StringComparison.Ordinal));
        Assert.Contains(left, d => d.StartsWith("ISOMIG007: 'RichTextRun.IsItalic'", StringComparison.Ordinal) && d.Contains(".Italic()", StringComparison.Ordinal));
        Assert.Contains(left, d => d.StartsWith("ISOMIG007: 'RichTextRun.IsStrikethrough'", StringComparison.Ordinal) && d.Contains(".Strikethrough()", StringComparison.Ordinal));
        // CharacterSpacing's unit isn't LetterSpacing's, so it is reported with the conversion, not renamed.
        Assert.Contains(left, d => d.StartsWith("ISOMIG010: Modifier 'CharacterSpacing'", StringComparison.Ordinal) && d.Contains(".LetterSpacing(", StringComparison.Ordinal));
    }

    [Fact]
    public void Hyperlink_is_still_reported_with_what_to_use_instead()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(Action go) => RichTextBlock(new[] { Paragraph(Run("See "), Hyperlink("this", go)) });
            }
            """;
        Assert.Contains(Migrate.Analyze(Migrate.Fix(reactor)).Describe(), d =>
            d.StartsWith("ISOMIG012: ", StringComparison.Ordinal) && d.Contains("InlineUIContainer(HyperlinkButton(...))", StringComparison.Ordinal));
    }
}
