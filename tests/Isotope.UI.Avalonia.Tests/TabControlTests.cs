using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// TabControl (Reactor's TabView, #26): tabs mount as TabItems that are kept across renders, so a component in a tab
/// keeps its state; the controlled selected index, whose engine writes don't echo while the user's changes reach the
/// state; tabs added and removed; a nested control's selection doesn't reach the tabs' callback.
/// </summary>
public class TabControlTests
{
    private static List<TabItem> Items(TabControl tabs) => tabs.Items.Cast<TabItem>().ToList();

    private static Func<RenderContext, Element> Counter(string name, List<string> log, Dictionary<string, Action>? bumps = null) => ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        if (bumps is not null) bumps[name] = () => setN(n + 1);
        ctx.UseEffect(() => () => log.Add("cleanup " + name), Array.Empty<object>());
        return TextBlock(name + " " + n);
    };

    private static string? TextOf(TabItem item) => item.Content switch
    {
        TextBlock t => t.Text,
        Border { Child: TextBlock t } => t.Text, // a component's wrapper
        _ => null,
    };

    [AvaloniaFact]
    public void TabControl_mounts_a_tab_item_per_tab_and_updates_headers_and_content_in_place()
    {
        var log = new List<string>();
        var bumps = new Dictionary<string, Action>();
        var a = Counter("a", log, bumps);
        var second = "Second";
        var host = new TestHost(() => TabControl(TabItem("First", Component(a)), TabItem(second, TextBlock("two"))));
        var tabs = host.All<TabControl>()[0];
        var items = Items(tabs);
        Assert.Equal(["First", "Second"], items.Select(i => i.Header));
        Assert.Equal(["a 0", "two"], items.Select(TextOf));

        bumps["a"]();
        host.Flush();
        second = "Two";
        host.Render();
        Assert.Same(tabs, host.All<TabControl>()[0]);
        Assert.Equal(items, Items(tabs));
        Assert.Equal(["First", "Two"], Items(tabs).Select(i => i.Header));
        Assert.Equal("a 1", TextOf(Items(tabs)[0]));
        Assert.Empty(log);
    }

    [AvaloniaFact]
    public void TabControl_user_selection_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<int>();
        Action<int>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (index, setIndex) = ctx.UseState(1);
            set = setIndex;
            return VStack(
                TextBlock("tab " + index),
                TabControl(index, i => { reported.Add(i); setIndex(i); }, TabItem("A", TextBlock("a")), TabItem("B", TextBlock("b")), TabItem("C", TextBlock("c"))));
        }));
        var tabs = host.All<TabControl>()[0];
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Empty(reported); // the mount's write doesn't echo

        tabs.SelectedIndex = 2; // as a click on the third tab does
        host.Flush();
        Assert.Equal([2], reported);
        Assert.Equal("tab 2", host.Texts()[0]);

        set!(0); // from the state: written, not reported
        host.Flush();
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal([2], reported);
        Assert.Same(tabs, host.All<TabControl>()[0]);
    }

    [AvaloniaFact]
    public void TabControl_without_a_selected_index_leaves_the_selection_to_the_control()
    {
        var reported = new List<int>();
        var label = "A";
        var host = new TestHost(() => TabControl(Optional<int>.Unset, i => reported.Add(i), TabItem(label, TextBlock("a")), TabItem("B", TextBlock("b"))));
        var tabs = host.All<TabControl>()[0];
        tabs.SelectedIndex = 1;
        Assert.Equal([1], reported);

        label = "A!"; // an update leaves the user's selection alone
        host.Render();
        Assert.Equal(1, tabs.SelectedIndex);
    }

    [AvaloniaFact]
    public void TabControl_tabs_added_and_removed_keep_the_others_and_tear_down_the_removed_content()
    {
        var log = new List<string>();
        var b = Counter("b", log);
        var three = false;
        var host = new TestHost(() => three
            ? TabControl(TabItem("A", TextBlock("a")), TabItem("B", Component(b)), TabItem("C", TextBlock("c")))
            : TabControl(TabItem("A", TextBlock("a"))));
        var tabs = host.All<TabControl>()[0];
        var first = Items(tabs)[0];

        three = true;
        host.Render();
        Assert.Equal(["A", "B", "C"], Items(tabs).Select(i => i.Header));
        Assert.Same(first, Items(tabs)[0]);
        Assert.Equal("b 0", TextOf(Items(tabs)[1]));

        three = false;
        host.Render();
        Assert.Equal(["A"], Items(tabs).Select(i => i.Header));
        Assert.Same(first, Items(tabs)[0]);
        Assert.Equal(["cleanup b"], log);
    }

    [AvaloniaFact]
    public void TabControl_removing_the_selected_tab_lands_on_the_declared_index_without_a_report()
    {
        // Avalonia keeps a tab selected, so removing the selected one moves the selection inside the engine's update:
        // that move isn't the user's, and the index the app declares wins.
        var reported = new List<int>();
        Action? close = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (state, setState) = ctx.UseState((Count: 3, Index: 2));
            close = () => setState((2, 0)); // closes the last tab and selects the first
            var tabs = Enumerable.Range(0, state.Count).Select(i => TabItem("T" + i, TextBlock("t" + i))).ToArray();
            return TabControl(state.Index, i => { reported.Add(i); setState(state with { Index = i }); }, tabs);
        }));
        var tabControl = host.All<TabControl>()[0];
        Assert.Equal(2, tabControl.SelectedIndex);

        close!();
        host.Flush();
        Assert.Equal(0, tabControl.SelectedIndex);
        Assert.Empty(reported);
    }

    [AvaloniaFact]
    public void TabControl_first_tab_added_is_not_reported_as_a_user_selection()
    {
        var reported = new List<int>();
        var tabs = Array.Empty<TabItemData>();
        var host = new TestHost(() => TabControl(Optional<int>.Unset, i => reported.Add(i), tabs));
        var tabControl = host.All<TabControl>()[0];

        tabs = [TabItem("A", TextBlock("a")), TabItem("B", TextBlock("b"))];
        host.Render();
        Assert.Equal(0, tabControl.SelectedIndex);
        Assert.Empty(reported);

        tabControl.SelectedIndex = 1; // the user's own changes still arrive
        Assert.Equal([1], reported);
    }

    [AvaloniaFact]
    public void TabControl_index_past_the_end_does_not_swallow_the_next_user_selection()
    {
        // An index the control can't take (the app closed a tab and left its index) changes nothing and raises no event,
        // so it must not leave anything behind to drop the user's next click.
        var reported = new List<int>();
        var count = 3;
        var host = new TestHost(() => TabControl(2, i => reported.Add(i),
            Enumerable.Range(0, count).Select(i => TabItem("T" + i, TextBlock("t" + i))).ToArray()));
        var tabControl = host.All<TabControl>()[0];

        count = 2;
        host.Render();
        host.Render();
        tabControl.SelectedIndex = tabControl.SelectedIndex == 0 ? 1 : 0; // the user's click
        Assert.Equal([tabControl.SelectedIndex], reported);
    }

    [AvaloniaFact]
    public void TabControl_tab_contents_are_torn_down_with_the_tabs()
    {
        var log = new List<string>();
        var a = Counter("a", log);
        var b = Counter("b", log);
        var host = new TestHost(() => VStack(TabControl(TabItem("A", Component(a)), TabItem("B", Component(b)))));

        host.Unmount();
        Assert.Equal(["cleanup b", "cleanup a"], log);
    }

    [AvaloniaFact]
    public void TabControl_does_not_report_the_selection_of_a_list_inside_a_tab()
    {
        // SelectionChanged bubbles on Avalonia: a ComboBox's change also reaches the TabControl.
        var reported = new List<int>();
        var host = new TestHost(() => TabControl(0, i => reported.Add(i), TabItem("A", ComboBox(["x", "y"], 0)), TabItem("B", TextBlock("b"))));
        var tabs = host.All<TabControl>()[0];
        var combo = Assert.IsType<ComboBox>(Items(tabs)[0].Content);
        // In a window, so the TabControl's template hosts the selected tab's content and the event has a route to it.
        var window = new Window { Width = 400, Height = 300, Content = host.Root };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Contains(tabs, combo.GetVisualAncestors());

            combo.SelectedIndex = 1;
            Assert.Empty(reported);
            Assert.Equal(0, tabs.SelectedIndex);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TabControl_strip_placement_is_written_and_cleared()
    {
        Dock? placement = Dock.Left;
        var host = new TestHost(() => TabControl(TabItem("A", TextBlock("a"))) with { TabStripPlacement = placement });
        var tabs = host.All<TabControl>()[0];
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);

        placement = null;
        host.Render();
        Assert.Equal(new TabControl().TabStripPlacement, tabs.TabStripPlacement);
    }
}
