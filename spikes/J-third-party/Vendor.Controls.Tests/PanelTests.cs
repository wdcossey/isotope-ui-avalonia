using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI;
using Isotope.UI.Core;
using Vendor.Controls.Isotope;
using Xunit;
using static Isotope.UI.Factories;
using static Vendor.Controls.Isotope.VendorFactories;
// The factories named after their controls hide the control types in member access (CS0119), as Isotope's Button does.
using RatingBarControl = Vendor.Controls.RatingBar;
using CalloutCardControl = Vendor.Controls.CalloutCard;
using SpreadPanelControl = Vendor.Controls.SpreadPanel;
using AvaloniaButton = Avalonia.Controls.Button;
using AvaloniaSlider = Avalonia.Controls.Slider;

namespace Vendor.Controls.Tests;

/// <summary>The vendor's panel: the generated children strategy, keyed moves inside it, and Isotope's group element.</summary>
public sealed class PanelTests
{
    private static Element Counter(string name) => Component(ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        return Button(name + " " + n, () => setN(n + 1));
    });

    [AvaloniaFact]
    public void A_keyed_reverse_inside_the_vendors_panel_moves_its_children_without_detaching_them()
    {
        string[] ids = ["a", "b", "c", "d"];
        Action<string[]>? setOrder = null;
        using var m = new Mounted(ctx =>
        {
            var (order, set) = ctx.UseState(ids);
            setOrder = set;
            return SpreadPanel([.. order.Select(id => (Element)CalloutCard(id, Counter(id)).WithKey(id))]) with { Spacing = 4 };
        });
        var panel = m.One<SpreadPanelControl>();
        Assert.Equal(4, panel.Spacing);
        var before = panel.Children.ToList();
        var detached = 0;
        foreach (var c in before) c.DetachedFromVisualTree += (_, _) => detached++;
        MountAndUpdateTests.TestClick(m.All<AvaloniaButton>().First(b => b.Content as string == "b 0"));
        Mounted.Pump();

        setOrder!([.. ids.Reverse()]);
        Mounted.Pump();

        Assert.Equal([.. Enumerable.Reverse(before)], panel.Children);
        Assert.Equal(0, detached);
        Assert.Equal(["d 0", "c 0", "b 1", "a 0"],
            m.All<AvaloniaButton>().Where(b => b.Name != "PART_Dismiss").Select(b => b.Content as string));
    }

    [AvaloniaFact]
    public void Children_added_and_removed_by_key_keep_the_others()
    {
        Action<string[]>? setIds = null;
        using var m = new Mounted(ctx =>
        {
            var (shown, set) = ctx.UseState<string[]>(["a", "b", "c"]);
            setIds = set;
            return SpreadPanel([.. shown.Select(id => (Element)RatingBar(id[0] - 'a' + 1).WithKey(id))]);
        });
        var panel = m.One<SpreadPanelControl>();
        var c = panel.Children[2];
        setIds!(["c", "x"]);
        Mounted.Pump();
        Assert.Equal(2, panel.Children.Count);
        Assert.Same(c, panel.Children[0]);
        Assert.Equal([3, 5], panel.Children.Cast<RatingBarControl>().Select(r => r.Value));
    }

    // Gap 4 (docs/spikes/J-third-party-controls.md): Isotope's own panel records flatten a Group into their children with
    // Reconciler.Flatten, which is internal, so a library's panel gets the group as one child, and mounting it throws.
    // Parity would mount three ratings. Pinned as it is now.
    [AvaloniaFact]
    public void Gap_a_group_element_inside_the_vendors_panel_throws()
    {
        using var m = new Mounted(_ => ErrorBoundary(SpreadPanel(RatingBar(1), Group(RatingBar(2), RatingBar(3))),
            ex => TextBlock("caught: " + ex.GetType().Name)));
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "caught: NotSupportedException");
        Assert.Empty(m.All<RatingBarControl>());
    }

    [AvaloniaFact]
    public void Builtin_a_group_element_inside_a_stack_is_flattened()
    {
        using var m = new Mounted(_ => VStack(RatingBar(1), Group(RatingBar(2), RatingBar(3))));
        Assert.Equal(3, m.One<StackPanel>().Children.Count);
    }

    [AvaloniaFact]
    public void An_empty_element_inside_the_vendors_panel_is_left_out()
    {
        // What If(false, ...) gives: the generated children strategy leaves it out, as Isotope's own panels do.
        using var m = new Mounted(_ => SpreadPanel(RatingBar(1), Empty(), RatingBar(2)));
        var panel = m.One<SpreadPanelControl>();
        Assert.Equal(2, panel.Children.Count);
        Assert.All(panel.Children, c => Assert.IsType<RatingBarControl>(c));
    }
}
