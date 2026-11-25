using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Wrappers;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A panel record as a library writes one: the generator's Panel children strategy, and children that the record
/// doesn't flatten itself (Isotope's own panel records do, with Reconciler.Flatten).
/// </summary>
[GenerateReactorDescriptor(typeof(StackPanel))]
public partial record GeneratedStackElement(Element[] Children) : Element
{
    internal Action<StackPanel>[] Setters { get; init; } = [];
}

/// <summary>
/// Groups inside a generated panel (#79): the generated strategy flattens them (PanelChildren.Of), so a group's
/// children mount into the panel as its siblings, as in Isotope's own panels. Upstream's mount path would mount the
/// group itself, which throws.
/// </summary>
public sealed class GeneratedPanelTests
{
    private static GeneratedStackElement Panel(params Element[] children) => new(children);

    private static StackPanel PanelOf(TestHost host) => Assert.IsType<StackPanel>(host.Root.Child);

    [AvaloniaFact]
    public void A_groups_children_mount_into_the_generated_panel()
    {
        var host = new TestHost(() => Panel(TextBlock("a"), Group(TextBlock("b"), TextBlock("c")), TextBlock("d")));

        Assert.Equal(["a", "b", "c", "d"], host.Texts());
        Assert.All(PanelOf(host).Children, c => Assert.IsType<TextBlock>(c));
    }

    [AvaloniaFact]
    public void A_groups_keyed_children_mount_into_the_generated_panel()
    {
        var host = new TestHost(() => Panel(Group(TextBlock("a").WithKey("a"), TextBlock("b").WithKey("b"))));

        Assert.Equal(["a", "b"], host.Texts());
        Assert.Equal(["a", "b"], PanelOf(host).Children.Select(c => Reconciler.GetMountedElement(c)!.Key));
    }

    [AvaloniaFact]
    public void Nested_groups_are_flattened_and_updated_in_place()
    {
        var inner = 2;
        var host = new TestHost(() => Panel(TextBlock("a"),
            Group(TextBlock("b"), Group([.. Enumerable.Range(0, inner).Select(i => (Element)TextBlock("n" + i))])),
            TextBlock("z")));
        Assert.Equal(["a", "b", "n0", "n1", "z"], host.Texts());
        var before = host.All<TextBlock>();

        inner = 3;
        host.Render();

        Assert.Equal(["a", "b", "n0", "n1", "n2", "z"], host.Texts());
        var after = host.All<TextBlock>();
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[1]);
        Assert.Same(before[2], after[2]);
        Assert.Same(before[3], after[3]);
        Assert.Same(before[4], after[5]);
    }

    [AvaloniaFact]
    public void A_group_that_changes_size_keeps_its_siblings_controls()
    {
        var count = 1;
        var host = new TestHost(() => Panel(TextBlock("before"),
            Group([.. Enumerable.Range(0, count).Select(i => (Element)TextBlock("g" + i))]), TextBlock("after")));
        Assert.Equal(["before", "g0", "after"], host.Texts());
        var first = host.All<TextBlock>()[0];
        var last = host.All<TextBlock>()[2];

        count = 3;
        host.Render();
        Assert.Equal(["before", "g0", "g1", "g2", "after"], host.Texts());
        Assert.Same(first, host.All<TextBlock>()[0]);
        Assert.Same(last, host.All<TextBlock>()[4]);

        count = 0;
        host.Render();
        Assert.Equal(["before", "after"], host.Texts());
        Assert.Same(first, host.All<TextBlock>()[0]);
        Assert.Same(last, host.All<TextBlock>()[1]);
    }

    [AvaloniaFact]
    public void Keyed_children_inside_a_group_move_with_their_controls()
    {
        string[] keys = ["a", "b", "c", "d"];
        var host = new TestHost(() => Panel(TextBlock("head"),
            Group([.. keys.Select(k => (Element)TextBlock(k).WithKey(k))]), TextBlock("tail")));
        var before = host.All<TextBlock>().ToDictionary(t => t.Text!);

        keys = ["d", "c", "b", "a"];
        host.Render();

        Assert.Equal(["head", "d", "c", "b", "a", "tail"], host.Texts());
        Assert.All(host.All<TextBlock>(), t => Assert.Same(before[t.Text!], t));
    }

    [AvaloniaFact]
    public void Children_with_no_group_are_passed_through_as_they_are()
    {
        // What keeps Isotope's own panels as they were: their records already flatten, and an array with no group comes
        // back as the same instance, so a reused children array still compares equal.
        Element[] children = [TextBlock("a"), TextBlock("b")];
        Assert.Same(children, PanelChildren.Of(children));
        Element[] grouped = [TextBlock("a"), Group(TextBlock("b"))];
        Assert.Same(PanelChildren.Of(grouped), PanelChildren.Of(grouped));
    }
}
