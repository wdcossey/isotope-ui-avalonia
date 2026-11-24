using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The tree view (#43): upstream's TreeViewElement and its node data, as an Avalonia TreeView of TreeViewItems. Under the
// naming decision new names are Avalonia's: a node is TreeViewItemData (upstream's TreeViewNodeData), with a TreeViewItem's
// Header, Items and IsExpanded (upstream's Content, Children, IsExpanded), and the tree's nodes are its Items. Upstream's
// ContentElement (an element per node, obsolete upstream) and the drag members have no counterpart here. Nodes hold no
// elements, so they are TreeViewItems built from the data and updated in place by position (TreeItemsSync), as menu
// items are; upstream rebuilds the tree when its shape changes. A node's IsExpanded is written when its declared value
// changes, so what the user expands stays expanded until then, as upstream's. OnItemInvoked and OnExpanding keep
// Reactor's names: Avalonia's TreeView has no such events. An item is invoked by a tap on it (not on its expand
// chevron) or by Enter or Space on it, as WinUI's ItemInvoked; OnExpanding runs on TreeViewItem's Expanded, which comes
// after the item expands where WinUI's Expanding comes before, and not for the engine's own writes.

/// <summary>
/// A node of a <see cref="TreeViewElement"/>: its <see cref="Header"/> text, its child <see cref="Items"/> and whether it
/// starts expanded. Built into an Avalonia <see cref="TreeViewItem"/> (upstream's name: TreeViewNodeData).
/// </summary>
public record TreeViewItemData(string Header, TreeViewItemData[]? Items = null)
{
    public bool IsExpanded { get; init; }
}

/// <summary>A hierarchy of text items, as an Avalonia <see cref="Avalonia.Controls.TreeView"/>: one item per node of <see cref="Items"/>.</summary>
[GenerateReactorDescriptor(typeof(TreeView), ClearValueOnUnset = true, Exclude = new[] { "SelectionChanged" })]
[WrapManual("Items")]
[WrapManual("OnItemInvoked")]
[WrapManual("OnExpanding")]
public partial record TreeViewElement(TreeViewItemData[] Items) : Element
{
    /// <summary>Called with a node's data when the user taps it, or presses Enter or Space on it.</summary>
    public Action<TreeViewItemData>? OnItemInvoked { get; init; }

    /// <summary>Called with a node's data when the user expands it (after it expands): load its children here.</summary>
    public Action<TreeViewItemData>? OnExpanding { get; init; }

    /// <summary>Single (the default), multiple, toggle or always-selected selection; Avalonia's.</summary>
    public SelectionMode? SelectionMode { get; init; }

    internal Action<TreeView>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnItemInvoked is not null || OnExpanding is not null;

    private static void Invoke(TreeView tree, object? source)
    {
        if (TreeItemsSync.ItemAt(tree, source) is { } item && TreeItemsSync.DataOf(item) is { } data)
            (Reconciler.GetElementTag(tree) as TreeViewElement)?.OnItemInvoked?.Invoke(data);
    }

    private static readonly EventHandler<TappedEventArgs> __TappedTrampoline = (s, args) => Invoke((TreeView)s!, args.Source);

    private static readonly EventHandler<KeyEventArgs> __KeyDownTrampoline = (s, args) =>
    {
        if (args.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space) Invoke((TreeView)s!, args.Source);
    };

    private static readonly EventHandler<RoutedEventArgs> __ExpandedTrampoline = (s, args) =>
    {
        var tree = (TreeView)s!;
        if (TreeItemsSync.IsWriting || args.Source is not TreeViewItem item || !ReferenceEquals(TreeItemsSync.TreeOf(item), tree)) return;
        if (TreeItemsSync.DataOf(item) is { } data) (Reconciler.GetElementTag(tree) as TreeViewElement)?.OnExpanding?.Invoke(data);
    };

    private static partial ControlDescriptor<TreeViewElement, TreeView> Customize(ControlDescriptor<TreeViewElement, TreeView> d)
    {
        d.Children = new Imperative<TreeViewElement, TreeView>(static (ctx, oldElement, newElement, control) =>
            TreeItemsSync.Sync(ItemsList.Of(control.Items), oldElement?.Items, newElement.Items));
        return d
            .HandCodedEvent<TreeViewEventPayload, EventHandler<TappedEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.TappedEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.OnItemInvoked,
                trampoline:      __TappedTrampoline,
                slotIsNull:      static p => p.TappedTrampoline is null,
                setSlot:         static (p, h) => p.TappedTrampoline = h)
            .HandCodedEvent<TreeViewEventPayload, EventHandler<KeyEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.KeyDownEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.OnItemInvoked,
                trampoline:      __KeyDownTrampoline,
                slotIsNull:      static p => p.KeyDownTrampoline is null,
                setSlot:         static (p, h) => p.KeyDownTrampoline = h)
            .HandCodedEvent<TreeViewEventPayload, EventHandler<RoutedEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(TreeViewItem.ExpandedEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.OnExpanding,
                trampoline:      __ExpandedTrampoline,
                slotIsNull:      static p => p.ExpandedTrampoline is null,
                setSlot:         static (p, h) => p.ExpandedTrampoline = h);
    }
}

/// <summary>Builds Avalonia tree view items from node data and keeps them in step with it, by position.</summary>
internal static class TreeItemsSync
{
    private sealed class DataOwner;

    // The data a TreeViewItem was last written with; the tree's handlers read it.
    private static readonly AttachedProperty<TreeViewItemData?> DataProperty =
        AvaloniaProperty.RegisterAttached<DataOwner, TreeViewItem, TreeViewItemData?>("IsotopeTreeData");

    [ThreadStatic] private static int t_writing;

    /// <summary>True while the engine writes items, so the expansions it writes aren't reported as the user's.</summary>
    internal static bool IsWriting => t_writing > 0;

    internal static TreeViewItemData? DataOf(TreeViewItem item) => item.GetValue(DataProperty);

    /// <summary>The tree view an item belongs to: its nearest tree view in the logical tree.</summary>
    internal static TreeView? TreeOf(TreeViewItem item)
    {
        for (var p = item.Parent; p is not null; p = p.Parent)
            if (p is TreeView tree) return tree;
        return null;
    }

    /// <summary>
    /// The item of <paramref name="tree"/> an input event's source is in, or null outside one or on an item's expand
    /// chevron (a toggle button), which expands the item rather than invoking it.
    /// </summary>
    internal static TreeViewItem? ItemAt(TreeView tree, object? source)
    {
        for (var v = source as Visual; v is not null && !ReferenceEquals(v, tree); v = v.GetVisualParent())
        {
            if (v is ToggleButton) return null;
            if (v is TreeViewItem item) return ReferenceEquals(TreeOf(item), tree) ? item : null;
        }
        return null;
    }

    /// <summary>Makes <paramref name="items"/> show <paramref name="data"/>, each item updated in place.</summary>
    internal static void Sync(IList<object> items, TreeViewItemData[]? old, TreeViewItemData[]? data)
    {
        t_writing++;
        try { SyncLevel(items, old, data ?? []); }
        finally { t_writing--; }
    }

    private static void SyncLevel(IList<object> items, TreeViewItemData[]? old, TreeViewItemData[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (i < items.Count && items[i] is TreeViewItem existing)
                Write(existing, old is not null && i < old.Length ? old[i] : null, data[i]);
            else
            {
                var created = new TreeViewItem();
                Write(created, null, data[i]);
                if (i < items.Count) items[i] = created;
                else items.Add(created);
            }
        }
        for (int i = items.Count - 1; i >= data.Length; i--) items.RemoveAt(i);
    }

    private static void Write(TreeViewItem item, TreeViewItemData? old, TreeViewItemData data)
    {
        // The data an item last showed, for one updated in place by an engine that has no old element (a remount).
        old ??= item.GetValue(DataProperty);
        item.SetValue(DataProperty, data);
        if (!Equals(item.Header, data.Header)) item.Header = data.Header;
        // Written when the declared value changes, so the user's expansions stay until then. Items are paired by
        // position, so an item whose header changed is another item: its value is written.
        if (old is null || old.IsExpanded != data.IsExpanded || old.Header != data.Header) item.IsExpanded = data.IsExpanded;
        SyncLevel(ItemsList.Of(item.Items), old?.Items, data.Items ?? []);
    }
}
