using Avalonia.Threading;

namespace Microsoft.UI.Reactor;

/// <summary>
/// The engine's view of a UI-thread queue, under the member names upstream calls on WinUI's
/// <c>DispatcherQueue</c> (<c>GetForCurrentThread</c>, <c>TryEnqueue</c>), over Avalonia's
/// <see cref="Dispatcher"/>. Internal: the public API exposes Avalonia's dispatcher.
/// </summary>
internal sealed class DispatcherQueue
{
    private static DispatcherQueue? s_uiThread;
    private readonly Dispatcher _dispatcher;
    private volatile bool _shutdownStarted;

    private DispatcherQueue(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        dispatcher.ShutdownStarted += (_, _) => _shutdownStarted = true;
    }

    /// <summary>
    /// The queue of Avalonia's current UI-thread dispatcher. Re-created when that dispatcher changes (a headless
    /// test session gets its own), so a queue is never left pointing at a dispatcher nothing pumps.
    /// </summary>
    internal static DispatcherQueue ForUIThread
    {
        get
        {
            var ui = Dispatcher.UIThread;
            var queue = s_uiThread;
            if (queue is null || !ReferenceEquals(queue._dispatcher, ui))
                s_uiThread = queue = new DispatcherQueue(ui);
            return queue;
        }
    }

    /// <summary>Whether the calling thread is this queue's thread.</summary>
    internal bool HasThreadAccess => _dispatcher.CheckAccess();

    /// <summary>The Avalonia dispatcher this queue posts to.</summary>
    internal Dispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// The queue of the calling thread when it is Avalonia's UI thread, else <c>null</c>
    /// (WinUI returns null on a thread without a queue).
    /// </summary>
    internal static DispatcherQueue? GetForCurrentThread()
        => Dispatcher.UIThread.CheckAccess() ? ForUIThread : null;

    /// <summary>
    /// Posts <paramref name="callback"/> to the UI thread. Returns <c>false</c> once the dispatcher
    /// has begun shutting down, as WinUI's <c>TryEnqueue</c> does.
    /// </summary>
    internal bool TryEnqueue(Action callback)
    {
        if (_shutdownStarted) return false;
        _dispatcher.Post(callback);
        return true;
    }
}
