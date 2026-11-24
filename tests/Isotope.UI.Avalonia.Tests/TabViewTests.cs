using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// TabView as an Avalonia TabControl (#26): tabs mount as TabItems that are kept across renders, so a component in a tab
/// keeps its state; the controlled selected index, whose engine writes don't echo while the user's changes reach the
/// state; tabs added and removed; a nested control's selection doesn't reach the tabs' callback.
/// </summary>
public class TabViewTests
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
    public void TabView_mounts_a_tab_item_per_tab_and_updates_headers_and_content_in_place()
    {
        var log = new List<string>();
        var bumps = new Dictionary<string, Action>();
        var a = Counter("a", log, bumps);
        var second = "Second";
        var host = new TestHost(() => TabView(Tab("First", Component(a)), Tab(second, TextBlock("two"))));
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
    public void TabView_user_selection_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<int>();
        Action<int>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (index, setIndex) = ctx.UseState(1);
            set = setIndex;
            return VStack(
                TextBlock("tab " + index),
                TabView(index, i => { reported.Add(i); setIndex(i); }, Tab("A", TextBlock("a")), Tab("B", TextBlock("b")), Tab("C", TextBlock("c"))));
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
    public void TabView_without_a_selected_index_leaves_the_selection_to_the_control()
    {
        var reported = new List<int>();
        var label = "A";
        var host = new TestHost(() => TabView(Optional<int>.Unset, i => reported.Add(i), Tab(label, TextBlock("a")), Tab("B", TextBlock("b"))));
        var tabs = host.All<TabControl>()[0];
        tabs.SelectedIndex = 1;
        Assert.Equal([1], reported);

        label = "A!"; // an update leaves the user's selection alone
        host.Render();
        Assert.Equal(1, tabs.SelectedIndex);
    }

    [AvaloniaFact]
    public void TabView_tabs_added_and_removed_keep_the_others_and_tear_down_the_removed_content()
    {
        var log = new List<string>();
        var b = Counter("b", log);
        var three = false;
        var host = new TestHost(() => three
            ? TabView(Tab("A", TextBlock("a")), Tab("B", Component(b)), Tab("C", TextBlock("c")))
            : TabView(Tab("A", TextBlock("a"))));
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
    public void TabView_tab_contents_are_torn_down_with_the_tabs()
    {
        var log = new List<string>();
        var a = Counter("a", log);
        var b = Counter("b", log);
        var host = new TestHost(() => VStack(TabView(Tab("A", Component(a)), Tab("B", Component(b)))));

        host.Unmount();
        Assert.Equal(["cleanup b", "cleanup a"], log);
    }

    [AvaloniaFact]
    public void TabView_does_not_report_the_selection_of_a_list_inside_a_tab()
    {
        // SelectionChanged bubbles on Avalonia: a ComboBox's change also reaches the TabControl.
        var reported = new List<int>();
        var host = new TestHost(() => TabView(0, i => reported.Add(i), Tab("A", ComboBox(["x", "y"], 0)), Tab("B", TextBlock("b"))));
        var tabs = host.All<TabControl>()[0];
        var combo = Assert.IsType<ComboBox>(Items(tabs)[0].Content);

        combo.SelectedIndex = 1;
        Assert.Empty(reported);
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [AvaloniaFact]
    public void TabView_strip_placement_is_written_and_cleared()
    {
        Dock? placement = Dock.Left;
        var host = new TestHost(() => TabView(Tab("A", TextBlock("a"))) with { TabStripPlacement = placement });
        var tabs = host.All<TabControl>()[0];
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);

        placement = null;
        host.Render();
        Assert.Equal(new TabControl().TabStripPlacement, tabs.TabStripPlacement);
    }
}
