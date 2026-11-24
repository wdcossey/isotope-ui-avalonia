using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;

namespace Isotope.UI.Core;

// Error boundaries (#25), after upstream's MountErrorBoundary and UpdateErrorBoundary (Reconciler.Mount.cs,
// Reconciler.Update.cs) and the rollback of a failed boundary child (Reconciler.cs, upstream issue #1291).
public sealed partial class Reconciler
{
    /// <summary>What a mounted boundary rendered last, by its wrapper.</summary>
    internal sealed class ErrorBoundaryNode
    {
        /// <summary>The child element of the boundary's current element.</summary>
        public Element ChildElement { get; set; } = null!;
        /// <summary>The element the wrapper holds: the child, or the fallback the child's failure produced.</summary>
        public Element? RenderedElement { get; set; }
        /// <summary>The exception the last mount or retry caught; null when the child rendered.</summary>
        public Exception? CaughtException { get; set; }
        /// <summary>The fallback of the boundary's current element.</summary>
        public Func<Exception, Element> Fallback { get; set; } = null!;
    }

    private readonly Dictionary<UIElement, ErrorBoundaryNode> _errorBoundaryNodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>The number of mounted error boundaries (diagnostics and tests).</summary>
    internal int ErrorBoundaryNodeCount => _errorBoundaryNodes.Count;

    /// <summary>Tries to find the boundary node a wrapper control belongs to.</summary>
    internal bool TryFindErrorBoundaryNode(UIElement control, [NotNullWhen(true)] out ErrorBoundaryNode? node)
        => _errorBoundaryNodes.TryGetValue(control, out node);

    // While a boundary mounts or retries its child (null otherwise, so ordinary mounts pay nothing):
    // - the controls whose Mount completed, collapsed as they nest: a completed mount replaces the roots its children
    //   left with its own control, which reaches them through the unmount walk. After a failure, the roots past the
    //   boundary's start are the completed subtrees the failed child left behind, attached to nothing that survives.
    // - the component nodes whose mount failed: their render ran (and flushed its effects) but no wrapper was
    //   registered, so no unmount can reach them. The boundary runs their cleanups.
    // Nested boundaries share both lists; each one owns what lies past its start.
    private List<UIElement>? _boundaryMountRoots;
    private List<ComponentNode>? _boundaryAbandonedNodes;

    private readonly record struct BoundaryMountRecord(int RootsStart, int AbandonedStart, bool OwnsLists);

    private BoundaryMountRecord BeginBoundaryMount()
    {
        bool owns = _boundaryMountRoots is null;
        _boundaryMountRoots ??= new List<UIElement>();
        _boundaryAbandonedNodes ??= new List<ComponentNode>();
        return new BoundaryMountRecord(_boundaryMountRoots.Count, _boundaryAbandonedNodes.Count, owns);
    }

    // What the boundary's child left stays recorded for an enclosing boundary: if that one's child fails later, it is
    // discarded with it.
    private void EndBoundaryMount(BoundaryMountRecord record)
    {
        if (!record.OwnsLists) return;
        _boundaryMountRoots = null;
        _boundaryAbandonedNodes = null;
    }

    private bool HasBoundaryMountLeftovers(BoundaryMountRecord record)
        => (_boundaryMountRoots?.Count ?? 0) > record.RootsStart || (_boundaryAbandonedNodes?.Count ?? 0) > record.AbandonedStart;

    /// <summary>Where a mount's completed roots start while a boundary records them; -1 when none does.</summary>
    private int BoundaryRootsMark => _boundaryMountRoots?.Count ?? -1;

    /// <summary>A mount completed: its control replaces the roots its children recorded. No-op unless a boundary records.</summary>
    private void NoteMountCompleted(int rootsStart, UIElement? control)
    {
        if (_boundaryMountRoots is not { } roots || rootsStart < 0) return;
        if (roots.Count > rootsStart)
            roots.RemoveRange(rootsStart, roots.Count - rootsStart);
        if (control is not null)
            roots.Add(control);
    }

    /// <summary>A component's mount failed: inside a boundary, the boundary runs its cleanups when it discards the child.</summary>
    private void NoteAbandonedNode(ComponentNode node) => _boundaryAbandonedNodes?.Add(node);

    private UIElement MountErrorBoundary(ErrorBoundaryElement boundary, Action requestRerender)
    {
        var wrapper = new Border();
        Element rendered;
        Exception? caught = null;
        var record = BeginBoundaryMount();
        // Whether the wrapper ended up with a child (the real one or the fallback); only a mount that escapes before
        // then leaves something to discard in the finally.
        bool settled = false;
        try
        {
            rendered = boundary.Child;
            wrapper.Child = Mount(boundary.Child, requestRerender);
            settled = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ErrorBoundary caught render error");
            caught = ex;
            // The partly mounted child is discarded: its cleanups run now, then the fallback mounts in its place.
            RollBackBoundaryMount(record, attachedChild: null);
            rendered = boundary.Fallback(ex);
            wrapper.Child = Mount(rendered, requestRerender);
            settled = true;
        }
        finally
        {
            // A fallback that throws escapes to the next boundary out (or the host): discard what it left as well.
            if (!settled && HasBoundaryMountLeftovers(record))
                RollBackBoundaryMount(record, attachedChild: null);
            EndBoundaryMount(record);
        }
        _errorBoundaryNodes[wrapper] = new ErrorBoundaryNode
        {
            ChildElement = boundary.Child,
            RenderedElement = rendered,
            CaughtException = caught,
            Fallback = boundary.Fallback,
        };
        return wrapper;
    }

    // Every update retries the child (upstream: "Always retry the child on re-render"), against whatever the wrapper
    // holds: the child, or the fallback of an earlier failure.
    private void UpdateErrorBoundary(ErrorBoundaryElement boundary, Border wrapper, Action requestRerender)
    {
        var node = _errorBoundaryNodes[wrapper];
        var existing = wrapper.Child;
        Element rendered;
        Exception? caught = null;
        var record = BeginBoundaryMount();
        bool settled = false;
        try
        {
            rendered = boundary.Child;
            var next = ReconcileSlot(node.RenderedElement, boundary.Child, existing, requestRerender);
            if (!ReferenceEquals(next, existing)) wrapper.Child = next;
            settled = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ErrorBoundary caught render error during update");
            caught = ex;
            // The retried child is torn down (its cleanups run), and so is what the failed retry mounted but never
            // attached under it.
            if (existing is not null) UnmountIsolated(existing);
            RollBackBoundaryMount(record, existing);
            rendered = boundary.Fallback(ex);
            wrapper.Child = Mount(rendered, requestRerender);
            settled = true;
        }
        finally
        {
            if (!settled && HasBoundaryMountLeftovers(record))
                RollBackBoundaryMount(record, existing);
            EndBoundaryMount(record);
        }
        node.ChildElement = boundary.Child;
        node.RenderedElement = rendered;
        node.CaughtException = caught;
        node.Fallback = boundary.Fallback;
    }

    /// <summary>
    /// Discards what a failed boundary child left behind: unmounts the subtrees that completed (deepest first), then
    /// runs the cleanups of the components whose mount failed. <paramref name="attachedChild"/> is the boundary's
    /// previous child on a retry, which the caller unmounts itself, so completed subtrees attached under it are left to
    /// that unmount. Every entry is processed; a teardown that throws is logged and the rest still run, because the
    /// error being handled matters more.
    /// </summary>
    private void RollBackBoundaryMount(BoundaryMountRecord record, UIElement? attachedChild)
    {
        List<UIElement>? orphans = null;
        if (_boundaryMountRoots is { } roots && roots.Count > record.RootsStart)
        {
            orphans = roots.GetRange(record.RootsStart, roots.Count - record.RootsStart);
            roots.RemoveRange(record.RootsStart, orphans.Count);
        }
        List<ComponentNode>? abandoned = null;
        if (_boundaryAbandonedNodes is { } nodes && nodes.Count > record.AbandonedStart)
        {
            abandoned = nodes.GetRange(record.AbandonedStart, nodes.Count - record.AbandonedStart);
            nodes.RemoveRange(record.AbandonedStart, abandoned.Count);
        }
        if (orphans is not null)
        {
            for (int i = orphans.Count - 1; i >= 0; i--)
            {
                if (attachedChild is not null && IsAttachedUnder(orphans[i], attachedChild)) continue;
                UnmountIsolated(orphans[i]);
            }
        }
        if (abandoned is not null)
        {
            // Recorded as the exception unwound: the deepest first.
            foreach (var node in abandoned)
            {
                node.Component?.Context.RunCleanupsIsolated(LogRollbackFailure);
                node.Context?.RunCleanupsIsolated(LogRollbackFailure);
            }
        }
    }

    // Unmounts a discarded subtree, running every cleanup even when one throws.
    private void UnmountIsolated(UIElement control)
    {
        try
        {
            using (IsolateUnmountCleanupFailures(LogRollbackFailure))
                UnmountChild(control);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogRollbackFailure(ex);
        }
    }

    private void LogRollbackFailure(Exception ex)
        => _logger?.LogWarning(ex, "Teardown threw while discarding a failed ErrorBoundary child");

    // True when control sits in ancestor's subtree (logical parent first, then visual).
    private static bool IsAttachedUnder(UIElement control, UIElement ancestor)
    {
        for (Visual? current = control; current is not null; current = current.Parent as Visual ?? current.GetVisualParent())
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }
}
