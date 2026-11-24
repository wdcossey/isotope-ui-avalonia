// Run from the repo root: dotnet run -c Release --project spikes/I-pooling/Measure [-- <rows> <runs>]
// Spike I (#14): is control pooling worth porting to Avalonia, and does Avalonia's Controls.Move beat the remove and
// re-insert that upstream's PanelChildCollection.Move does? Headless (no OS window), Skia and the Fluent theme, as the
// library's tests. Every case runs <runs> times (default 7) on a fresh tree after a warm-up and prints medians: the
// change, the change plus a layout pass, and the bytes allocated on this thread over both. Each case runs detached (no
// window, as Spike G's numbers) and in a headless window, where attach, styling, templates and layout happen.
//   1. Engine: mount/unmount churn and keyed reorders through the library's reconciler (upstream's ChildReconciler).
//   2. Avalonia: the same row controls created fresh against rented from a pool of used, reset controls (no library
//      change: this is what a pool behind RentControl could save at most).
//   3. Avalonia: the moves the engine issues for each reorder, recorded from the panel's collection events, replayed
//      as RemoveAt + Insert (vendored PanelChildCollection.Move) and as Controls.Move.
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Isotope.UI.Layout;

int rows = 1000, runs = 7;
if ((args.Length > 0 && !int.TryParse(args[0], out rows)) || (args.Length > 1 && !int.TryParse(args[1], out runs)) || rows < 64 || runs < 1)
{
    Console.Error.WriteLine("usage: [<rows> (at least 64)] [<runs> (at least 1)]");
    return 1;
}

var session = HeadlessUnitTestSession.StartNew(typeof(MeasureApp));
// A first pass of every case, not printed, so the JIT has tiered up the code paths before any case is timed (the first
// case timed in a fresh process otherwise ran several times slower than the same work later).
var failures = await session.Dispatch(() => new Bench(rows, 1, quiet: true).Run() + new Bench(rows, runs, quiet: false).Run(),
    CancellationToken.None);
// The headless session's dispatcher thread keeps the process alive (disposing the session waits on it), so exit.
Environment.Exit(failures == 0 ? 0 : 2);
return 0;

sealed class MeasureApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MeasureApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Medians of one case: the change, the change plus the layout pass, KB allocated and gen0 GCs over both.</summary>
readonly record struct Stats(double ChangeMs, double TotalMs, double Kb, double Gen0);

/// <summary>A move the engine made: the child at From ends up at To (RemoveAt(From) then Insert(To), as upstream).</summary>
readonly record struct MoveOp(int From, int To);

sealed class Bench(int n, int runs, bool quiet)
{
    private int _failures;

    private void Out(string line = "")
    {
        if (!quiet) Console.WriteLine(line);
    }
    private static readonly string[] Where = ["detached", "window"];

    public int Run()
    {
        Out($"rows={n} runs={runs}; medians: change ms | change + layout + posted jobs ms | KB allocated | gen0 GCs; then notes");
        EngineSection();
        PoolSection();
        MoveSection();
        // A leak guard: every tree is closed and collected, so the managed heap stays small.
        Out($"managed heap after the run: {GC.GetTotalMemory(true) / 1024.0 / 1024.0:F1} MB");
        Out(_failures == 0 ? "all checks passed" : $"{_failures} CHECK(S) FAILED");
        return _failures;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Measuring
    // ════════════════════════════════════════════════════════════════════

    // teardown gets whether the run was the warm-up, so tallies of kept, detached and focused rows count timed runs only.
    private Stats Sample<T>(Func<T> setup, Action<T> change, Action<T> layout, Action<T, bool> teardown)
    {
        var changeMs = new List<double>();
        var totalMs = new List<double>();
        var kb = new List<double>();
        var gen0 = new List<double>();
        for (int run = -1; run < runs; run++)
        {
            var state = setup();
            Settle();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gcBefore = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();
            change(state);
            double t1 = sw.Elapsed.TotalMilliseconds;
            layout(state);
            // What the change posted to the dispatcher (Avalonia raises Loaded and Unloaded through it) is part of its cost.
            Dispatcher.UIThread.RunJobs();
            double t2 = sw.Elapsed.TotalMilliseconds;
            long after = GC.GetAllocatedBytesForCurrentThread();
            int gcAfter = GC.CollectionCount(0);
            teardown(state, run < 0);
            Settle();
            if (run < 0) continue; // warm-up
            changeMs.Add(t1);
            totalMs.Add(t2);
            kb.Add((after - before) / 1024.0);
            gen0.Add(gcAfter - gcBefore);
        }
        return new Stats(Median(changeMs), Median(totalMs), Median(kb), Median(gen0));
    }

    // The whole run is one dispatcher job, so nothing else pumps the queue: run what setup and teardown posted (window
    // events, deferred cleanup) and render a frame, outside the timed part. Without it, closed windows pile up.
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static double Median(List<double> xs)
    {
        xs.Sort();
        return xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2;
    }

    private void Print(string name, string where, Stats s, string note = "")
        => Out($"  {name,-34} {where,-8} {s.ChangeMs,8:F2} | {s.TotalMs,8:F2} | {s.Kb,9:F0} KB | {s.Gen0,3:F0} gen0  {note}");

    private void Check(bool ok, string what)
    {
        if (ok) return;
        _failures++;
        Console.WriteLine($"  CHECK FAILED: {what}");
    }

    private static string[] Keys(string prefix, int count) => Enumerable.Range(0, count).Select(i => prefix + i).ToArray();

    private static Window ShowWindow(Control content)
    {
        var window = new Window { Width = 800, Height = 600, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    // Focuses a row's Button (a row is Border > StackPanel > TextBlock + Button); null if focus wasn't taken.
    private static Button? FocusButton(StackPanel list, int index)
    {
        var button = (Button)((StackPanel)((Border)list.Children[index]).Child!).Children[1];
        return button.Focus() && button.IsFocused ? button : null;
    }

    // The reorders Spike G measured, plus the churn this issue is about.
    private IEnumerable<(string Name, Func<string[], string[]> Change)> Reorders()
    {
        yield return ("reverse", keys => keys.Reverse().ToArray());
        yield return ("shuffle (seeded)", keys => { var rng = new Random(42); return keys.OrderBy(_ => rng.Next()).ToArray(); });
        yield return ("move last to first", keys => [keys[^1], .. keys[..^1]]);
        yield return ("move first to last", keys => [.. keys[1..], keys[0]]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. Engine
    // ════════════════════════════════════════════════════════════════════

    private readonly Dictionary<string, string[]> _finalOrder = new();
    private readonly Dictionary<string, List<MoveOp>> _engineMoves = new();
    private bool _engineMovesAreMoves; // the engine moved with Controls.Move (move-collection.patch applied)
    private readonly Dictionary<string, int> _movedRows = new(); // distinct rows each reorder moves

    // Records the edits the engine makes for each reorder (detached), for the focus checks and section 3.
    private void RecordMoves()
    {
        foreach (var (name, changeKeys) in Reorders())
        {
            var host = new EngineHost(inWindow: false);
            var keys = Keys("k", n);
            host.Keys = keys;
            host.Render();
            var events = new List<NotifyCollectionChangedEventArgs>();
            host.List.Children.CollectionChanged += (_, e) => events.Add(e);
            host.Keys = changeKeys(keys);
            host.Render();
            var moves = new List<MoveOp>();
            bool movesOnly = true;
            for (int i = 0; i < events.Count; i++)
            {
                if (i + 1 < events.Count
                    && events[i] is { Action: NotifyCollectionChangedAction.Remove, OldItems.Count: 1 } removed
                    && events[i + 1] is { Action: NotifyCollectionChangedAction.Add, NewItems.Count: 1 } added
                    && ReferenceEquals(removed.OldItems![0], added.NewItems![0]))
                {
                    moves.Add(new MoveOp(removed.OldStartingIndex, added.NewStartingIndex));
                    i++;
                }
                else if (events[i] is { Action: NotifyCollectionChangedAction.Move } moved)
                {
                    moves.Add(new MoveOp(moved.OldStartingIndex, moved.NewStartingIndex)); // the Controls.Move variant (move-collection.patch)
                    _engineMovesAreMoves = true;
                }
                else movesOnly = false;
            }
            Check(movesOnly, $"engine {name}: every collection edit is a move (a remove + insert of the same row, or a Move)");
            _engineMoves[name] = moves;
            _finalOrder[name] = host.Keys;
            // Which original rows the moves touch, and how many go to a later index (the direction Move's contract is
            // easiest to get wrong), by replaying the indices on a plain list.
            var order = Enumerable.Range(0, n).ToList();
            var movedRows = new HashSet<int>();
            int forward = 0;
            foreach (var (from, to) in moves)
            {
                var item = order[from];
                order.RemoveAt(from);
                order.Insert(to, item);
                movedRows.Add(item);
                if (to > from) forward++;
            }
            _movedRows[name] = movedRows.Count;
            Out($"  engine edits for {name,-20} {moves.Count} moves ({events.Count} collection events), {movedRows.Count} distinct rows, {forward} to a later index");
        }
    }

    private void EngineSection()
    {
        Out();
        Out("1. Engine (reconciler + upstream ChildReconciler), memo rows: Border > StackPanel > TextBlock + Button");
        RecordMoves();
        var cases = new List<(string Name, Func<string[], string[]> Change)>
        {
            ("mount", keys => keys),
            ("re-render, same keys", keys => keys),
            ("re-key all (n unmount + n mount)", keys => keys.Select(k => "r" + k).ToArray()),
            ("re-key every other", keys => keys.Select((k, i) => i % 2 == 0 ? "r" + k : k).ToArray()),
            ("unmount all", _ => []),
            ("mount again after unmount all", keys => keys),
        };
        cases.AddRange(Reorders());
        foreach (var (name, changeKeys) in cases)
        {
            foreach (var where in Where)
            {
                bool inWindow = where == "window";
                bool isMount = name is "mount" or "mount again after unmount all";
                int keptMin = int.MaxValue, detachedMax = 0, focusKept = 0, focusRuns = 0;
                var stats = Sample(
                    setup: () =>
                    {
                        var host = new EngineHost(inWindow);
                        var keys = Keys("k", n);
                        if (name == "mount again after unmount all")
                        {
                            host.Keys = keys;
                            host.Render();
                            host.Layout();
                            host.Keys = [];
                            host.Render();
                            host.Layout();
                            host.Keys = keys;
                        }
                        else if (name == "mount")
                            host.Keys = keys;
                        else
                        {
                            host.Keys = keys;
                            host.Render();
                            host.Layout();
                            // Focus a row the change moves: the first row the engine moves, else the first row.
                            host.Snapshot(_engineMoves.TryGetValue(name, out var m) && m.Count > 0 ? m[0].From : 0);
                            host.Keys = changeKeys(keys);
                        }
                        return host;
                    },
                    change: h => h.Render(),
                    layout: h => h.Layout(),
                    teardown: (h, warmup) =>
                    {
                        Check(h.RowKeys().SequenceEqual(h.Keys), $"engine {name} {where}: rows in key order");
                        if (!isMount)
                        {
                            int kept = h.Kept();
                            Check(kept == h.Keys.Count(k => k.StartsWith('k')), $"engine {name} {where}: every surviving key kept its row control (kept {kept})");
                            if (inWindow)
                            {
                                // Each move detaches its row with RemoveAt + Insert and none with Controls.Move; a removal detaches its row.
                                int expected = _engineMoves.ContainsKey(name) ? (_engineMovesAreMoves ? 0 : _movedRows[name]) : n - kept;
                                Check(h.Detached == expected, $"engine {name} {where}: rows detached {h.Detached}, expected {expected}");
                            }
                        }
                        if (!warmup)
                        {
                            if (!isMount) keptMin = Math.Min(keptMin, h.Kept());
                            detachedMax = Math.Max(detachedMax, h.Detached);
                            if (h.FocusedButton is { } b && _engineMoves.ContainsKey(name) && h.RowKeys().Contains(h.FocusedKey))
                            {
                                focusRuns++;
                                if (b.IsFocused) focusKept++;
                            }
                        }
                        h.Close();
                    });
                string note = isMount ? "" : $"kept {keptMin}";
                if (inWindow && !isMount) note += $", rows detached from the visual tree {detachedMax}";
                if (focusRuns > 0) note += $", focused Button of a moved row still focused in {focusKept} of {focusRuns} runs";
                Print(name, where, stats, note);
            }
        }

    }

    /// <summary>A root host like Spike G's: the list's keys, rendered through the library's reconciler.</summary>
    private sealed class EngineHost
    {
        private Element? _last;
        private Dictionary<string, Control> _before = new();
        private readonly Window? _window;

        public EngineHost(bool inWindow)
        {
            if (inWindow) _window = ShowWindow(Root);
        }

        public Reconciler Reconciler { get; } = new();
        public Border Root { get; } = new();
        public string[] Keys { get; set; } = [];
        private readonly HashSet<Control> _detached = new(ReferenceEqualityComparer.Instance);

        /// <summary>Distinct rows taken out of the visual tree since Snapshot.</summary>
        public int Detached => _detached.Count;
        public StackPanel List => (StackPanel)Root.Child!;

        public void Render()
        {
            var element = new StackElement(Orientation.Vertical, Keys.Select(Row).ToArray());
            var next = Reconciler.Reconcile(_last, element, Root.Child, () => { });
            if (!ReferenceEquals(next, Root.Child)) Root.Child = next;
            _last = element;
        }

        public void Layout() => _window?.UpdateLayout();

        public void Close() => _window?.Close();

        // Remembers each row's control (to count rows kept) and counts rows taken out of the visual tree.
        public void Snapshot(int focusIndex)
        {
            _before = RowControls();
            foreach (var row in _before.Values) row.DetachedFromVisualTree += (s, _) => _detached.Add((Control)s!);
            if (_window is null) return;
            FocusedButton = FocusButton(List, focusIndex);
            FocusedKey = KeyOf(List.Children[focusIndex]);
            if (FocusedButton is null) throw new InvalidOperationException($"row {focusIndex}'s Button didn't take focus");
        }

        /// <summary>The Button focused before the change (window only), or null.</summary>
        public Button? FocusedButton { get; private set; }

        public string? FocusedKey { get; private set; }

        public int Kept() => RowControls().Count(kv => _before.TryGetValue(kv.Key, out var c) && ReferenceEquals(c, kv.Value));

        public IEnumerable<string> RowKeys() => List.Children.Select(KeyOf);

        private Dictionary<string, Control> RowControls() => List.Children.ToDictionary(KeyOf, c => c);

        private static string KeyOf(Control row) => ((TextBlock)((StackPanel)((Border)row).Child!).Children[0]).Text!.Split(':')[0];

        // Spike G's row: a memoized function component with its own state, so a parent pass skips it.
        private static Element Row(string key) => new MemoElement(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return new StackElement(Orientation.Horizontal,
                [new TextBlockElement($"{key}: {count}"), new ButtonElement("+", () => setCount(count + 1))]);
        }) { Key = key };
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. Pooling, at the Avalonia layer
    // ════════════════════════════════════════════════════════════════════

    private void PoolSection()
    {
        Out();
        Out("2. Avalonia: the same row controls created fresh vs. rented from a pool of used, reset controls");
        Print("construct only (no tree)", "detached", Sample(
            setup: () => new List<Border>(n),
            change: list => { for (int i = 0; i < n; i++) list.Add(RowControls.Create("k" + i, null)); },
            layout: _ => { },
            teardown: (_, _) => { }));

        foreach (var where in Where)
        {
            bool inWindow = where == "window";
            // Mount n rows into an empty, laid-out panel.
            Print("mount: create", where, Sample(
                setup: () => new PanelHost(inWindow, 0, n),
                change: h => h.AddRows("k", null),
                layout: h => h.Layout(),
                teardown: (h, _) => h.Close()));
            Print("mount: rent from a full pool", where, Sample(
                setup: () =>
                {
                    var pool = new Pool(int.MaxValue, n);
                    var h = new PanelHost(inWindow, n, n);
                    h.RemoveRows(pool); // the rows were in the tree and laid out; now reset and pooled
                    h.Layout();
                    h.Pool = pool;
                    return h;
                },
                change: h => h.AddRows("k", h.Pool),
                layout: h => h.Layout(),
                teardown: (h, _) =>
                {
                    Check(h.Pool!.Rented == 4 * n && h.Pool.Created == 0, $"pool mount {where}: every control rented");
                    h.Close();
                }));
            // Replace every row: n removals, then n new rows.
            foreach (var (label, cap) in new (string, int?)[] { ("re-key all: create", null), ("re-key all: pool, no cap", int.MaxValue), ("re-key all: pool, cap 32", 32) })
            {
                Print(label, where, Sample(
                    setup: () =>
                    {
                        var h = new PanelHost(inWindow, n, n);
                        if (cap is { } c) h.Pool = new Pool(c, n);
                        return h;
                    },
                    change: h =>
                    {
                        h.RemoveRows(h.Pool);
                        h.AddRows("r", h.Pool);
                    },
                    layout: h => h.Layout(),
                    teardown: (h, _) =>
                    {
                        if (cap is { } c) Check(h.Pool!.Rented == 4 * Math.Min(c, n), $"pool re-key {where} cap {c}: rented {h.Pool.Rented}");
                        h.Close();
                    }), cap is 32 ? "(upstream's ElementPool keeps at most 32 per type)" : "");
            }
            Print("unmount all: drop", where, Sample(
                setup: () => new PanelHost(inWindow, n, n),
                change: h => h.RemoveRows(null),
                layout: h => h.Layout(),
                teardown: (h, _) => h.Close()));
            Print("unmount all: reset and pool", where, Sample(
                setup: () => new PanelHost(inWindow, n, n) { Pool = new Pool(int.MaxValue, n) }, // empty, with room for n rows
                change: h => h.RemoveRows(h.Pool),
                layout: h => h.Layout(),
                teardown: (h, _) => h.Close()));
        }
    }

    /// <summary>A per-type pool of controls, as upstream's ElementPool (which caps each type at 32).</summary>
    private sealed class Pool
    {
        private readonly int _capPerType;
        private readonly Dictionary<Type, Stack<Control>> _stacks = new();

        // reserve: room for that many of each row control type up front, as a pool that has been in use has, so a timed
        // run doesn't pay for growing the stacks.
        public Pool(int capPerType, int reserve)
        {
            _capPerType = capPerType;
            foreach (var type in new[] { typeof(Border), typeof(StackPanel), typeof(TextBlock), typeof(Button) })
                _stacks[type] = new Stack<Control>(Math.Min(capPerType, reserve));
        }
        public int Rented { get; private set; }
        public int Created { get; private set; }

        public T? TryRent<T>() where T : Control
        {
            if (_stacks.TryGetValue(typeof(T), out var stack) && stack.Count > 0)
            {
                Rented++;
                return (T)stack.Pop();
            }
            Created++;
            return null;
        }

        public void Return(Control control)
        {
            if (!_stacks.TryGetValue(control.GetType(), out var stack)) _stacks[control.GetType()] = stack = new();
            if (stack.Count < _capPerType) stack.Push(control);
        }
    }

    /// <summary>
    /// The controls the engine mounts for one row (a component's Border around a StackPanel of a TextBlock and a
    /// Button), built directly; Tag holds the key.
    /// </summary>
    private static class RowControls
    {
        private static readonly EventHandler<RoutedEventArgs> OnClick = (_, _) => { };

        public static Border Create(string key, Pool? pool)
        {
            var border = pool?.TryRent<Border>() ?? new Border();
            var stack = pool?.TryRent<StackPanel>() ?? new StackPanel();
            var text = pool?.TryRent<TextBlock>() ?? new TextBlock();
            // A pooled Button keeps its click trampoline subscribed, as upstream's pool does (spec 047 KD-3).
            var button = pool?.TryRent<Button>() ?? NewButton();
            stack.Orientation = Orientation.Horizontal;
            stack.Spacing = 8;
            text.Text = key + ": 0";
            button.Content = "+";
            stack.Children.Add(text);
            stack.Children.Add(button);
            border.Child = stack;
            border.Tag = key;
            return border;
        }

        private static Button NewButton()
        {
            var button = new Button();
            button.Click += OnClick;
            return button;
        }

        // Takes a removed row apart and resets what Create wrote, as upstream's CleanElement clears what Reactor set.
        public static void Return(Border border, Pool pool)
        {
            var stack = (StackPanel)border.Child!;
            border.Child = null;
            var text = (TextBlock)stack.Children[0];
            var button = (Button)stack.Children[1];
            stack.Children.Clear();
            border.ClearValue(Control.TagProperty);
            stack.ClearValue(StackPanel.OrientationProperty);
            stack.ClearValue(StackPanel.SpacingProperty);
            text.ClearValue(TextBlock.TextProperty);
            button.ClearValue(ContentControl.ContentProperty);
            pool.Return(border);
            pool.Return(stack);
            pool.Return(text);
            pool.Return(button);
        }
    }

    /// <summary>A vertical StackPanel of rows built directly, detached or in a headless window.</summary>
    private sealed class PanelHost
    {
        private readonly Window? _window;
        private readonly int _rows;

        // Starts with initialRows rows ("k0", "k1"…) laid out; AddRows adds rows of them.
        public PanelHost(bool inWindow, int initialRows, int rows)
        {
            _rows = rows;
            if (inWindow) _window = ShowWindow(Panel);
            for (int i = 0; i < initialRows; i++) Panel.Children.Add(RowControls.Create("k" + i, null));
            Layout();
        }

        public StackPanel Panel { get; } = new();
        public Pool? Pool { get; set; }
        private readonly HashSet<Control> _detached = new(ReferenceEqualityComparer.Instance);

        /// <summary>Distinct rows taken out of the visual tree since WatchDetach.</summary>
        public int Detached => _detached.Count;

        public void AddRows(string prefix, Pool? pool)
        {
            for (int i = 0; i < _rows; i++) Panel.Children.Add(RowControls.Create(prefix + i, pool));
        }

        // Removes rows from the end, one at a time, as a keyed diff removes them; returns them to the pool if one is given.
        public void RemoveRows(Pool? pool)
        {
            for (int i = Panel.Children.Count - 1; i >= 0; i--)
            {
                var row = (Border)Panel.Children[i];
                Panel.Children.RemoveAt(i);
                if (pool is not null) RowControls.Return(row, pool);
            }
        }

        public void WatchDetach()
        {
            foreach (var row in Panel.Children) row.DetachedFromVisualTree += (s, _) => _detached.Add((Control)s!);
        }

        public void Layout() => _window?.UpdateLayout();

        public void Close() => _window?.Close();

        public IEnumerable<string> Keys() => Panel.Children.Select(c => (string)c.Tag!);

        // Avalonia keeps the visual and logical children in Children's order; a move must move them too.
        public bool TreesInOrder()
            => Panel.GetVisualChildren().SequenceEqual(Panel.Children)
               && ((Avalonia.LogicalTree.ILogical)Panel).LogicalChildren.SequenceEqual(Panel.Children);

        public bool ArrangedInOrder()
        {
            double y = double.NegativeInfinity;
            foreach (var c in Panel.Children)
            {
                if (!(c.Bounds.Y > y) || c.Bounds.Height <= 0) return false;
                y = c.Bounds.Y;
            }
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. Moves
    // ════════════════════════════════════════════════════════════════════

    private void MoveSection()
    {
        Out();
        Out("3. Avalonia: the engine's moves for each reorder, replayed on built rows: RemoveAt + Insert vs. Controls.Move");
        foreach (var (name, _) in Reorders())
        {
            var moves = _engineMoves[name];
            var expected = _finalOrder[name];
            if (moves.Count == 0) continue; // RecordMoves has reported it as a failed check
            foreach (var where in Where)
            {
                bool inWindow = where == "window";
                foreach (var useMove in new[] { false, true })
                {
                    int detachedMax = 0, focusKept = 0, focusRuns = 0;
                    Button? focused = null;
                    var stats = Sample(
                        setup: () =>
                        {
                            var h = new PanelHost(inWindow, n, n);
                            h.WatchDetach();
                            focused = inWindow ? FocusButton(h.Panel, moves[0].From) : null; // a row that moves
                            Check(!inWindow || focused is not null, $"replay {name} {where}: the moved row's Button took focus");
                            return h;
                        },
                        change: h =>
                        {
                            var children = h.Panel.Children;
                            foreach (var (from, to) in moves)
                            {
                                if (useMove)
                                    children.Move(from, to);
                                else
                                {
                                    var item = children[from];
                                    children.RemoveAt(from);
                                    children.Insert(to, item);
                                }
                            }
                        },
                        layout: h => h.Layout(),
                        teardown: (h, warmup) =>
                        {
                            Check(h.Keys().SequenceEqual(expected), $"replay {name} {where} {(useMove ? "Move" : "RemoveAt+Insert")}: final order is the engine's");
                            Check(h.TreesInOrder(), $"replay {name} {where} {(useMove ? "Move" : "RemoveAt+Insert")}: visual and logical children in the new order");
                            if (inWindow) Check(h.ArrangedInOrder(), $"replay {name} {where} {(useMove ? "Move" : "RemoveAt+Insert")}: rows arranged top to bottom in the new order");
                            if (inWindow) Check(h.Detached == (useMove ? 0 : _movedRows[name]), $"replay {name} {where} {(useMove ? "Move" : "RemoveAt+Insert")}: rows detached {h.Detached}");
                            if (!warmup)
                            {
                                detachedMax = Math.Max(detachedMax, h.Detached);
                                if (focused is not null)
                                {
                                    focusRuns++;
                                    if (focused.IsFocused) focusKept++;
                                }
                            }
                            h.Close();
                        });
                    var label = $"{name}: {(useMove ? "Controls.Move" : "RemoveAt + Insert")}";
                    Print(label, where, stats, inWindow ? $"rows detached from the visual tree {detachedMax}, focused Button of a moved row still focused in {focusKept} of {focusRuns} runs" : "");
                }
            }
        }
        MoveContractCheck();
        FlexMoveCheck();
    }

    // Controls.Move against the contract of upstream's Move (RemoveAt(from), then Insert(to): to is the final index), in
    // both directions: seeded random moves on rows in a window, the trees checked after each move, the layout at the end.
    private void MoveContractCheck()
    {
        const int count = 20, steps = 200;
        var host = new PanelHost(inWindow: true, count, count);
        var expected = Enumerable.Range(0, count).Select(i => "k" + i).ToList();
        var rng = new Random(7);
        int forward = 0, backward = 0;
        bool ok = true;
        for (int step = 0; step < steps; step++)
        {
            int from = rng.Next(count), to = rng.Next(count);
            host.Panel.Children.Move(from, to);
            var item = expected[from];
            expected.RemoveAt(from);
            expected.Insert(to, item);
            if (to > from) forward++;
            else if (to < from) backward++;
            ok &= host.Keys().SequenceEqual(expected) && host.TreesInOrder();
        }
        host.Layout();
        ok &= host.ArrangedInOrder();
        host.Close();
        Check(ok, "Controls.Move: children, visual and logical order and arrangement follow RemoveAt + Insert's result");
        Out($"  Controls.Move contract, {steps} seeded moves on {count} rows in a window ({forward} to a later index, {backward} to an earlier one): same result as RemoveAt + Insert {(ok ? "yes" : "NO")}");
    }

    // A FlexPanel lays out from its own Yoga tree, rebuilt in Children's order on each measure: check a reverse done
    // with Controls.Move (no detach, so no cache drop) lays the row out in the new order.
    private void FlexMoveCheck()
    {
        const int count = 20;
        var flex = new FlexPanel();
        for (int i = 0; i < count; i++) flex.Children.Add(new Border { Width = 10, Height = 10, Tag = i });
        var window = ShowWindow(flex);
        var slots = Enumerable.Range(0, count).Select(i => i * 10.0).ToArray();
        bool before = flex.Children.Select(c => c.Bounds.X).SequenceEqual(slots);
        // Reverse with moves to earlier indices, then back with moves to later ones.
        for (int i = 0; i < count - 1; i++) flex.Children.Move(count - 1, i);
        window.UpdateLayout();
        bool reversed = flex.Children.Select(c => (int)c.Tag!).SequenceEqual(Enumerable.Range(0, count).Reverse())
                        && flex.Children.Select(c => c.Bounds.X).SequenceEqual(slots);
        for (int i = 0; i < count - 1; i++) flex.Children.Move(0, count - 1 - i);
        window.UpdateLayout();
        bool restored = flex.Children.Select(c => (int)c.Tag!).SequenceEqual(Enumerable.Range(0, count))
                        && flex.Children.Select(c => c.Bounds.X).SequenceEqual(slots);
        window.Close();
        bool ok = before && reversed && restored;
        Check(ok, "FlexPanel: reverses by Controls.Move, in both directions, lay the row out in the new order");
        Out($"  FlexPanel reversed by Controls.Move to earlier indices and back to later ones, {count} children: laid out in the new order {(ok ? "yes" : "NO")}");
    }
}
