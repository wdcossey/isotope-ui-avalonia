using Avalonia;
using Avalonia.Controls;

namespace Isotope.UI.Core;

// Element tooltips (#29): upstream's RichToolTip modifier (.WithToolTip(element)), as a child subtree on Avalonia's
// ToolTip.Tip. As upstream's, the subtree is reconciled in place on every update of its control (a component inside
// keeps its state), replaced when its element can't be updated, and unmounted when the modifier goes or the control is
// torn down. Unlike upstream's, the control it mounted is recorded on its owner, so a tip a setter wrote is never taken
// for it.
public sealed partial class Reconciler
{
    private sealed class ToolTipOwner;

    // The control this reconciler mounted for the owner's element tooltip.
    private static readonly AttachedProperty<Control?> OwnedToolTipProperty =
        AvaloniaProperty.RegisterAttached<ToolTipOwner, Control, Control?>("IsotopeOwnedToolTip");

    /// <summary>
    /// Writes the element tooltip: mounts it, reconciles it with the last one, or unmounts it and gives the slot back to
    /// the text tooltip.
    /// </summary>
    private void ApplyRichToolTip(UIElement control, ElementModifiers? oldM, ElementModifiers? newM, Action requestRerender)
    {
        var oldTip = oldM?.RichToolTip;
        var newTip = newM?.RichToolTip;
        if (oldTip is null && newTip is null) return;
        if (newTip is null)
        {
            // Its tip is cleared only if it is still the one it mounted; the text tooltip is written after the setters, as
            // the other modifiers are.
            UnmountOwnedToolTip(control);
            if (newM?.ToolTip is { } text) control.SetValue(ToolTip.TipProperty, text);
            return;
        }
        var owned = control.GetValue(OwnedToolTipProperty);
        if (owned is not null && oldTip is null)
        {
            // Nothing to diff it against: start afresh.
            UnmountOwnedToolTip(control);
            owned = null;
        }
        var next = ReconcileSlot(owned is null ? null : oldTip, newTip, owned, requestRerender);
        if (next is null)
        {
            // The tooltip rendered nothing (ReconcileSlot unmounted what it had).
            control.ClearValue(OwnedToolTipProperty);
            if (owned is not null && ReferenceEquals(ToolTip.GetTip(control), owned)) control.ClearValue(ToolTip.TipProperty);
            return;
        }
        if (!ReferenceEquals(owned, next)) control.SetValue(OwnedToolTipProperty, next);
        if (!ReferenceEquals(ToolTip.GetTip(control), next)) control.SetValue(ToolTip.TipProperty, next);
    }

    /// <summary>Unmounts the element tooltip this reconciler mounted on <paramref name="control"/>, if any.</summary>
    private void UnmountOwnedToolTip(UIElement control)
    {
        if (control.GetValue(OwnedToolTipProperty) is not { } tip) return;
        control.ClearValue(OwnedToolTipProperty);
        UnmountChild(tip);
        if (ReferenceEquals(ToolTip.GetTip(control), tip)) control.ClearValue(ToolTip.TipProperty);
    }
}
