using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvSplitView = Avalonia.Controls.SplitView;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// SplitView (#26): pane and content mount in their slots and keep a child component's state; IsPaneOpen is
/// edge-triggered, as upstream's, and its engine writes don't reach OnPaneOpenChanged while the control's own changes do;
/// a nested split view's events don't reach the outer one's callback.
/// </summary>
public class SplitViewTests
{
    private static Func<RenderContext, Element> Counter(string name, List<string> log, Dictionary<string, Action>? bumps = null) => ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        if (bumps is not null) bumps[name] = () => setN(n + 1);
        ctx.UseEffect(() => () => log.Add("cleanup " + name), Array.Empty<object>());
        return TextBlock(name + " " + n);
    };

    private static string? TextOf(object? slot) => slot switch
    {
        TextBlock t => t.Text,
        Border { Child: TextBlock t } => t.Text,
        _ => null,
    };

    [AvaloniaFact]
    public void SplitView_mounts_pane_and_content_and_keeps_their_components_state()
    {
        var log = new List<string>();
        var bumps = new Dictionary<string, Action>();
        var pane = Counter("pane", log, bumps);
        var length = 200.0;
        var host = new TestHost(() => SplitView(Component(pane), TextBlock("content")) with { OpenPaneLength = length });
        var view = host.All<AvSplitView>()[0];
        Assert.Equal(("pane 0", "content"), (TextOf(view.Pane), TextOf(view.Content)));
        Assert.True(view.IsPaneOpen); // upstream's default
        Assert.Equal((200.0, SplitViewDisplayMode.Overlay), (view.OpenPaneLength, view.DisplayMode));

        bumps["pane"]();
        host.Flush();
        length = 250;
        host.Render();
        Assert.Same(view, host.All<AvSplitView>()[0]);
        Assert.Equal(("pane 1", 250.0), (TextOf(view.Pane), view.OpenPaneLength));
        Assert.Empty(log);
    }

    [AvaloniaFact]
    public void SplitView_pane_and_content_are_torn_down_with_it()
    {
        var log = new List<string>();
        var (pane, content) = (Counter("pane", log), Counter("content", log));
        var host = new TestHost(() => VStack(SplitView(Component(pane), Component(content))));

        host.Unmount();
        Assert.Equal(["cleanup pane", "cleanup content"], log);
    }

    [AvaloniaFact]
    public void SplitView_user_change_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (open, setOpen) = ctx.UseState(false);
            set = setOpen;
            return VStack(TextBlock("open " + open),
                SplitView(TextBlock("pane"), TextBlock("content")) with
                {
                    IsPaneOpen = open, OnPaneOpenChanged = v => { reported.Add(v); setOpen(v); },
                });
        }));
        var view = host.All<AvSplitView>()[0];
        Assert.False(view.IsPaneOpen);

        set!(true); // from the state: written, not reported
        host.Flush();
        Assert.True(view.IsPaneOpen);
        Assert.Empty(reported);

        view.IsPaneOpen = false; // as a light dismiss does
        host.Flush();
        Assert.Equal([false], reported);
        Assert.Equal("open False", host.Texts()[0]);

        view.IsPaneOpen = true; // the next change of the control's still arrives
        Assert.Equal([false, true], reported);
    }

    [AvaloniaFact]
    public void SplitView_is_pane_open_is_edge_triggered_as_upstream()
    {
        // A pane the control closed stays closed when the same declared value renders again.
        var tick = 0;
        var host = new TestHost(() => SplitView(TextBlock("pane"), TextBlock("content " + tick)));
        var view = host.All<AvSplitView>()[0];
        Assert.True(view.IsPaneOpen);

        view.IsPaneOpen = false;
        tick = 1;
        host.Render();
        Assert.False(view.IsPaneOpen);
    }

    [AvaloniaFact]
    public void SplitView_does_not_report_a_nested_split_views_pane()
    {
        var reported = new List<bool>();
        var host = new TestHost(() => SplitView(
            SplitView(TextBlock("inner pane"), TextBlock("inner content")),
            TextBlock("content")) with { OnPaneOpenChanged = v => reported.Add(v) });
        var inner = Assert.IsType<AvSplitView>(host.All<AvSplitView>()[0].Pane);

        inner.IsPaneOpen = false;
        Assert.Empty(reported);
    }

    [AvaloniaFact]
    public void SplitView_pane_background_and_placement_are_written_and_cleared()
    {
        var set = true;
        var host = new TestHost(() => set
            ? SplitView() with { PaneBackground = Brushes.Red, PanePlacement = SplitViewPanePlacement.Right }
            : SplitView());
        var view = host.All<AvSplitView>()[0];
        Assert.Equal((Brushes.Red, SplitViewPanePlacement.Right), (view.PaneBackground, view.PanePlacement));

        set = false;
        host.Render();
        Assert.False(view.IsSet(AvSplitView.PaneBackgroundProperty));
        Assert.Equal(new AvSplitView().PanePlacement, view.PanePlacement);
    }
}
