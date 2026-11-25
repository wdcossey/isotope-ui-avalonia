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

    // On that control, its owner: the tooltip isn't the owner's child, so the walks up and down the tree that must see
    // it as part of its owner (the dirty path, the context check) go through these two.
    private static readonly AttachedProperty<Control?> ToolTipOwnerProperty =
        AvaloniaProperty.RegisterAttached<ToolTipOwner, Control, Control?>("IsotopeToolTipOwner");

    /// <summary>The element tooltip this reconciler mounted on <paramref name="control"/>, or null.</summary>
    internal static Control? OwnedToolTipOf(UIElement control) => control.GetValue(OwnedToolTipProperty);

    /// <summary>
    /// The next control up from <paramref name="child"/> on a dirty path: the owner of an element tooltip, else the
    /// logical parent.
    /// </summary>
    private static Control? DirtyPathParent(UIElement child, out bool viaToolTip)
    {
        if (child.GetValue(ToolTipOwnerProperty) is { } owner)
        {
            viaToolTip = true;
            return owner;
        }
        viaToolTip = false;
        // A control in a text block's inlines (#65) has its inline UI container as its parent, inside spans, inside the
        // text block: the path goes on from the text block.
        var parent = child.Parent;
        while (parent is not null and not Control) parent = parent.Parent;
        return parent as Control;
    }

    /// <summary>The text tooltip of an element: its modifier's, else its command's description (#29).</summary>
    private static string? TextToolTipOf(Element? element, ElementModifiers? modifiers)
        => modifiers?.ToolTip ?? CommandBindings.CommandOf(element)?.Description;

    /// <summary>
    /// Writes the element tooltip: mounts it, reconciles it with the last one, or unmounts it and gives the slot back to
    /// the text tooltip.
    /// </summary>
    private void ApplyRichToolTip(UIElement control, Element? oldEl, Element newEl, ElementModifiers? oldM, ElementModifiers? newM,
        Action requestRerender)
    {
        var oldTip = oldM?.RichToolTip;
        var newTip = newM?.RichToolTip;
        if (oldTip is null && newTip is null) return;
        if (newTip is null)
        {
            // Its tip is cleared only if it is still the one it mounted; the text tooltip is written after the setters, as
            // the other modifiers are.
            UnmountOwnedToolTip(control);
            WriteTextToolTip(control, oldEl, oldM, newEl, newM);
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
            // The tooltip rendered nothing (ReconcileSlot unmounted what it had): the text tooltip, if any, takes the slot.
            control.ClearValue(OwnedToolTipProperty);
            if (owned is not null)
            {
                owned.ClearValue(ToolTipOwnerProperty);
                if (ReferenceEquals(ToolTip.GetTip(control), owned)) control.ClearValue(ToolTip.TipProperty);
            }
            WriteTextToolTip(control, oldEl, oldM, newEl, newM);
            return;
        }
        if (!ReferenceEquals(owned, next))
        {
            owned?.ClearValue(ToolTipOwnerProperty);
            control.SetValue(OwnedToolTipProperty, next);
            next.SetValue(ToolTipOwnerProperty, (Control)control);
        }
        if (!ReferenceEquals(ToolTip.GetTip(control), next)) control.SetValue(ToolTip.TipProperty, next);
    }

    // The text tooltip, when the element tooltip leaves it the slot: written when there is one (after the setters, as the
    // other modifiers are), else the last one cleared if it is still there (an element tooltip that rendered nothing
    // left it in place).
    private static void WriteTextToolTip(UIElement control, Element? oldEl, ElementModifiers? oldM, Element newEl, ElementModifiers? newM)
    {
        if (TextToolTipOf(newEl, newM) is { } text) control.SetValue(ToolTip.TipProperty, text);
        else if (TextToolTipOf(oldEl, oldM) is { } oldText && Equals(ToolTip.GetTip(control), oldText)) control.ClearValue(ToolTip.TipProperty);
    }

    /// <summary>Unmounts the element tooltip this reconciler mounted on <paramref name="control"/>, if any.</summary>
    private void UnmountOwnedToolTip(UIElement control)
    {
        if (control.GetValue(OwnedToolTipProperty) is not { } tip) return;
        control.ClearValue(OwnedToolTipProperty);
        tip.ClearValue(ToolTipOwnerProperty);
        UnmountChild(tip);
        if (ReferenceEquals(ToolTip.GetTip(control), tip)) control.ClearValue(ToolTip.TipProperty);
    }
}
