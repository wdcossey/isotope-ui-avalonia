using System.Runtime.CompilerServices;
using Isotope.UI.Core.V1Protocol;

namespace Isotope.UI.Core;

/// <summary>
/// The members upstream's descriptor runtime (#12: <c>ControlDescriptor</c>, <c>PropEntry</c>, <c>DescriptorHandler</c>,
/// <c>MountContext</c>, <c>ChildrenStrategy</c>, <c>ControlRegistry</c>, <c>V1HandlerAdapter</c>) calls on the
/// reconciler, under upstream's names, and the dispatch of elements to the handlers registered in
/// <see cref="ControlRegistry"/>.
/// </summary>
/// <remarks>
/// Control pooling and stagger scopes are tier 3: controls are always created fresh and dropped on unmount, and a
/// stagger scope does nothing.
/// </remarks>
public sealed partial class Reconciler
{
    // The handler entry for each element type this reconciler has dispatched, or null for a type with none.
    private readonly Dictionary<Type, IV1HandlerEntry?> _v1Handlers = new();

    /// <summary>
    /// The registered handler for <paramref name="elementType"/>, or for the nearest base type that has one; resolved
    /// once per reconciler.
    /// </summary>
    internal bool TryGetV1Handler(Type elementType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IV1HandlerEntry? entry)
    {
        if (!_v1Handlers.TryGetValue(elementType, out entry))
        {
            // A record derived from a control's record (record MyText(string S) : TextBlockElement(S)) mounts as its
            // base, as the seam's type patterns did; upstream's registry matches the exact type only.
            for (var t = elementType; t is not null && t != typeof(Element); t = t.BaseType)
            {
                // A generated descriptor registers from its record's static constructor; make sure it has run.
                RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                if (ControlRegistry.TryResolve(t, out var factory))
                {
                    entry = factory();
                    break;
                }
            }
            _v1Handlers[elementType] = entry;
        }
        return entry is not null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element tags (upstream: ReactorAttached state on the control)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The element <paramref name="control"/> currently represents, as its event trampolines read it.</summary>
    public static Element? GetElementTag(UIElement control) => control.GetValue(ReactorAttached.StateProperty)?.Element;

    /// <summary>
    /// Records <paramref name="element"/> on the control. Upstream tags only controls that need it (callbacks, keys,
    /// extensions); this reconciler records every mounted element, so this always writes.
    /// </summary>
    internal static void SetElementTagIfNeeded(FrameworkElement control, Element element, bool ownsTeardown)
        => GetOrCreateReactorState(control).Element = element;

    /// <summary>Native AOT diagnostics mode, which skips call-site-only tags. Not on Avalonia: never.</summary>
    internal static bool SkipsCallSiteOnlyTags => false;

    // ════════════════════════════════════════════════════════════════════
    //  Per-control event payloads (upstream: ControlEventStateBox)
    // ════════════════════════════════════════════════════════════════════

    private sealed class ControlEventStateBox
    {
        public required Type HandlerType;
        public required object Payload;
    }

    /// <summary>Keeps the trampolines <c>ReactorBinding.OnCustomEvent</c> subscribed alive with the control.</summary>
    private sealed class CustomEventAnchorPayload
    {
        public readonly List<Delegate> Trampolines = new();
    }

    internal static void AnchorCustomEventTrampoline(FrameworkElement fe, Delegate trampoline)
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is not ControlEventStateBox box || box.HandlerType != typeof(CustomEventAnchorPayload))
        {
            box = new ControlEventStateBox { HandlerType = typeof(CustomEventAnchorPayload), Payload = new CustomEventAnchorPayload() };
            state.ControlEventState = box;
        }
        ((CustomEventAnchorPayload)box.Payload).Trampolines.Add(trampoline);
    }

    /// <summary>The control's event payload of type <typeparamref name="T"/>, created on first use.</summary>
    internal static T GetOrCreateControlEventPayload<T>(FrameworkElement fe) where T : class, new()
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is ControlEventStateBox existing && existing.HandlerType == typeof(T))
            return (T)existing.Payload;
        var payload = new T();
        state.ControlEventState = new ControlEventStateBox { HandlerType = typeof(T), Payload = payload };
        return payload;
    }

    /// <summary>The control's event payload of type <typeparamref name="T"/>, or null; never creates one.</summary>
    internal static T? TryGetControlEventPayload<T>(FrameworkElement fe) where T : class
        => fe.GetValue(ReactorAttached.StateProperty) is { } state ? TryGetControlEventPayload<T>(state) : null;

    internal static T? TryGetControlEventPayload<T>(ReactorState state) where T : class
        => state.ControlEventState is ControlEventStateBox box && box.HandlerType == typeof(T) ? (T)box.Payload : null;

    // ════════════════════════════════════════════════════════════════════
    //  Controls, setters, child slots
    // ════════════════════════════════════════════════════════════════════

    /// <summary>A control for a handler to mount: <paramref name="factory"/>'s, or a new one. Pooling is tier 3.</summary>
    public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class, new()
        => factory is not null ? factory() : new T();

    /// <summary>Gives a control back to its pool. Pooling is tier 3, so the control is left to the GC.</summary>
    public void ReturnControl<T>(T control, PoolPolicy<T>? policy = null) where T : class
        => ArgumentNullException.ThrowIfNull(control);

    /// <summary>
    /// Runs each setter on <paramref name="control"/> inside the echo-suppression scope, so a value a setter writes
    /// doesn't echo back through the element's change callback (upstream's contract).
    /// </summary>
    public static void ApplySetters<T>(Action<T>[] setters, T control) where T : class
    {
        if (setters.Length == 0) return;
        ReactorState? state = control is FrameworkElement fe ? fe.GetValue(ReactorAttached.StateProperty) : null;
        if (state is not null) state.EchoSuppressScopeDepth++;
        try
        {
            foreach (var setter in setters) setter(control);
        }
        finally
        {
            if (state is not null) state.EchoSuppressScopeDepth--;
        }
    }

    /// <summary>
    /// Reconciles a secondary element slot (single content, a named slot): mounts, updates in place, or unmounts,
    /// keeping descendant component state. Returns the control the slot holds afterwards.
    /// </summary>
    internal UIElement? ReconcileV1Child(Element? oldChild, Element? newChild, UIElement? existing, Action requestRerender)
    {
        if (newChild is null)
        {
            if (existing is not null) UnmountChild(existing);
            return null;
        }
        if (oldChild is not null && existing is not null && CanUpdate(oldChild, newChild))
        {
            var replacement = UpdateChildTracked(oldChild, newChild, existing, requestRerender, out var unmountedByUpdate);
            if (replacement is null || ReferenceEquals(replacement, existing)) return existing;
            if (!unmountedByUpdate) UnmountChild(existing);
            return replacement;
        }
        if (existing is not null) UnmountChild(existing);
        return Mount(newChild, requestRerender);
    }

    /// <summary>
    /// Keyed reconcile of a panel strategy's children through upstream's ChildReconciler. Moves go through
    /// <see cref="MovingPanelChildCollection"/>, so a moved control stays in the visual and logical trees (#23).
    /// </summary>
    internal void ReconcilePanelChildrenInto(Element[] oldChildren, Element[] newChildren, UIElementCollection collection,
        Action requestRerender, UIElement? parentControl = null)
        => ChildReconciler.Reconcile(Flatten(oldChildren), Flatten(newChildren), new MovingPanelChildCollection(collection),
            this, requestRerender, parentControl);

    // ════════════════════════════════════════════════════════════════════
    //  Scopes
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Provides <paramref name="value"/> for <paramref name="context"/> until the result is disposed.</summary>
    internal IDisposable PushContextDisposable<T>(Context<T> context, T value)
    {
        _contextScope.Push(new Dictionary<ContextBase, object?>(1) { [context] = value });
        return new PopOnDispose(_contextScope);
    }

    private sealed class PopOnDispose(ContextScope scope) : IDisposable
    {
        private ContextScope? _scope = scope;

        public void Dispose()
        {
            _scope?.Pop(1);
            _scope = null;
        }
    }

    /// <summary>A stagger scope for children's enter transitions. Transitions are tier 3: it does nothing.</summary>
    internal IDisposable PushStaggerScopeDisposable(TimeSpan delay) => NoScope.Instance;

    private sealed class NoScope : IDisposable
    {
        internal static readonly NoScope Instance = new();
        public void Dispose() { }
    }

    /// <summary>True during a theme-resource refresh pass (Theme.NotifyResourcesChanged, #41; Reconciler.Theme.cs).</summary>
    internal bool ResourceRefreshActive => _resourceRefreshActive;

    // Unmount isolation (IsolatesUnmountCleanupFailures and the members the handler adapter calls) is in
    // Reconciler.ComponentErrors.cs (#28).
}
