using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using Isotope.UI.Core;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A minimal root host for tests: renders a root element into a <see cref="Border"/> through the
/// <see cref="Reconciler"/>, and re-renders from the root on <see cref="Flush"/> after a state change
/// asked for one (the real host, tier 3, schedules that on the dispatcher).
/// </summary>
internal sealed class TestHost
{
    private readonly Func<Element> _render;
    private Element? _last;
    private bool _pending;

    public TestHost(Func<Element> render, ILogger? logger = null)
    {
        _render = render;
        Reconciler = new Reconciler(logger);
        Render();
    }

    public Reconciler Reconciler { get; }
    public Border Root { get; } = new();
    public int RootRenders { get; private set; }
    public bool RerenderRequested => _pending;

    public void Render()
    {
        var element = _render();
        var next = Reconciler.Reconcile(_last, element, Root.Child, () => _pending = true);
        if (!ReferenceEquals(next, Root.Child)) Root.Child = next;
        _last = element;
        RootRenders++;
    }

    /// <summary>Runs the root passes state changes asked for.</summary>
    public void Flush()
    {
        for (int guard = 0; _pending; guard++)
        {
            if (guard > 50) throw new InvalidOperationException("render loop");
            _pending = false;
            Render();
        }
    }

    /// <summary>Unmounts the whole tree.</summary>
    public void Unmount()
    {
        if (Root.Child is { } child) Reconciler.UnmountChild(child);
        Root.Child = null;
        _last = null;
    }

    /// <summary>The controls of type <typeparamref name="T"/> under the root, depth first.</summary>
    public List<T> All<T>() where T : Control
    {
        var found = new List<T>();
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case T t: found.Add(t); break;
            }
            switch (c)
            {
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
                case ContentControl { Content: Control content }: Walk(content); break;
            }
        }
        Walk(Root.Child);
        return found;
    }

    public List<string?> Texts() => All<TextBlock>().Select(t => t.Text).ToList();

    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Call before a headless press and release. A RepeatButton repeats once it is held past its Delay, and a headless
    /// MouseUp runs the due dispatcher timers first, so on a slow runner a click fired twice (#55). A click in a test
    /// isn't a hold: this moves the first repeat a minute out, on the control (a style can't: the element sets Delay
    /// itself). TestApp does the same for tooltips.
    /// </summary>
    public static void HoldOffRepeat(Control control)
    {
        if (control is RepeatButton repeat) repeat.Delay = 60_000;
    }
}

/// <summary>Collects log entries.</summary>
internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
