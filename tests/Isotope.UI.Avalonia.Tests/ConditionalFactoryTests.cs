using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Upstream's conditional and list helpers (#43): When, If, Expr and ForEach, as values and mounted in a stack, where
/// ForEach's keys from IReactorKeyed items keep each row's control through a reorder.
/// </summary>
public class ConditionalFactoryTests
{
    private sealed record Todo(string Key, string Title) : IReactorKeyed;

    [Fact]
    public void When_renders_its_element_only_when_true_and_runs_it_only_then()
    {
        var runs = 0;
        Element Then() { runs++; return TextBlock("yes"); }
        Assert.Equal("yes", Assert.IsType<TextBlockElement>(When(true, Then)).Text);
        Assert.Same(EmptyElement.Instance, When(false, Then));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void If_renders_one_branch_and_nothing_without_an_otherwise()
    {
        Assert.Equal("a", Assert.IsType<TextBlockElement>(If(true, () => TextBlock("a"), () => TextBlock("b"))).Text);
        Assert.Equal("b", Assert.IsType<TextBlockElement>(If(false, () => TextBlock("a"), () => TextBlock("b"))).Text);
        Assert.Same(EmptyElement.Instance, If(false, () => TextBlock("a")));
        Assert.Throws<InvalidOperationException>(() => If(true, () => throw new InvalidOperationException(), () => TextBlock("b")));
    }

    [Fact]
    public void Expr_returns_its_element_or_nothing_for_null()
    {
        Assert.Equal("x", Assert.IsType<TextBlockElement>(Expr(() => TextBlock("x"))).Text);
        Assert.Same(EmptyElement.Instance, Expr(() => null));
        Assert.Throws<ArgumentNullException>(() => Expr(null!));
    }

    [Fact]
    public void ForEach_keys_elements_from_keyed_items_and_keeps_a_key_the_render_set()
    {
        Todo[] todos = [new("a", "First"), new("b", "Second")];
        var group = Assert.IsType<GroupElement>(ForEach(todos, t => TextBlock(t.Title)));
        Assert.Equal(["a", "b"], group.Children.Select(c => c.Key));
        Assert.Equal(["First", "Second"], group.Children.Select(c => ((TextBlockElement)c).Text));

        var own = Assert.IsType<GroupElement>(ForEach(todos, t => TextBlock(t.Title).WithKey("own-" + t.Key)));
        Assert.Equal(["own-a", "own-b"], own.Children.Select(c => c.Key));

        // Items that aren't keyed stay unkeyed; the index overload counts from 0, and a lazy sequence works.
        var indexed = Assert.IsType<GroupElement>(ForEach(new[] { "x", "y" }.Where(_ => true), (s, i) => TextBlock($"{i}:{s}")));
        Assert.Equal(["0:x", "1:y"], indexed.Children.Select(c => ((TextBlockElement)c).Text));
        Assert.All(indexed.Children, c => Assert.Null(c.Key));
        Assert.Equal(["a", "b"], ((GroupElement)ForEach(todos, (t, _) => TextBlock(t.Title))).Children.Select(c => c.Key));

        // As upstream's, keying follows the item type ForEach is given: over objects, keyed items stay unkeyed too.
        IEnumerable<object> mixed = [todos[0], "plain"];
        Assert.All(((GroupElement)ForEach(mixed, o => TextBlock(o.ToString()!))).Children, c => Assert.Null(c.Key));
    }

    [AvaloniaFact]
    public void Helpers_mount_in_a_stack_and_ForEach_rows_keep_their_controls_when_reordered()
    {
        Todo[] todos = [new("a", "First"), new("b", "Second"), new("c", "Third")];
        var showFooter = false;
        var host = new TestHost(() => VStack(
            ForEach(todos, t => TextBlock(t.Title)),
            When(showFooter, () => TextBlock("footer"))));
        Assert.Equal(["First", "Second", "Third"], host.Texts());
        var first = host.All<TextBlock>()[0];

        todos = [todos[2], todos[1], todos[0]];
        showFooter = true;
        host.Render();
        Assert.Equal(["Third", "Second", "First", "footer"], host.Texts());
        Assert.Same(first, host.All<TextBlock>()[2]); // "First" moved with its key
    }
}
