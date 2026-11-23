namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// Per-binding event helper handed to element handlers by <c>MountContext.BindFor</c> (upstream's
/// <c>Core/V1Protocol/ReactorBindingT.cs</c>, tier 3). Keeps upstream's member names for what the descriptor
/// runtime calls: <see cref="Control"/>, <see cref="OnCustomEvent{TArgs}"/> and <see cref="WriteSuppressed"/>.
/// </summary>
/// <remarks>
/// Upstream's typed families for WinUI routed input (pointer, tap, key, focus) and its reference edges are not
/// here yet: they take WinUI event-argument types and element refs, and come with Avalonia's input vocabulary.
/// </remarks>
public readonly struct ReactorBinding<TElement> where TElement : Element
{
    private readonly Reconciler _reconciler;
    private readonly FrameworkElement _control;
    private readonly TElement _element;

    internal ReactorBinding(Reconciler reconciler, FrameworkElement control, TElement element)
    {
        _reconciler = reconciler;
        _control = control;
        _element = element;
    }

    /// <summary>The control the binding is anchored to.</summary>
    public FrameworkElement Control => _control;

    /// <summary>
    /// Subscribes to a plain CLR event of the control. The trampoline drops an echo of the engine's own write
    /// (see <see cref="WriteSuppressed"/>), then calls <paramref name="handler"/> with the element the control
    /// currently represents, so the subscription survives re-renders. Call it once per mount.
    /// </summary>
    public void OnCustomEvent<TArgs>(
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<FrameworkElement, EventHandler<TArgs>> unsubscribe,
        Action<TElement, TArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        ArgumentNullException.ThrowIfNull(handler);

        var fe = _control;
        EventHandler<TArgs> trampoline = (sender, args) =>
        {
            if (!Reconciler.TryGetReactorState(fe, out var state)) return;
            if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
            if (state.Element is TElement el)
                handler(el, args);
        };
        subscribe(fe, trampoline);
        Reconciler.AnchorCustomEventTrampoline(fe, trampoline);
    }

    /// <summary>Runs <paramref name="mutate"/> so the change event it raises doesn't reach the element's callback.</summary>
    public void WriteSuppressed(Action mutate) => ReactorBinding.WriteSuppressed(_control, mutate);
}
