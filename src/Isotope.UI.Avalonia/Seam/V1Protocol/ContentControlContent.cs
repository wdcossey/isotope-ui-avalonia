using Avalonia;
using Avalonia.Controls;

namespace Isotope.UI.Core.V1Protocol.Descriptor;

/// <summary>
/// Content for the button family (#26): a real child subtree or the string, as upstream's
/// <c>Core/V1Protocol/Descriptor/ContentControlContent.cs</c> (tier 3: it registers a WinUI attached property). The
/// owned child is recorded on the control, in an Avalonia attached property, so the next update reconciles it in place
/// and a component inside keeps its state.
/// </summary>
/// <remarks>
/// Unlike upstream's, it doesn't tag controls for teardown: this reconciler records the element on every control it
/// mounts, so the strategy's <c>Unmount</c> always runs; and there are no mount, update or unmount action modifiers on
/// Avalonia yet to displace the child.
/// </remarks>
internal static class ContentControlContent
{
    private sealed class ContentState
    {
        internal Control? Child;
    }

    private static readonly AttachedProperty<ContentState?> OwnedContentProperty =
        AvaloniaProperty.RegisterAttached<ContentState, ContentControl, ContentState?>("ReactorOwnedContent");

    private static ContentState? GetOwnedContent(ContentControl control) => control.GetValue(OwnedContentProperty);

    /// <summary>
    /// The control's content: <paramref name="getChild"/>'s element when there is one, mounted and reconciled as a child,
    /// else <paramref name="getText"/>'s string. A record using it marks its text member <c>[WrapManual]</c>.
    /// </summary>
    internal static ControlDescriptor<TElement, TControl> WithContent<TElement, TControl>(
        this ControlDescriptor<TElement, TControl> descriptor,
        Func<TElement, Element?> getChild,
        Func<TElement, string?> getText)
        where TElement : Element
        where TControl : ContentControl, new()
    {
        // As upstream's: the strategy owns reconciliation and removal of the declared child. Setters run between the
        // text bridge below and this strategy, so a declared element wins while an undeclared slot leaves a setter's
        // content alone.
        descriptor.Children = new Imperative<TElement, TControl>((ctx, oldElement, newElement, control) =>
        {
            if (getChild(newElement) is not { } newChild) return;
            var state = GetOwnedContent(control);
            var hadContent = state is not null;
            if (state is null)
            {
                state = new ContentState();
                control.SetValue(OwnedContentProperty, state);
            }
            state.Child = ctx.ReconcileChild(hadContent ? getChild(oldElement) : null, newChild, state.Child);
            if (!ReferenceEquals(control.Content, state.Child)) control.Content = state.Child;
        })
        {
            Unmount = static (ctx, control) => UnmountOwnedContent(ctx.Reconciler, control),
        };

        return descriptor.ImperativeBridged(
            mount: (_, control, element) =>
            {
                control.ClearValue(OwnedContentProperty);
                control.Content = getChild(element) is null ? getText(element) : null;
            },
            update: (ctx, control, oldElement, newElement) =>
            {
                var oldChild = getChild(oldElement);
                var newChild = getChild(newElement);
                if (newChild is not null)
                {
                    // A declared child may render no control: clear the old string before the reconcile, keeping an
                    // existing subtree.
                    if (oldChild is null && control.Content is not Control)
                        control.Content = null;
                    return;
                }
                if (oldChild is not null)
                {
                    // Tear down a declared child before the text takes its slot and the setters run.
                    UnmountOwnedContent(ctx.Reconciler, control);
                    control.Content = getText(newElement);
                }
                else if (!string.Equals(getText(oldElement), getText(newElement), StringComparison.Ordinal))
                {
                    control.Content = getText(newElement);
                }
            });
    }

    private static void UnmountOwnedContent(Reconciler reconciler, ContentControl control)
    {
        if (GetOwnedContent(control) is not { } state) return;
        control.ClearValue(OwnedContentProperty);
        if (state.Child is not { } child) return;
        reconciler.UnmountChild(child);
        if (ReferenceEquals(control.Content, child)) control.Content = null;
    }
}
