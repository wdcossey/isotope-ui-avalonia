using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Keyed reorders move each row with Avalonia's <c>Controls.Move</c> (#23, Spike I), so in a window a moved row stays in
/// the visual and logical trees: it isn't detached, its focused control keeps focus, and the panel lays it out in its
/// new place. Upstream's RemoveAt + Insert took the row out of both trees and put it back.
/// </summary>
public sealed class KeyedMoveTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private TestHost Show(Func<Element> render)
    {
        var host = new TestHost(render);
        var w = new Window { Width = 400, Height = 600, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }

    // A keyed list of component rows: each row is the component's Border around a TextBlock and a Button.
    private TestHost ShowRows(Func<string[]> keys) => Show(() => VStack(keys().Select(key =>
        (Element)Component(_ => HStack(TextBlock(key), Button(key))).WithKey(key)).ToArray()));

    private static StackPanel List(TestHost host) => Assert.IsType<StackPanel>(host.Root.Child);

    private static string KeyOf(Control row) => ((TextBlock)((StackPanel)((Border)row).Child!).Children[0]).Text!;

    private static Button ButtonOf(Control row) => (Button)((StackPanel)((Border)row).Child!).Children[1];

    private static Control Row(TestHost host, string key) => List(host).Children.Single(row => KeyOf(row) == key);

    // Counts each row's DetachedFromVisualTree, and records the list's collection edits, from now on.
    private static (Func<int> Detached, List<NotifyCollectionChangedEventArgs> Edits) Watch(Panel panel)
    {
        int detached = 0;
        foreach (var child in panel.Children) child.DetachedFromVisualTree += (_, _) => detached++;
        var edits = new List<NotifyCollectionChangedEventArgs>();
        panel.Children.CollectionChanged += (_, e) => edits.Add(e);
        return (() => detached, edits);
    }

    private static void Layout(TestHost host)
    {
        Dispatcher.UIThread.RunJobs();
        ((Window)host.Root.Parent!).UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Reversing_keyed_rows_in_a_window_moves_them_without_detaching_any()
    {
        string[] keys = ["a", "b", "c", "d", "e", "f"];
        var host = ShowRows(() => keys);
        var before = List(host).Children.ToDictionary(KeyOf);
        var (detached, edits) = Watch(List(host));

        keys = ["f", "e", "d", "c", "b", "a"];
        host.Render();
        Layout(host);

        Assert.Equal(keys, List(host).Children.Select(KeyOf));
        Assert.All(keys, key => Assert.Same(before[key], Row(host, key)));
        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal(NotifyCollectionChangedAction.Move, e.Action));
        Assert.Equal(0, detached());
    }

    [AvaloniaTheory]
    [InlineData("first to last")]
    [InlineData("last to first")]
    public void A_focused_control_in_a_moved_row_keeps_focus(string reorder)
    {
        string[] keys = ["a", "b", "c", "d", "e"];
        var host = ShowRows(() => keys);
        string moved = reorder == "first to last" ? "a" : "e";
        var button = ButtonOf(Row(host, moved));
        Assert.True(button.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsFocused);

        keys = reorder == "first to last" ? [.. keys[1..], keys[0]] : [keys[^1], .. keys[..^1]];
        host.Render();
        Layout(host);

        Assert.Equal(keys, List(host).Children.Select(KeyOf));
        Assert.Same(button, ButtonOf(Row(host, moved)));
        Assert.True(button.IsFocused);
        Assert.Same(button, TopLevel.GetTopLevel(button)!.FocusManager!.GetFocusedElement());
    }

    // A move to a later index is the direction a Move contract is easiest to get wrong (the final index, or the index
    // before the removal?); the edit itself is checked, so each case does move in the direction it names.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_move_to_a_later_or_an_earlier_index_keeps_the_visual_logical_and_arranged_order(bool toLater)
    {
        string[] keys = ["a", "b", "c", "d", "e"];
        var host = ShowRows(() => keys);
        var panel = List(host);
        var before = panel.Children.ToDictionary(KeyOf);
        var (detached, edits) = Watch(panel);

        // Move b after d, or d before b: one row moves, the others keep their order.
        keys = toLater ? ["a", "c", "d", "b", "e"] : ["a", "d", "b", "c", "e"];
        host.Render();
        Layout(host);

        var move = Assert.Single(edits);
        Assert.Equal(NotifyCollectionChangedAction.Move, move.Action);
        Assert.Equal(toLater, move.NewStartingIndex > move.OldStartingIndex);
        Assert.Equal(0, detached());

        // Each key's row is the control it had (a wrong index would leave the texts right and the rows swapped: the
        // reconcile then updates each row in place with another row's element).
        var children = panel.Children.ToList();
        Assert.Equal(keys, children.Select(KeyOf));
        Assert.All(keys, key => Assert.Same(before[key], Row(host, key)));
        Assert.Equal(children, panel.GetVisualChildren().Cast<Control>());
        Assert.Equal(children, ((ILogical)panel).LogicalChildren.Cast<Control>());
        var tops = children.Select(row => row.Bounds.Top).ToList();
        Assert.Equal(tops.Order(), tops);
        Assert.Equal(tops.Count, tops.Distinct().Count());
    }

    [AvaloniaFact]
    public void A_FlexElement_reorder_lays_out_in_the_new_order_without_detaching()
    {
        // Widths that differ, so each order puts the boxes at its own offsets.
        var widths = new Dictionary<string, double> { ["a"] = 10, ["b"] = 20, ["c"] = 40, ["d"] = 80 };
        string[] keys = ["a", "b", "c", "d"];
        var host = Show(() => new FlexElement(keys.Select(key =>
            (Element)TextBlock(key).WithKey(key).WithModifiers(m => m with { Width = widths[key], Height = 20 })).ToArray())
        {
            Direction = FlexDirection.Row,
        });
        var panel = Assert.IsType<FlexPanel>(host.Root.Child);
        var before = panel.Children.Cast<TextBlock>().ToDictionary(box => box.Text!);
        var (detached, edits) = Watch(panel);

        keys = ["d", "b", "a", "c"]; // d to the front and a after b
        host.Render();
        Layout(host);

        Assert.NotEmpty(edits);
        Assert.All(edits, e => Assert.Equal(NotifyCollectionChangedAction.Move, e.Action));
        Assert.Equal(0, detached());
        var boxes = panel.Children.Cast<TextBlock>().ToList();
        Assert.Equal(keys, boxes.Select(box => box.Text));
        Assert.All(boxes, box => Assert.Same(before[box.Text!], box));
        double left = 0;
        foreach (var box in boxes)
        {
            Assert.Equal(left, box.Bounds.Left);
            left += widths[box.Text!];
        }
    }
}
