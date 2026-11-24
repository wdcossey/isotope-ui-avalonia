using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// #9: a nested FlexPanel fills its slot or fits its content depending on the outer panel's Yoga height mode (upstream's
/// [ThreadStatic] handshake), but Avalonia skips a Measure at an unchanged constraint and re-measures an invalidated
/// panel on its own. Each case puts a nested column (content 40 high) in a 400 x 300 window, with nothing in between
/// that would re-measure it: a basis of 0 (CSS <c>flex: 1 1 0</c>), an explicit min size, or a wrapper.
/// </summary>
public sealed class FlexPanelAmbientModeTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private void Host(Control content)
    {
        var w = new Window { Width = 400, Height = 300, Content = content, SizeToContent = SizeToContent.Manual };
        _windows.Add(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Box(double h) => new() { Height = h, Background = Brushes.SteelBlue };

    private static FlexPanel Nested(Control? leaf = null) =>
        new() { Direction = FlexDirection.Column, Children = { leaf ?? Box(40) } };

    private static T Flex1<T>(T c) where T : Control
    {
        FlexPanel.SetGrow(c, 1);
        FlexPanel.SetBasis(c, 0);
        return c;
    }

    [AvaloniaFact]
    public void AlignSelf_FromStretchToFlexStart_RefitsANestedPanel()
    {
        // Measured Exactly 300 (fill), then AtMost 300 (fit content): the same constraint.
        var nested = Flex1(Nested());
        Host(new FlexPanel { Children = { nested } });
        Assert.Equal(300, nested.Bounds.Height);

        FlexPanel.SetAlignSelf(nested, FlexAlign.FlexStart);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void AlignSelf_FromStretchToFlexStart_RefitsAPanelInsideAWrapper()
    {
        // A wrapper (a card here; every component is mounted in a Border) passes the constraint on, and its own
        // measure cache hides the nested panel from the outer one.
        var nested = Nested();
        var card = Flex1(new Border { Padding = new Thickness(5), Child = nested });
        Host(new FlexPanel { Children = { card } });
        Assert.Equal(290, nested.Bounds.Height);

        FlexPanel.SetAlignSelf(card, FlexAlign.FlexStart);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void AlignSelf_FromFlexStartToStretch_FillsANestedPanel()
    {
        // The other way round. The outer's bridge reports an Exactly slot itself, so the bounds were right before #9,
        // but the panel's own measure stayed at its content height (DesiredSize 40).
        var nested = Flex1(Nested());
        FlexPanel.SetAlignSelf(nested, FlexAlign.FlexStart);
        Host(new FlexPanel { Children = { nested } });
        Assert.Equal(40, nested.Bounds.Height);

        FlexPanel.SetAlignSelf(nested, FlexAlign.Stretch);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(300, nested.Bounds.Height);
        Assert.Equal(300, nested.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void NestedPanel_ChangedOnItsOwn_KeepsFittingItsContent()
    {
        // Only the nested panel is invalidated, so Avalonia re-measures it alone at its last constraint (300 high),
        // where no outer mode is set.
        var nested = Flex1(Nested());
        Host(new FlexPanel { AlignItems = FlexAlign.FlexStart, Children = { nested } });
        Assert.Equal(40, nested.Bounds.Height);

        nested.RowGap = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.Bounds.Height);
    }

    private sealed class CountingFlexPanel : FlexPanel
    {
        public int Measures;
        protected override Size MeasureOverride(Size availableSize) { Measures++; return base.MeasureOverride(availableSize); }
    }

    [AvaloniaFact]
    public void NestedPanel_ChangedOnItsOwn_IsNotReMeasuredThroughTheOuter()
    {
        // Re-measured alone, the nested panel keeps the fit-content mode the outer gave it, so its size doesn't change
        // and the outer isn't re-measured. Measured with no mode it would fill (300), and the outer would have to
        // measure it again to undo that.
        var nested = Flex1(Nested());
        var outer = new CountingFlexPanel { AlignItems = FlexAlign.FlexStart, Children = { nested } };
        Host(outer);
        outer.Measures = 0;

        nested.RowGap = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, outer.Measures);
        Assert.Equal(40, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void PanelInsideAWrapper_ChangedOnItsOwn_KeepsFittingItsContent()
    {
        var nested = Nested();
        var card = Flex1(new Border { Padding = new Thickness(5), Child = nested });
        Host(new FlexPanel { AlignItems = FlexAlign.FlexStart, Children = { card } });
        Assert.Equal(40, nested.Bounds.Height);

        nested.RowGap = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.Bounds.Height);
        Assert.Equal(50, card.Bounds.Height);
    }

    [AvaloniaFact]
    public void LeafHeightChange_InANestedPanel_FitsTheNewContent()
    {
        var leaf = Box(40);
        var nested = Flex1(Nested(leaf));
        Host(new FlexPanel { AlignItems = FlexAlign.FlexStart, Children = { nested } });
        Assert.Equal(40, nested.Bounds.Height);

        leaf.Height = 50;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void Column_NestedPanelWithMinHeight0_ChangedOnItsOwn_KeepsItsHeight()
    {
        // Yoga measures the nested panel AtMost 300 for its basis and reuses that for its 40-high slot.
        var nested = Nested();
        FlexPanel.SetFlexMinHeight(nested, 0);
        var filler = new Border();
        FlexPanel.SetGrow(filler, 1);
        Host(new FlexPanel { Direction = FlexDirection.Column, Children = { nested, filler } });
        Assert.Equal(40, nested.Bounds.Height);

        nested.RowGap = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.Bounds.Height);
        Assert.Equal(40, filler.Bounds.Y);
    }

    [AvaloniaFact]
    public void PanelReMeasuredOnItsOwn_GivesTheChildrenItMeasuresItsMode()
    {
        // Review pass 1: the middle panel is re-measured alone after its own change, and keeps the outer's fit-content
        // mode; the children it measures itself (here a fixed-size box Yoga doesn't measure, in its final sweep) must
        // get that mode too, not none (fill). The bounds come from the box either way; the nested panel's measure didn't.
        var nested = Nested();
        var fixedBox = new Border { Width = 100, Height = 100, Child = nested };
        var middle = Flex1(new FlexPanel { Children = { fixedBox } });
        Host(new FlexPanel { AlignItems = FlexAlign.FlexStart, Children = { middle } });
        Assert.Equal(40, nested.DesiredSize.Height);

        middle.RowGap = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.DesiredSize.Height);
        Assert.Equal(100, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void PanelNotDependingOnTheMode_StillGivesItToTheChildrenItMeasures()
    {
        // Review pass 2: the middle panel is top-aligned, so its own height doesn't depend on the outer's mode, but the
        // nested panel its final sweep measures does: it must follow the outer from stretch (fill) to flex-start.
        var nested = Nested();
        var middle = Flex1(new FlexPanel { VerticalAlignment = VerticalAlignment.Top, Children = { new Border { Width = 100, Height = 100, Child = nested } } });
        var outer = new FlexPanel { Children = { middle } };
        Host(outer);
        Assert.Equal(100, nested.DesiredSize.Height);

        outer.AlignItems = FlexAlign.FlexStart;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, nested.DesiredSize.Height);
    }

    private static Action<double>? s_setGap;

    private static Element Card(RenderContext ctx)
    {
        var (gap, setGap) = ctx.UseState(0.0);
        s_setGap = setGap;
        return new FlexElement([
            TextBlock("a").WithModifiers(m => m with { Height = 20 }),
            TextBlock("b").WithModifiers(m => m with { Height = 20 }),
        ]) { Direction = FlexDirection.Column, RowGap = gap };
    }

    [AvaloniaFact]
    public void Component_ReRenderingANestedFlex_KeepsFittingItsContent()
    {
        // The layout an app writes: a component (mounted in a Border) that re-renders its own flex column.
        var host = new TestHost(() => new FlexElement([new FuncElement(Card).Flex(grow: 1, basis: 0)]) { AlignItems = FlexAlign.FlexStart });
        Host(host.Root);
        var nested = host.All<FlexPanel>()[1];
        Assert.Equal(40, nested.Bounds.Height);

        s_setGap!(4);
        host.Flush();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, nested.RowGap);
        Assert.Equal(44, nested.Bounds.Height);
    }

    [AvaloniaFact]
    public void MinContentProbe_UnderAnOuterPanel_DoesNotDependOnItsMode()
    {
        // Issue 1 of #9, a guard: the middle column probes its children's min-content height while the outer's mode is
        // still set (as upstream's): Exactly 300 under a stretching outer, AtMost 300 under a flex-start one. Our probe
        // (#15) measures at an unbounded height, or at 0 (which Avalonia clamps), so the mode can't change it. 400 of
        // content in 300: the unwrapping panel keeps its min-content (200), the wrapping one (min-content 0) shrinks.
        (double, double, double) Layout(FlexAlign outerAlign)
        {
            var keeps = new FlexPanel { Direction = FlexDirection.Column, Children = { Box(200) } };
            var shrinks = new FlexPanel { Direction = FlexDirection.Column, Wrap = FlexWrap.Wrap, Children = { Box(200) } };
            var middle = new FlexPanel { Direction = FlexDirection.Column, Children = { keeps, shrinks } };
            FlexPanel.SetGrow(middle, 1);
            Host(new FlexPanel { AlignItems = outerAlign, Children = { middle } });
            return (keeps.Bounds.Height, shrinks.Bounds.Height, shrinks.Bounds.Y);
        }
        Assert.Equal((200, 100, 200), Layout(FlexAlign.Stretch));
        Assert.Equal((200, 100, 200), Layout(FlexAlign.FlexStart));
    }
}
