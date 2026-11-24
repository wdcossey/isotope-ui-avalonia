using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>The migration of the controls #43 added: Reactor's names become the Avalonia names the table marks applied.</summary>
public class NextControlsMigrationTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    // What ScrollView becomes: a ScrollViewer whose content is as wide as the view, as WinUI ScrollView's.
    const string Disabled = " with { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled })";

    [Fact]
    public void ScrollView_becomes_a_ScrollViewer_with_its_layout_and_its_type_is_renamed()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => ScrollView(child: TextBlock("x"));
                public static Element Nested() => ScrollView(ScrollView(TextBlock("z"))).Margin(4);
                public static ScrollViewElement Qualified() => Factories.ScrollView(TextBlock("f"));
            }
            """;
        var before = Migrate.Analyze(reactor).Describe();
        Assert.Contains("ISOMIG009: Factory 'ScrollView' is 'ScrollViewer' in Isotope", before);
        Assert.Contains("ISOMIG009: Type 'ScrollViewElement' is 'ScrollViewerElement' in Isotope", before);

        var fixedText = Migrate.Fix(reactor);
        // Written in full, with no using: Avalonia.Controls.Primitives would make Reactor's Popup and UniformGrid name its types.
        Assert.DoesNotContain("using Avalonia.Controls.Primitives;", fixedText);
        Assert.Contains("Render() => (ScrollViewer(content: TextBlock(\"x\"))" + Disabled + ";", fixedText);
        Assert.Contains("Nested() => (ScrollViewer((ScrollViewer(TextBlock(\"z\"))" + Disabled + ")" + Disabled + ".Margin(4);", fixedText);
        Assert.Contains("ScrollViewerElement Qualified() => (Factories.ScrollViewer(TextBlock(\"f\"))" + Disabled + ";", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }

    [Fact]
    public void ScrollView_s_content_orientation_is_left_for_a_hand_fix_with_the_table_s_note()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Sideways() => ScrollView(TextBlock("y")) with { ContentOrientation = default };
            }
            """;
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("Sideways() => (ScrollViewer(TextBlock(\"y\"))" + Disabled + " with { ContentOrientation = default };", fixedText);
        // ScrollViewerElement's own member entries and those of the record it replaced (ScrollViewElement) both apply.
        Assert.Contains(Migrate.Analyze(fixedText).Describe(), d =>
            d.StartsWith("ISOMIG007: 'ScrollViewElement.ContentOrientation'", StringComparison.Ordinal) && d.Contains("Disabled horizontal scroll bar", StringComparison.Ordinal));
        Assert.Contains("ISOMIG006: 'ScrollViewerElement.Child' is 'Content' in Isotope",
            Migrate.Analyze(Usings + "static class D { static object? C(ScrollViewerElement s) => s.Child; }").Describe());
    }

    [Fact]
    public void A_Reactor_tree_view_takes_the_Avalonia_names_and_builds()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(Action<string> open) =>
                    TreeView(TreeNode("Documents", TreeNode("Report.docx")), TreeNode(content: "Music"))
                        with { OnItemInvoked = node => open(node.Content), OnExpanding = node => open(node.Children?[0].Content ?? "") };

                public static TreeViewNodeData Leaf(string name) => new(Content: name) { IsExpanded = true };
            }
            """;
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("TreeView(TreeViewItem(\"Documents\", TreeViewItem(\"Report.docx\")), TreeViewItem(header: \"Music\"))", fixedText);
        Assert.Contains("OnItemInvoked = node => open(node.Header), OnExpanding = node => open(node.Items?[0].Header ?? \"\")", fixedText);
        Assert.Contains("public static TreeViewItemData Leaf(string name) => new(Header: name) { IsExpanded = true };", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }
}
