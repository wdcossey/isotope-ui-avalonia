using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The list box's long-list numbers in the components guide and FINDINGS (#94, #102), three runs each, in a 300-high list in
/// a headless window: mount, show, an equal render and a keyed reverse of 1,000 and 10,000 element rows, and 10,000
/// strings (<see cref="Long_list"/>); what 10,000 component rows mount, and the state they keep across a keyed reverse and
/// a scroll away and back (<see cref="Row_state"/>); and where keyboard focus in a row goes across an equal render, a keyed
/// move and a scroll (<see cref="Row_focus"/>). Not tests: they run only when ISOTOPE_MEASURE names the file to append the
/// lines to (<c>ISOTOPE_MEASURE=&lt;file&gt; dotnet test tests/Isotope.UI.Avalonia.Tests -f net10.0 --filter ListBoxMeasurements</c>).
/// Each reads the rows through the realised containers, so it measures either way of mounting the items.
/// </summary>
public sealed class ListBoxMeasurements
{
    public static bool Measuring => Environment.GetEnvironmentVariable("ISOTOPE_MEASURE") is { Length: > 0 };

    private static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("ISOTOPE_MEASURE")!, s + "\n");

    private static Window Show(TestHost host)
    {
        var w = new Window { Width = 400, Height = 400, Content = host.Root, SizeToContent = SizeToContent.Manual };
        w.Show();
        Layout(w);
        return w;
    }

    private static void Layout(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    // The control a realised container shows, or null.
    private static Control? Row(ListBox list, int index) => (list.ContainerFromIndex(index) as ContentControl)?.Content as Control;

    private static string? FirstText(Control? c) => c switch
    {
        TextBlock t => t.Text,
        Decorator d => FirstText(d.Child),
        Panel p => p.Children.Count > 0 ? FirstText(p.Children[0]) : null,
        _ => null,
    };

    private static void ScrollTo(Window w, ListBox list, int index)
    {
        list.ScrollIntoView(index);
        Layout(w);
    }

    [AvaloniaFact(SkipUnless = nameof(Measuring), Skip = "a measurement: set ISOTOPE_MEASURE to the output file")]
    public void Long_list()
    {
        foreach (var n in new[] { 1000, 10000 })
        for (int run = 0; run < 3; run++)
        {
            var keys = Enumerable.Range(0, n).Select(i => "k" + i).ToArray();
            var sw = Stopwatch.StartNew();
            var host = new TestHost(() => ListBox(keys.Select(k => (Element?)HStack(TextBlock(k), TextBlock("x")).WithKey(k)).ToArray()).Height(300));
            var mount = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var w = Show(host);
            var show = sw.Elapsed.TotalMilliseconds;
            var list = (ListBox)host.Root.Child!;
            var realized = list.GetRealizedContainers().Count();
            var top = Row(list, 0);
            sw.Restart();
            host.Render();
            var same = sw.Elapsed.TotalMilliseconds;
            keys = Enumerable.Reverse(keys).ToArray();
            sw.Restart();
            host.Render();
            var rev = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            Layout(w);
            var layout = sw.Elapsed.TotalMilliseconds;
            var firstAfter = FirstText(Row(list, 0));
            ScrollTo(w, list, n - 1);
            var keptTop = ReferenceEquals(Row(list, n - 1), top);
            Log($"n={n} run={run} mount={mount:F1}ms show={show:F1}ms realized={realized} render-same={same:F1}ms reverse={rev:F1}ms layout-after={layout:F1}ms firstAfter={firstAfter} realizedAfter={list.GetRealizedContainers().Count()} k0ControlKept={keptTop}");
            w.Close(); Dispatcher.UIThread.RunJobs();
        }
        foreach (var n in new[] { 10000 })
        for (int run = 0; run < 3; run++)
        {
            var strings = Enumerable.Range(0, n).Select(i => "s" + i).ToArray();
            var sw = Stopwatch.StartNew();
            var host = new TestHost(() => ListBox(strings).Height(300));
            var mount = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var w = Show(host);
            var show = sw.Elapsed.TotalMilliseconds;
            var list = (ListBox)host.Root.Child!;
            Log($"strings n={n} run={run} mount={mount:F1}ms show={show:F1}ms realized={list.GetRealizedContainers().Count()}");
            w.Close(); Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact(SkipUnless = nameof(Measuring), Skip = "a measurement: set ISOTOPE_MEASURE to the output file")]
    public void Row_state()
    {
        const int n = 10000;
        for (int run = 0; run < 3; run++)
        {
            var keys = Enumerable.Range(0, n).Select(i => "k" + i).ToArray();
            var setters = new Dictionary<string, Action<int>>();
            int mounts = 0, cleanups = 0;
            Element RowOf(string key) => Component(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                setters[key] = setCount;
                ctx.UseEffect(() =>
                {
                    mounts++;
                    return () => cleanups++;
                });
                return TextBlock(key + " " + count);
            }).WithKey(key);
            var sw = Stopwatch.StartNew();
            var host = new TestHost(() => ListBox(keys.Select(RowOf).ToArray()).Height(300));
            var mount = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var w = Show(host);
            var show = sw.Elapsed.TotalMilliseconds;
            var list = (ListBox)host.Root.Child!;
            var mountedShown = mounts;
            var k1 = Row(list, 1);
            setters["k1"](5);
            host.Flush();
            Layout(w);
            var k1Text = FirstText(Row(list, 1));

            // A keyed reverse: k1 goes to n - 2, out of view; scroll to it.
            keys = Enumerable.Reverse(keys).ToArray();
            sw.Restart();
            host.Render();
            Layout(w);
            var reverse = sw.Elapsed.TotalMilliseconds;
            var mountsAfterReverse = mounts;
            ScrollTo(w, list, n - 2);
            var k1AfterReverse = FirstText(Row(list, n - 2));
            var k1SameControl = ReferenceEquals(Row(list, n - 2), k1);

            // A scroll away and back: k1 out of view and in again.
            ScrollTo(w, list, n / 2);
            var k1RealisedAway = list.ContainerFromIndex(n - 2) is not null;
            ScrollTo(w, list, n - 2);
            var k1AfterScroll = FirstText(Row(list, n - 2));
            var k1SameAfterScroll = ReferenceEquals(Row(list, n - 2), k1);
            Log($"state n={n} run={run} mount={mount:F1}ms show={show:F1}ms mountedShown={mountedShown} k1={k1Text} reverse={reverse:F1}ms mountsAfterReverse={mountsAfterReverse} " +
                $"k1AfterReverse={k1AfterReverse} sameControl={k1SameControl} k1RealisedWhileAway={k1RealisedAway} k1AfterScroll={k1AfterScroll} sameControlAfterScroll={k1SameAfterScroll} " +
                $"mountsEnd={mounts} cleanupsEnd={cleanups}");
            host.Unmount();
            Log($"state n={n} run={run} cleanupsAfterUnmount={cleanups}");
            w.Close(); Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact(SkipUnless = nameof(Measuring), Skip = "a measurement: set ISOTOPE_MEASURE to the output file")]
    public void Row_focus()
    {
        const int n = 1000;
        var keys = Enumerable.Range(0, n).Select(i => "k" + i).ToArray();
        var host = new TestHost(() => ListBox(keys.Select(k => (Element?)HStack(TextBlock(k), TextBox("")).WithKey(k)).ToArray()).Height(300));
        var w = Show(host);
        var list = (ListBox)host.Root.Child!;
        TextBox? BoxOf(int index) => (Row(list, index) as Panel)?.Children.OfType<TextBox>().Single();
        var box = BoxOf(2)!;
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        var focusedAtStart = box.IsFocused;

        host.Render();
        Layout(w);
        var afterEqualRender = box.IsFocused;

        // The focused row scrolled out of view and back.
        ScrollTo(w, list, n / 2);
        var whileAway = box.IsFocused;
        var stillRealisedAway = list.ContainerFromIndex(2) is not null;
        ScrollTo(w, list, 0);
        var afterScroll = box.IsFocused;
        var sameBox = ReferenceEquals(BoxOf(2), box);

        // The focused row moved by key (k2 to the front).
        keys = [keys[2], .. keys.Where((_, i) => i != 2)];
        host.Render();
        Layout(w);
        var afterMove = box.IsFocused;
        var movedSameBox = ReferenceEquals(BoxOf(0), box);
        var focused = TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement();
        Log($"focus n={n} start={focusedAtStart} equalRender={afterEqualRender} scrolledAway={whileAway} containerKeptAway={stillRealisedAway} " +
            $"backInView={afterScroll} sameBox={sameBox} keyedMove={afterMove} movedSameBox={movedSameBox} focusedAfterMove={focused?.GetType().Name ?? "none"}");
        w.Close(); Dispatcher.UIThread.RunJobs();
    }
}
