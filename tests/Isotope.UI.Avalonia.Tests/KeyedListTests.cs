using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A keyed list of stateful rows through upstream's ChildReconciler and KeyedListDiff (#2): insert, remove and
/// reorder keep each row's controls (identity) and its hook state, and unmount only the removed rows.
/// </summary>
public class KeyedListTests
{
    private sealed class Rows
    {
        public string[] Keys = [];
        public readonly List<string> Log = new();
        public readonly Dictionary<string, Action> Increment = new();
    }

    // Each row has its own count (UseState), an effect whose cleanup logs the row's removal, and an uncontrolled text
    // box that keeps what was typed into it.
    private static TestHost Host(Rows rows) => new(() => VStack(rows.Keys.Select(key => (Element)Component(ctx =>
    {
        var (count, setCount) = ctx.UseState(0);
        rows.Increment[key] = () => setCount(count + 1);
        ctx.UseEffect(() => () => rows.Log.Add("unmount " + key));
        return VStack(Text($"{key}={count}"), new TextBoxElement());
    }, key)).ToArray()));

    private static Dictionary<string, (Border Row, TextBox Box)> Controls(TestHost host)
    {
        var panel = Assert.IsType<StackPanel>(host.Root.Child);
        return panel.Children.Cast<Border>().ToDictionary(
            row => ((TextBlock)((StackPanel)row.Child!).Children[0]).Text!.Split('=')[0],
            row => (row, (TextBox)((StackPanel)row.Child!).Children[1]));
    }

    private static List<string> Order(TestHost host) => host.Texts().Select(t => t!).ToList();

    [AvaloniaFact]
    public void Reorder_moves_each_rows_controls_and_keeps_its_state()
    {
        var rows = new Rows { Keys = ["a", "b", "c", "d"] };
        var host = Host(rows);
        rows.Increment["b"]();
        host.Flush();
        rows.Increment["d"]();
        host.Flush();
        var before = Controls(host);
        foreach (var (key, (_, box)) in before) box.Text = "typed " + key;
        Dispatcher.UIThread.RunJobs();

        rows.Keys = ["d", "b", "a", "c"];
        host.Render();

        Assert.Equal(["d=1", "b=1", "a=0", "c=0"], Order(host));
        var after = Controls(host);
        foreach (var key in rows.Keys)
        {
            Assert.Same(before[key].Row, after[key].Row);
            Assert.Same(before[key].Box, after[key].Box);
            Assert.Equal("typed " + key, after[key].Box.Text);
        }
        Assert.Empty(rows.Log);
    }

    [AvaloniaFact]
    public void Reversing_the_list_keeps_every_row()
    {
        var rows = new Rows { Keys = ["a", "b", "c", "d", "e"] };
        var host = Host(rows);
        var before = Controls(host);

        rows.Keys = ["e", "d", "c", "b", "a"];
        host.Render();

        Assert.Equal(["e=0", "d=0", "c=0", "b=0", "a=0"], Order(host));
        var after = Controls(host);
        Assert.All(rows.Keys, key => Assert.Same(before[key].Row, after[key].Row));
        Assert.Equal(5, host.Reconciler.ComponentNodeCount);
        Assert.Empty(rows.Log);
    }

    [AvaloniaFact]
    public void Insert_in_the_middle_mounts_only_the_new_row()
    {
        var rows = new Rows { Keys = ["a", "c"] };
        var host = Host(rows);
        rows.Increment["c"]();
        host.Flush();
        var before = Controls(host);

        rows.Keys = ["a", "b", "c"];
        host.Render();

        Assert.Equal(["a=0", "b=0", "c=1"], Order(host));
        var after = Controls(host);
        Assert.Same(before["a"].Row, after["a"].Row);
        Assert.Same(before["c"].Row, after["c"].Row);
        Assert.Equal(3, host.Reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void Remove_unmounts_only_the_removed_rows_and_a_state_change_after_it_reaches_the_right_row()
    {
        var rows = new Rows { Keys = ["a", "b", "c", "d"] };
        var host = Host(rows);
        var before = Controls(host);

        rows.Keys = ["a", "d"];
        host.Render();

        Assert.Equal(["a=0", "d=0"], Order(host));
        Assert.Equal(["unmount b", "unmount c"], rows.Log.Order());
        Assert.Same(before["d"].Row, Controls(host)["d"].Row);
        Assert.Equal(2, host.Reconciler.ComponentNodeCount);

        rows.Increment["d"]();
        host.Flush();
        Assert.Equal(["a=0", "d=1"], Order(host));
    }

    [AvaloniaFact]
    public void Insert_remove_and_move_in_one_render()
    {
        var rows = new Rows { Keys = ["a", "b", "c", "d", "e"] };
        var host = Host(rows);
        rows.Increment["e"]();
        host.Flush();
        var before = Controls(host);

        rows.Keys = ["e", "x", "a", "d"]; // b and c removed, x inserted, e moved to the front
        host.Render();

        Assert.Equal(["e=1", "x=0", "a=0", "d=0"], Order(host));
        var after = Controls(host);
        foreach (var key in new[] { "a", "d", "e" }) Assert.Same(before[key].Row, after[key].Row);
        Assert.Equal(["unmount b", "unmount c"], rows.Log.Order());
    }
}
