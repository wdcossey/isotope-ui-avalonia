using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

namespace Isotope.UI.Core;

// #28: a component that fails outside every error boundary, after upstream's MountComponent, MountFuncComponent,
// MountMemoComponent and ReconcileComponent (their in-tree fallback arm), RenderErrorDispatch.BuildInTreeFallback and
// ErrorFallback.BuildElement; the rollback of a root pass that throws (as a boundary rolls back its child, #25); and
// isolated control-handler teardown, after upstream's RunIsolatedTeardown (Reconciler.cs, upstream issue #1291).
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════════
    //  The in-tree fallback
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Supplies the owning host's effective <see cref="RenderErrorHandler"/> (upstream's member). A reconciler without a
    /// host uses <see cref="ReactorApp.DefaultRenderErrorHandler"/>.
    /// </summary>
    internal Func<RenderErrorHandler?>? RenderErrorHandlerProvider { get; init; }

    private RenderErrorHandler? ResolveRenderErrorHandler() =>
        RenderErrorHandlerProvider is { } provider ? provider() : ReactorApp.DefaultRenderErrorHandler;

    // How many error boundaries the pass is inside (their child or their fallback). At zero, a component's failure is
    // shown in its own slot; otherwise it goes to the nearest boundary.
    private int _errorBoundaryDepth;

    // The exception an app's handler asked to propagate (RenderError.Propagate) in this pass: no boundary, component or
    // host catches it again. Cleared when the next root pass starts.
    private Exception? _propagating;

    /// <summary>Whether <paramref name="ex"/> is on its way out because the app's handler asked to propagate it.</summary>
    internal bool IsPropagating(Exception ex) => _propagating is not null && ReferenceEquals(_propagating, ex);

    /// <summary>
    /// Renders a component and flushes its effects. Outside every error boundary, an exception from either is shown in
    /// the component's slot: the result is then the fallback for it (see <see cref="BuildInTreeFallback"/>), and the
    /// component stays mounted, so its next render retries it. Inside a boundary, or for a fatal error, it propagates.
    /// </summary>
    private Element RenderComponent(ComponentNode node, RenderContext ctx, Action componentRerender, Func<Element> render)
    {
        bool inEffects = false;
        try
        {
            ctx.BeginRender(componentRerender, _contextScope, Owner);
            var element = render() ?? EmptyElement.Instance;
            inEffects = true;
            ctx.FlushEffects();
            return element;
        }
        catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException
            && !IsPropagating(ex))
        {
            _logger?.LogError(ex, "Component render threw: {ComponentName}", Diagnostics.ComponentNames.For(node.Component, node.Element));
            // As upstream: the class component's type name; none for a function or memo component.
            return BuildInTreeFallback(ex, inEffects, node.Component?.GetType().Name);
        }
    }

    /// <summary>
    /// What a failed component's slot shows, as upstream's <c>RenderErrorDispatch.BuildInTreeFallback</c>: the host's
    /// handler is told (<see cref="RenderError.IsHostLevel"/> false). With no handler, or one that returns null, the
    /// built-in placeholder with the exception text; with an element, that element, inside a boundary whose fallback
    /// is the neutral text, so a fallback that throws doesn't come back to the handler; with a handler that throws, the
    /// neutral text. A handler that calls <see cref="RenderError.Propagate"/> gets the exception rethrown, marked so
    /// nothing catches it again.
    /// </summary>
    private Element BuildInTreeFallback(Exception ex, bool inEffects, string? componentName)
    {
        if (ResolveRenderErrorHandler() is not { } handler)
            return BuiltInFallback(ex);
        var error = new RenderError(ex, inEffects ? RenderErrorSource.Effects : RenderErrorSource.ComponentRender,
            componentName, isHostLevel: false);
        Element? appElement;
        try
        {
            appElement = handler(error);
        }
        catch (Exception handlerEx) when (handlerEx is not OutOfMemoryException and not StackOverflowException
            && !IsPropagating(handlerEx))
        {
            // A handler that throws after Propagate() did not finish deciding: the neutral text instead.
            error.CancelPropagation();
            _logger?.LogError(handlerEx, "RenderErrorHandler threw while handling {ExceptionType}; showing the neutral fallback",
                ex.GetType().Name);
            return NeutralFallback(ex);
        }
        if (error.IsPropagationRequested)
        {
            _propagating = ex;
            ExceptionDispatchInfo.Capture(ex).Throw();
        }
        return appElement is null ? BuiltInFallback(ex) : new ErrorBoundaryElement(appElement, s_neutralFallback);
    }

    /// <summary>The text of the built-in placeholder for <paramref name="ex"/> (upstream's wording).</summary>
    internal static string FallbackText(Exception ex) => $"⚠ Render error: {ex.GetType().Name}: {ex.Message}\n\n{ex}";

    /// <summary>The neutral text shown when the app's handler failed (no exception details), as the host's.</summary>
    internal const string NeutralFallbackText = "Something went wrong.";

    private static readonly Action<TextBlock>[] s_wrapText = [static t => t.TextWrapping = TextWrapping.Wrap];
    private static readonly Func<Exception, Element> s_neutralFallback = NeutralFallback;

    // Upstream's ErrorFallback.BuildElement: a scrollable, wrapping text with the exception's type, message and stack.
    private static Element BuiltInFallback(Exception ex)
        => new ScrollViewerElement(new TextBlockElement(FallbackText(ex)) { Setters = s_wrapText });

    private static Element NeutralFallback(Exception _) => new TextBlockElement(NeutralFallbackText) { Setters = s_wrapText };

    // ════════════════════════════════════════════════════════════════════
    //  The rollback of a root pass that throws
    // ════════════════════════════════════════════════════════════════════

    // A root pass records completed mounts and abandoned component nodes as a boundary does (Reconciler.ErrorBoundary.cs),
    // so what a pass that throws mounted but never attached is torn down; a boundary inside the pass records past the
    // root's start. Returns null when a pass is already recording (a reconcile nested in one).
    private BoundaryMountRecord? BeginRootPass()
    {
        if (_boundaryMountRoots is not null) return null;
        _propagating = null;
        return BeginBoundaryMount();
    }

    // The pass threw: the subtrees it mounted but never attached are unmounted, and the cleanups of components whose
    // mount failed run (each once, every one even when some throw). What is attached under existingControl, the tree
    // the pass started from, is left to whoever releases that tree (the host does).
    private void EndRootPass(BoundaryMountRecord? record, bool completed, UIElement? existingControl)
    {
        if (record is not { } r) return;
        try
        {
            if (!completed && HasBoundaryMountLeftovers(r))
                RollBackBoundaryMount(r, existingControl);
        }
        finally
        {
            EndBoundaryMount(r);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Isolated control-handler teardown
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether an <see cref="IsolateUnmountCleanupFailures"/> scope collects teardown failures. The handler adapter and
    /// descriptors then run every unmount hook and child teardown, and report what throws here.
    /// </summary>
    internal bool IsolatesUnmountCleanupFailures => _unmountCleanupFailures is not null;

    /// <summary>Hands a teardown failure to the active isolation scope (a control handler's or descriptor's unmount hook).</summary>
    internal void ReportIsolatedUnmountFailure(Exception ex) => _unmountCleanupFailures?.Invoke(ex);

    // While a control handler's teardown runs isolated: the controls it has torn down. If it throws part-way, the recovery
    // walk over its children skips those (_isolatedTeardownSkip), so no child teardown runs twice.
    private HashSet<UIElement>? _isolatedTeardownTrack;
    private HashSet<UIElement>? _isolatedTeardownSkip;

    // Entry of the unmount walk: true to skip a control the failed teardown being recovered already tore down; otherwise
    // the control is recorded for the running teardown.
    private bool SkipOrTrackIsolatedTeardown(UIElement control)
    {
        if (_isolatedTeardownSkip is { } skip && skip.Contains(control)) return true;
        _isolatedTeardownTrack?.Add(control);
        return false;
    }

    /// <summary>
    /// Runs a control handler's unmount inside an isolated teardown. A throw is reported (unless the handler reported its
    /// failures itself), and the children the handler didn't reach are torn down, each once; returns false then.
    /// </summary>
    private bool RunIsolatedTeardown(UIElement control, IV1HandlerEntry entry, out V1Protocol.V1UnmountDisposition disposition)
    {
        disposition = default;
        var outerTrack = _isolatedTeardownTrack;
        var track = _isolatedTeardownTrack = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        bool failed = false;
        try
        {
            disposition = entry.Unmount(control, this);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (ex is not V1Protocol.ReportedUnmountFailureException)
                ReportIsolatedUnmountFailure(ex);
            failed = true;
        }
        finally
        {
            _isolatedTeardownTrack = outerTrack;
        }
        if (!failed) return true;

        // An enclosing recovery's skips still apply.
        var outerSkip = _isolatedTeardownSkip;
        if (outerSkip is not null) track.UnionWith(outerSkip);
        _isolatedTeardownSkip = track;
        try
        {
            UnmountChildren(control);
        }
        finally
        {
            _isolatedTeardownSkip = outerSkip;
        }
        return false;
    }

    /// <summary>
    /// Runs <paramref name="teardown"/> (a handler's own child teardown after its unmount hook failed) skipping the
    /// controls the running teardown already tore down, so none is torn down twice.
    /// </summary>
    internal void SkippingIsolatedTornDown<TState>(TState state, Action<TState> teardown)
    {
        var outerSkip = _isolatedTeardownSkip;
        var skip = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        if (_isolatedTeardownTrack is { } track) skip.UnionWith(track);
        if (outerSkip is not null) skip.UnionWith(outerSkip);
        _isolatedTeardownSkip = skip;
        try
        {
            teardown(state);
        }
        finally
        {
            _isolatedTeardownSkip = outerSkip;
        }
    }
}
