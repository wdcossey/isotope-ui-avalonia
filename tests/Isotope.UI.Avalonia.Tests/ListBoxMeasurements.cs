using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The list box's long-list numbers in FINDINGS (#94): mount, show, an equal render and a keyed reverse of 1,000 and 10,000
/// element rows, the containers realised and reused, and 10,000 strings, three runs each, in a 300-high list in a headless
/// window. Not a test: it runs only when ISOTOPE_MEASURE names the file to append the lines to
/// (<c>ISOTOPE_MEASURE=&lt;file&gt; dotnet test tests/Isotope.UI.Avalonia.Tests -f net10.0 --filter ListBoxMeasurements</c>).
/// </summary>
public sealed class ListBoxMeasurements
{
    public static bool Measuring => Environment.GetEnvironmentVariable("ISOTOPE_MEASURE") is { Length: > 0 };

    private static void Log(string s) => File.AppendAllText(Environment.GetEnvironmentVariable("ISOTOPE_MEASURE")!, s + "\n");

    private static Window Show(TestHost host)
    {
        var w = new Window { Width = 400, Height = 400, Content = host.Root, SizeToContent = SizeToContent.Manual };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return w;
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
            var items0 = list.Items.Cast<Control>().ToArray();
            var cont0 = list.GetRealizedContainers().ToArray();
            var content0 = cont0.Select(c => ((ContentControl)c).Content).ToArray();
            sw.Restart();
            host.Render();
            var same = sw.Elapsed.TotalMilliseconds;
            keys = keys.Reverse().ToArray();
            int detached = 0;
            foreach (var c in items0) c.DetachedFromVisualTree += (_, _) => detached++;
            int logicalDetached = 0;
            foreach (var c in items0) c.DetachedFromLogicalTree += (_, _) => logicalDetached++;
            sw.Restart();
            host.Render();
            var rev = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var layout = sw.Elapsed.TotalMilliseconds;
            var items1 = list.Items.Cast<Control>().ToArray();
            var allSame = items0.Reverse().SequenceEqual(items1);
            var cont1 = list.GetRealizedContainers().ToArray();
            var keptContainers = cont1.Count(c => cont0.Contains(c));
            Log($"n={n} run={run} mount={mount:F1}ms show={show:F1}ms realized={realized} render-same={same:F1}ms reverse={rev:F1}ms layout-after={layout:F1}ms sameControls={allSame} realizedAfter={cont1.Length} containersReused={keptContainers} rowVisualDetached={detached} rowLogicalDetached={logicalDetached}");
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
}
