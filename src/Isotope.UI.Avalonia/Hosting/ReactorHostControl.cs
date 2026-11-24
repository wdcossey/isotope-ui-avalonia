using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Isotope.UI.Core;

namespace Isotope.UI.Hosting;

/// <summary>
/// Renders a root component into any Avalonia layout: a control whose child is the component's UI, re-rendered
/// on Avalonia's dispatcher when its state changes. Tier 3: written for Avalonia after upstream's
/// <c>ReactorHostControl</c>, whose render scheduling, render-error path and lifetime rules it keeps.
/// </summary>
/// <remarks>
/// <para><b>Scheduling.</b> Nothing renders synchronously: <see cref="Mount"/> and every state change ask for a pass,
/// and the pass runs from the dispatcher. However many changes arrive before it runs, one pass is queued. A change made
/// during a pass (by the render or an effect) queues one more pass, at <see cref="DispatcherPriority.Background"/> so
/// input, layout and rendering run first; so does any request after a pass slower than one frame (16 ms).</para>
/// <para><b>Errors.</b> An exception from the root's render (<see cref="RenderErrorSource.RootRender"/>), the reconcile
/// after it (<see cref="RenderErrorSource.Reconcile"/>: a control's mount or update, a modifier) or the root's effects,
/// flushed once the tree is committed (<see cref="RenderErrorSource.Effects"/>), goes to
/// <see cref="EffectiveRenderErrorHandler"/> with <see cref="RenderError.IsHostLevel"/> true, and its outcome replaces
/// the whole content. With no handler, or one that returns <c>null</c>, a built-in panel shows the exception text; after
/// a root render or effects failure the tree is then kept, so the next good pass reconciles against it and child state
/// survives. After a reconcile failure the half-updated tree is released (its cleanups run, and so do those of what the
/// pass mounted but never attached). An element the handler returns replaces the content, and the tree it replaces is
/// unmounted; it is mounted inside a guard, as upstream's, so a component in it that throws shows the neutral text. A
/// handler that throws, or whose element fails, gets a neutral "Something went wrong." panel.</para>
/// <para>A child component whose render or effects throw outside every error boundary is replaced in its own slot, as
/// upstream's: the handler gets <see cref="RenderErrorSource.ComponentRender"/> or <see cref="RenderErrorSource.Effects"/>
/// with <see cref="RenderError.IsHostLevel"/> false and the component's name (a class component's), and the rest of the
/// tree keeps its controls and state. The slot shows the handler's element, or the exception text when there is none,
/// or the neutral text when the handler throws or its element does. Every later render of that component retries it.
/// Inside an <see cref="ErrorBoundaryElement"/>, the boundary shows its own fallback instead, and the handler isn't
/// told.</para>
/// <para>A handler that calls <see cref="RenderError.Propagate"/>, for either kind of error, gets nothing shown (the
/// tree is released), and the exception is rethrown out of the dispatcher callback (Avalonia's
/// <c>Dispatcher.UnhandledException</c>; Avalonia also faults the dispatcher operation's task with it, which surfaces as
/// an unobserved task exception). Every later pass retries the root.</para>
/// <para><b>Lifetime.</b> Leaving the visual tree does not unmount (it may be a move); <see cref="Dispose"/> does:
/// it runs every effect cleanup (each isolated from the others' failures) and stops rendering, also when called during
/// a pass. A cleanup that throws is reported to the handler as
/// <see cref="RenderErrorSource.Cleanup"/>; with no handler, the first is rethrown once the teardown is done.</para>
/// </remarks>
public sealed partial class ReactorHostControl : Decorator, IDisposable
{
    /// <summary>A pass slower than this (one frame at 60 Hz) demotes the next one, as upstream's render priority policy.</summary>
    internal const double FrameBudgetMs = 16.0;

    private readonly Reconciler _reconciler;
    private readonly ILogger? _logger;
    private readonly DispatcherQueue _queue;
    private readonly Action _requestRender;
    private readonly Action _renderLoop;

    private Func<RenderContext, Element>? _rootRender;
    private RenderContext? _rootContext;
    // The tree the last pass (or fallback) reconciled, and its control. While the built-in error panel is shown, the
    // control is detached but kept, so the next good pass reconciles against it.
    private Element? _currentTree;
    private Control? _currentControl;

    private int _renderPending;          // 0 or 1: one queued pass at a time (Interlocked, any thread)
    private volatile bool _needsRerender;
    private volatile bool _isRendering;  // written on the UI thread only
    private volatile bool _disposed;
    private double _lastRenderMs;

    /// <summary>Creates an empty host on the UI thread; <see cref="Mount"/> gives it a root.</summary>
    public ReactorHostControl(ILogger? logger = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _logger = logger;
        // The owner of the content: the platform hooks find this host (and its window) through it.
        // The handler a child component's failure is reported to, from the reconciler (#28), resolved when it fails.
        _reconciler = new Reconciler(logger) { Owner = this, RenderErrorHandlerProvider = () => EffectiveRenderErrorHandler };
        _queue = DispatcherQueue.ForUIThread;
        _requestRender = RequestRender;
        _renderLoop = RenderLoop;
        // As upstream's host: a state setter called off the UI thread is moved onto it once a host exists.
        if (ReactorApp.UIDispatcher is not { } ui || !ReferenceEquals(ui.Dispatcher, _queue.Dispatcher))
            ReactorApp.UIDispatcher = _queue;
    }

    /// <summary>Creates a host and mounts <paramref name="rootRender"/> (it renders on the dispatcher).</summary>
    public ReactorHostControl(Func<RenderContext, Element> rootRender, ILogger? logger = null) : this(logger)
        => Mount(rootRender);

    /// <summary>
    /// This host's handler for render errors; <c>null</c> (the default) uses <see cref="ReactorApp.DefaultRenderErrorHandler"/>.
    /// </summary>
    public RenderErrorHandler? RenderErrorHandler { get; set; }

    /// <summary>The handler used right now: this host's, else the app-wide default, else <c>null</c> (the built-in panel).</summary>
    public RenderErrorHandler? EffectiveRenderErrorHandler => RenderErrorHandler ?? ReactorApp.DefaultRenderErrorHandler;

    /// <summary>
    /// True when no pass is queued or running and none is asked for. Safe to read from any thread; a disposed host is idle.
    /// </summary>
    public bool IsIdle => _disposed || (Volatile.Read(ref _renderPending) == 0 && !_isRendering && !_needsRerender);

    /// <summary>
    /// Mounts a function component as the root, replacing any previous root (its tree is unmounted and its cleanups
    /// run first). The root renders on the dispatcher, not inside this call.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called during this host's own render pass.</exception>
    public void Mount(Func<RenderContext, Element> rootRender)
    {
        ArgumentNullException.ThrowIfNull(rootRender);
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAccess();
        if (_isRendering)
            throw new InvalidOperationException("Mount was called during this host's render pass; call it from an event handler or after the pass.");
        var failures = RetireRoot();
        _rootRender = rootRender;
        _rootContext = new RenderContext();
        RequestRender();
        ReportCleanupFailures(failures)?.Throw();
    }

    /// <summary>
    /// Unmounts the tree, runs every effect cleanup (the root's first, then its children's) and stops rendering.
    /// Must be called on the UI thread; a second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        VerifyAccess();
        _disposed = true;
        // During a pass (an effect or handler closed the window), the pass sees _disposed and drops what it mounts.
        var failures = RetireRoot();
        ReportCleanupFailures(failures)?.Throw();
    }

    // Thread-safe. During a pass a request only sets the flag (the pass re-queues once); between passes the first
    // request flips the gate and queues the pass, and later ones find it closed.
    private void RequestRender()
    {
        if (_disposed) return;
        _needsRerender = true;
        if (_isRendering) return;
        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0) return;
        var priority = Volatile.Read(ref _lastRenderMs) > FrameBudgetMs ? DispatcherPriority.Background : DispatcherPriority.Normal;
        Enqueue(priority);
    }

    // The gate is held (_renderPending is 1). A dispatcher shutting down takes nothing: then nothing is pending.
    private void Enqueue(DispatcherPriority priority)
    {
        if (_queue.TryEnqueue(priority, _renderLoop)) return;
        _needsRerender = false;
        Interlocked.Exchange(ref _renderPending, 0);
    }

    private void RenderLoop()
    {
        try
        {
            if (_disposed) return;
            _needsRerender = false;
            Render();
        }
        finally
        {
            // Also when a propagated render error escapes, so later requests can queue again.
            Interlocked.Exchange(ref _renderPending, 0);
            // A change during the pass (also one that propagated): one more pass, below input, layout and rendering,
            // so a component that keeps changing its state can't starve them.
            if (_needsRerender && !_disposed && Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                Enqueue(DispatcherPriority.Background);
        }
    }

    private void Render()
    {
        if (_rootRender is not { } render || _rootContext is not { } ctx) return;
        _isRendering = true;
        var clock = Stopwatch.StartNew();
        var phase = RenderErrorSource.RootRender;
        try
        {
            _reconciler.BeginRootRender(ctx, _requestRender);
            Element? tree = render(ctx);
            // Disposed by the render (Dispose retired the root): nothing to commit.
            if (_disposed) return;
            // As upstream: a root that renders null keeps its content.
            if (tree is not null)
            {
                phase = RenderErrorSource.Reconcile;
                var control = _reconciler.Reconcile(_currentTree, tree, _currentControl, _requestRender);
                if (_disposed)
                {
                    // Disposed during the reconcile (a child's effect closed the window): drop what this pass mounted.
                    if (control is not null) ReleaseControl(control);
                    return;
                }
                _currentTree = tree;
                _currentControl = control;
                if (!ReferenceEquals(Child, control)) Child = control;
            }
            // As upstream: the root's effects run once its tree is committed.
            phase = RenderErrorSource.Effects;
            ctx.FlushEffects();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (_disposed)
            {
                _logger?.LogError(ex, "Render failed ({Source}) after the host was disposed", phase);
                // A child component's failure the handler asked to propagate still leaves the dispatcher (#28).
                if (_reconciler.ConsumePropagation(ex)) ExceptionDispatchInfo.Capture(ex).Throw();
            }
            else
            {
                ShowRenderError(ex, phase);
            }
        }
        finally
        {
            _isRendering = false;
            Volatile.Write(ref _lastRenderMs, clock.Elapsed.TotalMilliseconds);
        }
    }

    // Upstream's host-level fallback (RenderErrorDispatch.BuildHostFallback), for one host.
    private void ShowRenderError(Exception ex, RenderErrorSource source)
    {
        if (_reconciler.ConsumePropagation(ex))
        {
            // A child component's failure the handler asked to propagate (#28): as for the host's own, nothing is shown,
            // and the handler isn't asked again.
            _logger?.LogError(ex, "Render failed ({Source}); propagated by the render-error handler", source);
            ReleaseTree();
            Child = null;
            ExceptionDispatchInfo.Capture(ex).Throw();
        }
        _logger?.LogError(ex, "Render failed ({Source})", source);
        var error = new RenderError(ex, source, componentName: null, isHostLevel: true);
        // A reconcile that threw part-way left the tree in an unknown state: release it rather than diff against it.
        if (source == RenderErrorSource.Reconcile) ReleaseTree();
        Element? appElement = null;
        bool handlerFailed = false;
        if (EffectiveRenderErrorHandler is { } handler)
        {
            try
            {
                appElement = handler(error);
            }
            catch (Exception handlerEx) when (handlerEx is not OutOfMemoryException and not StackOverflowException)
            {
                // A handler that throws after Propagate() did not finish deciding: the safe panel instead.
                error.CancelPropagation();
                handlerFailed = true;
                _logger?.LogError(handlerEx, "RenderErrorHandler threw; showing the neutral fallback");
            }
        }

        if (error.IsPropagationRequested)
        {
            ReleaseTree();
            Child = null;
            ExceptionDispatchInfo.Capture(ex).Throw();
        }
        if (handlerFailed)
        {
            ReleaseTree();
            Child = SafePanel();
            return;
        }
        if (appElement is not null)
        {
            try
            {
                // Reconciled against the current tree, so a repeated failure updates the fallback in place and the
                // replaced tree is unmounted (its cleanups run). Guarded as upstream's: a component in it that throws
                // gets the neutral text rather than a second call to the handler (#28).
                var guarded = Reconciler.GuardFallback(appElement);
                var control = _reconciler.Reconcile(_currentTree, guarded, _currentControl, _requestRender);
                _currentTree = guarded;
                _currentControl = control;
                Child = control;
            }
            catch (Exception mountEx) when (mountEx is not OutOfMemoryException and not StackOverflowException)
            {
                // The reconcile failed part-way: the tree's state is unknown, so it is forgotten, not unmounted again.
                _logger?.LogError(mountEx, "RenderErrorHandler fallback failed to mount; showing the neutral fallback");
                _currentTree = null;
                _currentControl = null;
                Child = SafePanel();
            }
            return;
        }
        // The built-in panel. A tree the pass kept stays for the next good pass to reconcile against, but a handler's
        // fallback from an earlier failure is released rather than left mounted out of sight (as upstream's).
        if (Reconciler.IsGuardedFallback(_currentTree)) ReleaseTree();
        Child = ErrorPanel(ex);
    }

    // Unmounts the current tree (best effort: the error being handled matters more than a failed teardown).
    private void ReleaseTree()
    {
        var control = _currentControl;
        _currentTree = null;
        _currentControl = null;
        if (control is not null) ReleaseControl(control);
    }

    private void ReleaseControl(Control control)
    {
        var failures = new List<Exception>();
        try
        {
            using (_reconciler.IsolateUnmountCleanupFailures(failures.Add))
                _reconciler.UnmountChild(control);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            failures.Add(ex);
        }
        foreach (var failure in failures)
            _logger?.LogError(failure, "Unmounting a released tree threw; the remaining cleanups still ran");
    }

    // Runs the root's cleanups (each isolated), then unmounts its tree, and forgets the root. Returns the failures.
    private List<Exception> RetireRoot()
    {
        var failures = new List<Exception>();
        var ctx = _rootContext;
        var control = _currentControl;
        _rootRender = null;
        _rootContext = null;
        _currentTree = null;
        _currentControl = null;
        Child = null;
        ctx?.RunCleanupsIsolated(failures.Add);
        if (control is not null)
        {
            try
            {
                using (_reconciler.IsolateUnmountCleanupFailures(failures.Add))
                    _reconciler.UnmountChild(control);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add(ex);
            }
        }
        return failures;
    }

    // Each failure goes to the handler as a Cleanup error (its return value is ignored). With no handler, or one that
    // asks to propagate, the first such failure is returned for the caller to rethrow.
    private ExceptionDispatchInfo? ReportCleanupFailures(List<Exception> failures)
    {
        ExceptionDispatchInfo? rethrow = null;
        foreach (var failure in failures)
        {
            _logger?.LogError(failure, "An effect cleanup threw while the root was released");
            if (EffectiveRenderErrorHandler is not { } handler)
            {
                rethrow ??= ExceptionDispatchInfo.Capture(failure);
                continue;
            }
            var error = new RenderError(failure, RenderErrorSource.Cleanup, componentName: null, isHostLevel: true);
            try
            {
                handler(error);
            }
            catch (Exception handlerEx) when (handlerEx is not OutOfMemoryException and not StackOverflowException)
            {
                error.CancelPropagation();
                _logger?.LogError(handlerEx, "RenderErrorHandler threw while reporting a cleanup failure");
            }
            if (error.IsPropagationRequested) rethrow ??= ExceptionDispatchInfo.Capture(failure);
        }
        return rethrow;
    }

    /// <summary>The text of the built-in error panel's message for <paramref name="ex"/>.</summary>
    internal static string ErrorText(Exception ex) => "Render error\n\n" + ex;

    /// <summary>The neutral message shown when the error handler itself failed (no exception details).</summary>
    internal const string SafeText = Reconciler.NeutralFallbackText;

    private static Control ErrorPanel(Exception ex) => new Border
    {
        Padding = new Avalonia.Thickness(12),
        BorderBrush = Brushes.IndianRed,
        BorderThickness = new Avalonia.Thickness(2),
        Child = new ScrollViewer
        {
            Content = new SelectableTextBlock { Text = ErrorText(ex), TextWrapping = TextWrapping.Wrap },
        },
    };

    private static Control SafePanel() => new TextBlock
    {
        Text = SafeText,
        Margin = new Avalonia.Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
