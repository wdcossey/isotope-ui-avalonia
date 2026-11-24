using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// ComboBox and ListBox (#19): string items through the generated items host (Avalonia's ItemCollection, adapted by
/// ItemsList.Of), updated in place, and the controlled selection, whose engine writes don't echo.
/// </summary>
public class ItemsControlTests
{
    private static List<object?> ItemsOf(ItemsControl c) => c.Items.Cast<object?>().ToList();

    [AvaloniaFact]
    public void ComboBox_mounts_its_items_with_the_selection_landing_in_the_populated_list()
    {
        var host = new TestHost(() => ComboBox(["a", "b", "c"], 2) with { PlaceholderText = "Pick", IsEditable = true });
        var combo = host.All<ComboBox>()[0];
        Assert.Equal(["a", "b", "c"], ItemsOf(combo));
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal("c", combo.SelectedItem);
        Assert.Equal(("Pick", true), (combo.PlaceholderText, combo.IsEditable));
    }

    [AvaloniaFact]
    public void ComboBox_items_are_updated_in_place()
    {
        string[] items = ["a", "b", "c"];
        var host = new TestHost(() => ComboBox(items));
        var combo = host.All<ComboBox>()[0];

        items = ["a", "x", "c", "d"];
        host.Render();
        Assert.Same(combo, host.All<ComboBox>()[0]);
        Assert.Equal(["a", "x", "c", "d"], ItemsOf(combo));

        items = ["d"];
        host.Render();
        Assert.Equal(["d"], ItemsOf(combo));
    }

    [AvaloniaFact]
    public void ComboBox_user_selection_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<int>();
        Action<int>? set = null;
        string[] items = ["a", "b", "c"];
        var host = new TestHost(() => Component(ctx =>
        {
            var (index, setIndex) = ctx.UseState(0);
            set = setIndex;
            return VStack(TextBlock("state " + index), ComboBox(items, index, v => { reported.Add(v); setIndex(v); }));
        }));
        var combo = host.All<ComboBox>()[0];

        combo.SelectedIndex = 1; // as choosing from the list does
        host.Flush();
        Assert.Equal([1], reported);
        Assert.Equal(["state 1"], host.Texts());

        set!(2);
        host.Flush();
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal([1], reported);

        combo.SelectedIndex = 0;
        Assert.Equal([1, 0], reported);
    }

    [AvaloniaFact]
    public void ComboBox_placeholder_set_back_to_null_and_an_unset_drop_down_height_keep_the_defaults()
    {
        var set = true;
        var host = new TestHost(() => set
            ? ComboBox(["a"]) with { PlaceholderText = "Pick", MaxDropDownHeight = 120 }
            : ComboBox(["a"]));
        var combo = host.All<ComboBox>()[0];
        Assert.Equal(("Pick", 120.0), (combo.PlaceholderText, combo.MaxDropDownHeight));

        set = false;
        host.Render();
        var defaults = new ComboBox();
        Assert.Equal(defaults.PlaceholderText, combo.PlaceholderText);
        Assert.Equal(120.0, combo.MaxDropDownHeight); // NaN means "leave it", as upstream's

        var fresh = new TestHost(() => ComboBox(["a"])).All<ComboBox>()[0];
        Assert.Equal(defaults.MaxDropDownHeight, fresh.MaxDropDownHeight);
    }

    [AvaloniaFact]
    public void ComboBox_drop_down_events_reach_their_callbacks()
    {
        var events = new List<string>();
        var host = new TestHost(() => ComboBox(["a", "b"]) with
        {
            OnDropDownOpened = () => events.Add("opened"),
            OnDropDownClosed = () => events.Add("closed"),
        });
        var window = new Window { Content = host.Root };
        window.Show();
        try
        {
            var combo = host.All<ComboBox>()[0];
            combo.IsDropDownOpen = true;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = false;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(["opened", "closed"], events);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ComboBox_selection_and_drop_down_callbacks_work_together()
    {
        // The three share the control's one event payload: none replaces another's.
        var events = new List<string>();
        var host = new TestHost(() => ComboBox(["a", "b"], 0, i => events.Add("selected " + i)) with
        {
            OnDropDownOpened = () => events.Add("opened"),
            OnDropDownClosed = () => events.Add("closed"),
        });
        var window = new Window { Content = host.Root };
        window.Show();
        try
        {
            var combo = host.All<ComboBox>()[0];
            combo.IsDropDownOpen = true;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            combo.SelectedIndex = 1;
            combo.IsDropDownOpen = false;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(["opened", "selected 1", "closed"], events);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ComboBox_callback_added_on_a_later_render_is_wired()
    {
        var events = new List<string>();
        string[] items = ["a", "b"];
        Action<int> selected = i => events.Add("selected " + i);
        var withOpened = false;
        var host = new TestHost(() => withOpened
            ? ComboBox(items, 0, selected) with { OnDropDownOpened = () => events.Add("opened") }
            : ComboBox(items, 0, selected));
        var window = new Window { Content = host.Root };
        window.Show();
        try
        {
            withOpened = true; // otherwise equal: still not skipped, so the new callback is subscribed
            host.Render();
            host.All<ComboBox>()[0].IsDropDownOpen = true;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(["opened"], events);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ListBox_mounts_its_items_and_selection_and_updates_the_items_in_place()
    {
        string[] items = ["a", "b", "c"];
        var host = new TestHost(() => ListBox(items, 1));
        var list = host.All<ListBox>()[0];
        Assert.Equal(["a", "b", "c"], ItemsOf(list));
        Assert.Equal(1, list.SelectedIndex);

        items = ["c", "b", "a"];
        host.Render();
        Assert.Same(list, host.All<ListBox>()[0]);
        Assert.Equal(["c", "b", "a"], ItemsOf(list));
        Assert.Equal(1, list.SelectedIndex);
    }

    [AvaloniaFact]
    public void ListBox_user_selection_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<int>();
        Action<int>? set = null;
        string[] items = ["a", "b", "c"];
        var host = new TestHost(() => Component(ctx =>
        {
            var (index, setIndex) = ctx.UseState(-1);
            set = setIndex;
            return VStack(TextBlock("state " + index), ListBox(items, index, v => { reported.Add(v); setIndex(v); }));
        }));
        var list = host.All<ListBox>()[0];
        Assert.Equal(-1, list.SelectedIndex);

        list.SelectedIndex = 2;
        host.Flush();
        Assert.Equal([2], reported);
        Assert.Equal(["state 2"], host.Texts());

        set!(0);
        host.Flush();
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal([2], reported);

        list.SelectedIndex = 1;
        Assert.Equal([2, 1], reported);
    }

    [AvaloniaFact]
    public void ListBox_multiple_selection_reports_every_selected_index()
    {
        var snapshots = new List<IReadOnlyList<int>>();
        var host = new TestHost(() => new ListBoxElement(["a", "b", "c"])
        {
            SelectionMode = SelectionMode.Multiple,
            OnSelectionChanged = snapshots.Add,
        });
        var list = host.All<ListBox>()[0];
        Assert.Equal(SelectionMode.Multiple, list.SelectionMode);

        list.Selection.Select(2);
        list.Selection.Select(0);
        Assert.Equal([0, 2], snapshots[^1]);
    }

    [AvaloniaFact]
    public void ListBox_multiple_selection_tells_equal_items_apart()
    {
        var snapshots = new List<IReadOnlyList<int>>();
        var host = new TestHost(() => new ListBoxElement(["x", "y", "x"])
        {
            SelectionMode = SelectionMode.Multiple,
            OnSelectionChanged = snapshots.Add,
        });
        host.All<ListBox>()[0].Selection.Select(2);
        Assert.Equal([2], snapshots[^1]);
    }
}
