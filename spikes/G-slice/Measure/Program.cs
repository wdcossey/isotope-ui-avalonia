// Run from the repo root: dotnet run -c Release --project spikes/G-slice/Measure [-- <rows> <runs>]
// Spike G (#2): timings of the vertical slice on Avalonia 12, headless (no window, no rendering): a keyed list of
// stateful rows through upstream's ChildReconciler and KeyedListDiff, and one row's state change. Each case runs
// --runs times (default 7) on a fresh tree after a warm-up; the median is printed, with how many row controls
// survived the change (identity kept).
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Isotope.UI.Core;

var rows = args.Length > 0 ? int.Parse(args[0]) : 1000;
var runs = args.Length > 1 ? int.Parse(args[1]) : 7;

var session = HeadlessUnitTestSession.StartNew(typeof(MeasureApp));
await session.Dispatch(() =>
{
    Console.WriteLine($"rows={rows} runs={runs} (median ms; rows reused)");
    Case("mount", rows, _ => { }, keys => keys);
    Case("re-render, same keys", rows, _ => { }, keys => keys);
    Case("reverse", rows, _ => { }, keys => keys.Reverse().ToArray());
    Case("move last to first", rows, _ => { }, keys => [keys[^1], .. keys[..^1]]);
    Case("insert 1 in the middle", rows, _ => { }, keys => [.. keys[..(keys.Length / 2)], "new", .. keys[(keys.Length / 2)..]]);
    Case("remove every other", rows, _ => { }, keys => keys.Where((_, i) => i % 2 == 0).ToArray());
    Case("shuffle (seeded)", rows, _ => { }, keys => keys.OrderBy(k => (k.GetHashCode() * 31) ^ 0x5bd1e995).ToArray());
    StateCase(rows);
    return 0;
}, CancellationToken.None);
// The headless session's dispatcher thread keeps the process alive (disposing the session waits on it), so exit.
Environment.Exit(0);

void Case(string name, int n, Action<Host> before, Func<string[], string[]> change)
{
    var times = new List<double>();
    int reused = 0;
    for (int run = -1; run < runs; run++)
    {
        var keys = Enumerable.Range(0, n).Select(i => "k" + i).ToArray();
        var sw = Stopwatch.StartNew();
        var host = new Host(() => keys);
        if (name == "mount")
        {
            sw.Stop();
            if (run >= 0) times.Add(sw.Elapsed.TotalMilliseconds);
            continue;
        }
        before(host);
        var rowsBefore = host.RowControls();
        keys = change(keys);
        sw.Restart();
        host.Render();
        sw.Stop();
        var rowsAfter = host.RowControls();
        reused = rowsAfter.Count(kv => rowsBefore.TryGetValue(kv.Key, out var c) && ReferenceEquals(c, kv.Value));
        if (run >= 0) times.Add(sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{name,-26} {Median(times),8:F2} ms   reused {(name == "mount" ? "-" : reused.ToString())}");
}

void StateCase(int n)
{
    var times = new List<double>();
    for (int run = -1; run < runs; run++)
    {
        var keys = Enumerable.Range(0, n).Select(i => "k" + i).ToArray();
        var host = new Host(() => keys);
        var sw = Stopwatch.StartNew();
        host.Increment[keys[n / 2]]();
        host.Flush();
        sw.Stop();
        if (run >= 0) times.Add(sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{"one row's state change",-26} {Median(times),8:F2} ms   (a root pass)");
}

static double Median(List<double> xs) { xs.Sort(); return xs[xs.Count / 2]; }

sealed class MeasureApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MeasureApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

// A root host like the tests': a state change asks for a root pass, run by Flush. The rows are memoized function
// components (keyed MemoElements with no dependencies), so a parent pass skips them unless their own state changed,
// as an app would write a long list.
sealed class Host
{
    private readonly Func<string[]> _keys;
    private Element? _last;
    private bool _pending;
    public Reconciler Reconciler { get; } = new();
    public Border Root { get; } = new();
    public Dictionary<string, Action> Increment { get; } = new();

    public Host(Func<string[]> keys) { _keys = keys; Render(); }

    public void Render()
    {
        var element = new StackElement(Orientation.Vertical, _keys().Select(Row).ToArray());
        var next = Reconciler.Reconcile(_last, element, Root.Child, () => _pending = true);
        if (!ReferenceEquals(next, Root.Child)) Root.Child = next;
        _last = element;
    }

    public void Flush()
    {
        while (_pending) { _pending = false; Render(); }
    }

    private Element Row(string key) => new MemoElement(ctx =>
    {
        var (count, setCount) = ctx.UseState(0);
        Increment[key] = () => setCount(count + 1);
        return new StackElement(Orientation.Horizontal, [new TextBlockElement($"{key}: {count}"), new ButtonElement("+", () => setCount(count + 1))]);
    }) { Key = key };

    public Dictionary<string, Control> RowControls()
        => ((StackPanel)Root.Child!).Children.ToDictionary(c => ((TextBlock)((StackPanel)((Border)c).Child!).Children[0]).Text!.Split(':')[0], c => c);
}
