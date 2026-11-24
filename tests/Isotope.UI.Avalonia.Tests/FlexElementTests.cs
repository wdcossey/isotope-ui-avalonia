using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>FlexElement (upstream's record, with its FlexPanel's member names) mounted through its generated descriptor (#2).</summary>
public sealed class FlexElementTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private TestHost Show(Func<Element> render, double width = 400, double height = 300)
    {
        var host = new TestHost(render);
        var w = new Window { Width = width, Height = height, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }

    private static Element Box(string name, double width) => TextBlock(name).WithKey(name).WithModifiers(m => m with { Width = width, Height = 40 });

    [AvaloniaFact]
    public void Mounts_a_FlexPanel_with_the_container_members_and_its_children()
    {
        var host = new TestHost(() => new FlexElement([TextBlock("a"), TextBlock("b")])
        {
            Direction = FlexDirection.Column, JustifyContent = FlexJustify.Center, AlignItems = FlexAlign.FlexEnd,
            AlignContent = FlexAlign.Center, Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 6, FlexPadding = new Thickness(1, 2, 3, 4),
        });

        var panel = Assert.IsType<FlexPanel>(host.Root.Child);
        Assert.Equal(FlexDirection.Column, panel.Direction);
        Assert.Equal(FlexJustify.Center, panel.JustifyContent);
        Assert.Equal(FlexAlign.FlexEnd, panel.AlignItems);
        Assert.Equal(FlexAlign.Center, panel.AlignContent);
        Assert.Equal(FlexWrap.Wrap, panel.Wrap);
        Assert.Equal(4, panel.ColumnGap);
        Assert.Equal(6, panel.RowGap);
        Assert.Equal(new Thickness(1, 2, 3, 4), panel.FlexPadding);
        Assert.Equal(["a", "b"], host.Texts());
    }

    [AvaloniaFact]
    public void A_grown_child_takes_the_remaining_width_and_loses_it_when_the_flex_data_goes()
    {
        var grow = true;
        var host = Show(() => new FlexElement([
            Box("a", 100),
            grow ? TextBlock("b").WithKey("b").WithModifiers(m => m with { Height = 40 }).Flex(grow: 1)
                 : TextBlock("b").WithKey("b").WithModifiers(m => m with { Height = 40 }),
            Box("c", 50)]));
        var b = host.All<TextBlock>()[1];
        Assert.Equal(1, FlexPanel.GetGrow(b));
        Assert.Equal(100, b.Bounds.X);
        Assert.Equal(250, b.Bounds.Width);

        grow = false;
        host.Render();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(b, host.All<TextBlock>()[1]);
        Assert.Equal(0, FlexPanel.GetGrow(b)); // reset on the reused control
        Assert.True(b.Bounds.Width < 250, $"width {b.Bounds.Width}");
    }

    [AvaloniaFact]
    public void Every_flex_member_reaches_the_child_and_is_cleared_when_unset()
    {
        var set = true;
        var host = new TestHost(() => new FlexElement([set
            ? TextBlock("a").Flex(grow: 2, shrink: 0, basis: 30, minWidth: 5, minHeight: 6, alignSelf: FlexAlign.Center,
                position: FlexPositionType.Absolute, left: 1, top: 2, right: 3, bottom: 4)
            : TextBlock("a")]));
        var a = host.All<TextBlock>()[0];
        Assert.Equal((2.0, 0.0, 30.0, 5.0, 6.0, FlexAlign.Center, FlexPositionType.Absolute),
            (FlexPanel.GetGrow(a), FlexPanel.GetShrink(a), FlexPanel.GetBasis(a), FlexPanel.GetFlexMinWidth(a),
             FlexPanel.GetFlexMinHeight(a), FlexPanel.GetAlignSelf(a), FlexPanel.GetPosition(a)));
        Assert.Equal((1.0, 2.0, 3.0, 4.0), (FlexPanel.GetLeft(a), FlexPanel.GetTop(a), FlexPanel.GetRight(a), FlexPanel.GetBottom(a)));

        set = false;
        host.Render();

        var defaults = new TextBlock();
        Assert.Same(a, host.All<TextBlock>()[0]);
        Assert.Equal((0.0, 1.0, FlexAlign.Auto, FlexPositionType.Relative),
            (FlexPanel.GetGrow(a), FlexPanel.GetShrink(a), FlexPanel.GetAlignSelf(a), FlexPanel.GetPosition(a)));
        Assert.All(new[] { FlexPanel.GetBasis(a), FlexPanel.GetFlexMinWidth(a), FlexPanel.GetFlexMinHeight(a),
            FlexPanel.GetLeft(a), FlexPanel.GetTop(a), FlexPanel.GetRight(a), FlexPanel.GetBottom(a) }, v => Assert.True(double.IsNaN(v)));
    }

    [AvaloniaFact]
    public void A_keyed_reorder_keeps_the_controls_and_lays_them_out_in_the_new_order()
    {
        string[] keys = ["a", "b", "c"];
        var host = Show(() => new FlexElement(keys.Select(k => Box(k, 50)).ToArray()) { ColumnGap = 10 });
        var before = host.All<TextBlock>().ToDictionary(t => t.Text!);

        keys = ["c", "a", "b"];
        host.Render();
        Dispatcher.UIThread.RunJobs();

        var after = host.All<TextBlock>();
        Assert.Equal(["c", "a", "b"], after.Select(t => t.Text));
        Assert.All(after, t => Assert.Same(before[t.Text!], t));
        Assert.Equal([0.0, 60.0, 120.0], after.Select(t => t.Bounds.X));
    }

    [AvaloniaFact]
    public void A_container_member_change_updates_the_same_panel_and_lays_out_again()
    {
        var column = false;
        var host = Show(() => new FlexElement([Box("a", 50), Box("b", 50)])
        {
            Direction = column ? FlexDirection.Column : FlexDirection.Row,
            FlexPadding = column ? new Thickness(5) : default,
        });
        var panel = Assert.IsType<FlexPanel>(host.Root.Child);
        var b = host.All<TextBlock>()[1];
        Assert.Equal(new Point(50, 0), b.Bounds.Position);

        column = true;
        host.Render();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(panel, host.Root.Child);
        Assert.Equal(FlexDirection.Column, panel.Direction);
        Assert.Equal(new Thickness(5), panel.FlexPadding);
        Assert.Equal(45, b.Bounds.Y); // 5 padding + 40 for a
    }

    [AvaloniaFact]
    public void An_equal_FlexElement_is_skipped_and_a_changed_one_is_not()
    {
        // A skipped element is never applied, so the panel keeps the element it was last updated with.
        var children = new Element[] { TextBlock("a") };
        var gap = 3.0;
        var rendered = new List<FlexElement>();
        var host = new TestHost(() =>
        {
            var flex = new FlexElement(children) { ColumnGap = gap };
            rendered.Add(flex);
            return VStack(flex);
        });
        var panel = host.All<FlexPanel>()[0];

        host.Render(); // a new, equal FlexElement
        Assert.Same(rendered[0], Reconciler.GetMountedElement(panel));

        gap = 4;
        host.Render();
        Assert.Same(rendered[2], Reconciler.GetMountedElement(panel));
        Assert.Same(panel, host.All<FlexPanel>()[0]);
        Assert.Equal(4, panel.ColumnGap);
    }

    [AvaloniaFact]
    public void Flex_rejects_negative_or_non_finite_factors_as_upstream_does()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextBlock("a").Flex(grow: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextBlock("a").Flex(shrink: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextBlock("a").Flex(minWidth: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextBlock("a").Flex(minHeight: -2));
    }
}
