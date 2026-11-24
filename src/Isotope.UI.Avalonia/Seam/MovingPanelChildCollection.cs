using System.Diagnostics;

namespace Isotope.UI.Core;

/// <summary>
/// The child collection a panel's keyed reconcile edits (#23): upstream's <c>PanelChildCollection</c> (vendored
/// <c>Core/ChildCollection.cs</c>), except that <see cref="Move"/> is Avalonia's <c>Controls.Move</c>.
/// </summary>
/// <remarks>
/// Upstream moves a child with <c>RemoveAt</c> then <c>Insert</c>. On Avalonia that takes the control out of the visual
/// and logical trees and attaches it again, so it is restyled, re-measured, gets <c>Unloaded</c> and <c>Loaded</c>, and a
/// focused control in it loses focus. <c>Controls.Move</c> raises a single Move change, which <c>Panel</c> applies by
/// moving its logical and visual children, so nothing leaves the trees (Spike I: reversing 1,000 keyed rows in a
/// headless window fell from about 200 ms to about 10 ms).
/// </remarks>
internal sealed class MovingPanelChildCollection(UIElementCollection children) : IChildCollection
{
    public int Count => children.Count;
    public UIElement Get(int index) => children[index];
    public int IndexOf(UIElement element) => children.IndexOf(element);

    // Upstream's counts of the edits made through this wrapper (each reconcile of a container gets a fresh one), kept for
    // parity: upstream's perf ChildReconcile span reads them, and nothing here does until that span is ported.
    internal uint InsertCount { get; private set; }
    internal uint RemoveCount { get; private set; }
    internal uint MoveCount { get; private set; }
    internal uint ReplaceCount { get; private set; }

    public void Insert(int index, UIElement element)
    {
        children.Insert(index, element);
        InsertCount++;
    }

    public void RemoveAt(int index)
    {
        children.RemoveAt(index);
        RemoveCount++;
    }

    /// <summary>
    /// Moves the child at <paramref name="oldIndex"/> so that it ends up at <paramref name="newIndex"/>, upstream's
    /// contract (<c>AvaloniaList.Move</c> removes the item, then inserts it at <paramref name="newIndex"/>).
    /// </summary>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        Debug.Assert(oldIndex >= 0 && oldIndex < children.Count, $"oldIndex {oldIndex} out of range [0, {children.Count})");
        Debug.Assert(newIndex >= 0 && newIndex < children.Count, $"newIndex {newIndex} out of range [0, {children.Count})");
        children.Move(oldIndex, newIndex);
        MoveCount++;
    }

    // As upstream: RemoveAt then Insert, so the old control leaves the trees before the new one arrives.
    public void Replace(int index, UIElement element)
    {
        children.RemoveAt(index);
        children.Insert(index, element);
        ReplaceCount++;
    }
}
