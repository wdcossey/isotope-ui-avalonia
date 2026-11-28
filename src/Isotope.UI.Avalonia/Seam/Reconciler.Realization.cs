using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace Isotope.UI.Core;

// Items mounted outside the traversal (#102): a list box mounts an element item when Avalonia realises its container,
// during layout. As upstream's realization frame (Reconciler.EnterRealization, for its ListView and LazyVStack rows), the
// mount replays the provider stack the list box was last reconciled under, and sets aside the pass's error-boundary
// bookkeeping, so a row that throws gets its own in-tree fallback and isn't rolled back with an unrelated subtree.
public sealed partial class Reconciler
{
    private sealed class ItemsOwner;

    // On a control mounted for an item of a list box (#102): the list box. The item's control is shown by a container
    // Avalonia recycles, so it has no parent while it isn't realised: the dirty path goes on from the list box
    // (DirtyPathParent), which reaches the item's control through its items.
    internal static readonly AttachedProperty<Control?> ItemsOwnerProperty =
        AvaloniaProperty.RegisterAttached<ItemsOwner, Control, Control?>("IsotopeItemsOwner");

    /// <summary>Records the live provider stack in <paramref name="capture"/> (a host reached by the traversal does).</summary>
    internal void RefreshContextCapture(ContextCapture capture) => capture.Refresh(_contextScope);

    /// <summary>
    /// Opens a realization frame for mounting an item outside (or re-entrantly inside) the traversal: until it is disposed,
    /// the live provider stack is <paramref name="capture"/>'s, and the in-flight pass's error-boundary and provider-change
    /// state is set aside. A null capture leaves the provider stack as it is.
    /// </summary>
    internal RealizationFrame EnterRealization(ContextCapture? capture) => new(this, capture);

    /// <summary>
    /// Mounts an item outside the traversal (in a realization frame): a mount that throws, outside a component's render
    /// too (a handler, a setter), gets the in-tree fallback in the item's place, as a component's render does, rather than
    /// leaving the layout pass. An exception the app's handler asked to propagate (or a fatal one) still leaves.
    /// </summary>
    internal UIElement? MountItem(Element element, Action requestRerender)
    {
        try
        {
            return Mount(element, requestRerender);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException && !IsPropagating(ex))
        {
            _logger?.LogError(ex, "A list item threw when it was mounted: {ElementType}", element.GetType().Name);
            return Mount(BuildInTreeFallback(ex, inEffects: false, componentName: null), requestRerender);
        }
    }

    internal readonly struct RealizationFrame : IDisposable
    {
        private readonly Reconciler _owner;
        private readonly List<(ContextBase Context, object? Value)>? _previousStack;
        private readonly List<UIElement>? _previousRoots;
        private readonly List<ComponentNode>? _previousAbandoned;
        private readonly int _previousBoundaryDepth;
        private readonly int _previousProviderChangeDepth;
        private readonly Exception? _previousPropagating;

        internal RealizationFrame(Reconciler owner, ContextCapture? capture)
        {
            _owner = owner;
            _previousRoots = owner._boundaryMountRoots;
            _previousAbandoned = owner._boundaryAbandonedNodes;
            _previousBoundaryDepth = owner._errorBoundaryDepth;
            _previousProviderChangeDepth = owner._providerChangeDepth;
            _previousPropagating = owner._propagating;
            owner._boundaryMountRoots = null;
            owner._boundaryAbandonedNodes = null;
            owner._errorBoundaryDepth = 0;
            owner._providerChangeDepth = 0;
            _previousStack = capture is not null ? owner._contextScope.Replace(capture.Entries, capture.Generation) : null;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null) return;
            if (_previousStack is not null) owner._contextScope.Restore(_previousStack);
            owner._boundaryMountRoots = _previousRoots;
            owner._boundaryAbandonedNodes = _previousAbandoned;
            owner._errorBoundaryDepth = _previousBoundaryDepth;
            owner._providerChangeDepth = _previousProviderChangeDepth;
            owner._propagating = _previousPropagating;
        }
    }
}
