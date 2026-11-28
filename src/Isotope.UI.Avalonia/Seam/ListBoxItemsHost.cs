using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol;

namespace Isotope.UI.Core;

// A list box's element items, mounted lazily (#102). Upstream's ListView mounts an item when WinUI realises its container
// (ContainerContentChanging) and rebuilds every realised item when the items array changes, so an item loses its state to
// any new items array. Here an element item is mounted the first time Avalonia prepares a container for it
// (ItemsControl.PreparingContainer, before the container takes the item as its content), and reconciled from then on as
// a panel's child is: by key, else by position among the unkeyed, updated in place, moved, and a component in it keeps
// its state. It stays mounted while it is out of view, until it leaves the items or the list box is unmounted, so
// scrolling never loses its state either; an item never shown is never mounted. Strings (a list with no element in it)
// keep upstream's items host, as before.

/// <summary>
/// One item of a list box with element items: the list box's <see cref="ItemsControl.Items"/> hold these, one per item,
/// in order, and Avalonia's containers show the control mounted for each.
/// </summary>
internal sealed class ListBoxItemSlot(object? source)
{
    /// <summary>The item: its element, as the last render gave it, or a value in a list that mixes values and elements.</summary>
    public object? Source { get; set; } = source;

    /// <summary>The item's key: its element's.</summary>
    public string? Key => (Source as Element)?.Key;

    /// <summary>Whether the element has been mounted (its container was realised).</summary>
    public bool Mounted { get; set; }

    /// <summary>The control mounted for the element; null until then, and for an element that mounts nothing.</summary>
    public UIElement? Control { get; set; }

    /// <summary>The realised container that shows the item, or null.</summary>
    public ContentControl? Container { get; set; }

    public override string ToString() => Source is Element e ? e.Key ?? e.GetType().Name : Source?.ToString() ?? "";
}

/// <summary>What a list box with element items keeps between renders (<see cref="Reconciler.ReactorState.ListBoxItems"/>).</summary>
internal sealed class ListBoxItemsState
{
    /// <summary>The items, in the list box's order.</summary>
    public List<ListBoxItemSlot> Slots { get; set; } = new();

    /// <summary>The items array of the last render.</summary>
    public IReadOnlyList<object>? Source { get; set; }

    public bool AnyThemeSensitive { get; set; }

    /// <summary>The providers the list box was last reconciled under, which an item mounted during layout reads.</summary>
    public ContextCapture Context { get; } = new();

    public Reconciler Reconciler { get; set; } = null!;

    public Action RequestRerender { get; set; } = null!;

    /// <summary>The slot each realised container shows.</summary>
    public Dictionary<Control, ListBoxItemSlot> Realised { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>
/// The list box's items (#94, #102): strings through upstream's items host, elements as <see cref="ListBoxItemSlot"/>s whose
/// elements are mounted as Avalonia realises their containers. Edits go through <see cref="ListBoxItemsCollection"/>, so a
/// keyed move is one Move and keeps its selection. Bound before the other members, so a first SelectedIndex lands in a
/// populated list.
/// </summary>
internal sealed record ListBoxItemsHost : ChildrenStrategy<ListBoxElement, ListBox>, IItemsBinderStrategy
{
    internal static readonly ListBoxItemsHost Instance = new();

    private static readonly ItemsHost<ListBoxElement, ListBox> s_values = new(
        GetItems:      static e => e.Items,
        GetCollection: static c => new ListBoxItemsCollection(c));

    private static readonly EventHandler<ContainerPreparedEventArgs> s_preparing = static (sender, e) =>
    {
        var list = (ListBox)sender!;
        if (!Reconciler.TryGetReactorState(list, out var state) || state.ListBoxItems is not { } items) return;
        if ((uint)e.Index >= (uint)list.Items.Count || list.Items[e.Index] is not ListBoxItemSlot slot
            || e.Container is not ContentControl container) return;
        Realise(list, items, slot, container);
    };

    // Avalonia makes a container's item its data context once PreparingContainer has run: an element's row keeps the list
    // box's, as the item control did before #102, and a value's is the value, as a value item's is.
    private static readonly EventHandler<ContainerPreparedEventArgs> s_prepared = static (sender, e) =>
    {
        if (e.Container.DataContext is not ListBoxItemSlot slot) return;
        if (slot.Source is Element) e.Container.ClearValue(global::Avalonia.StyledElement.DataContextProperty);
        else e.Container.DataContext = slot.Source;
    };

    private static readonly EventHandler<ContainerClearingEventArgs> s_clearing = static (sender, e) =>
    {
        var list = (ListBox)sender!;
        if (!Reconciler.TryGetReactorState(list, out var state) || state.ListBoxItems is not { } items) return;
        // Avalonia has already cleared the container's content: the item's control stays mounted, with no parent.
        if (items.Realised.Remove(e.Container, out var slot) && ReferenceEquals(slot.Container, e.Container))
            slot.Container = null;
    };

    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler,
        Action requestRerender, bool isMount)
    {
        var list = (ListBox)control;
        var el = (ListBoxElement)element;
        var state = Reconciler.GetOrCreateReactorState(list);
        if (isMount)
        {
            list.PreparingContainer += s_preparing;
            list.ContainerPrepared += s_prepared;
            list.ContainerClearing += s_clearing;
        }
        if (!HasElement(el.Items))
        {
            // Strings: upstream's items host, as before #102. A list that held elements gives them up first.
            bool fromElements = state.ListBoxItems is not null;
            if (fromElements) ReleaseElements(list, state, reconciler);
            ItemsHostReconciliation.Bind(s_values, el, list, reconciler, requestRerender, isMount || fromElements);
            return;
        }
        if (isMount) state.Element = element; // as the items host: a callback-free list is tagged here too
        if (state.ItemsHostState is not null)
        {
            // From strings: they have nothing mounted.
            state.ItemsHostState = null;
            ClearSuppressed(list);
        }
        var items = state.ListBoxItems ??= new ListBoxItemsState();
        BindElements(list, items, el.Items, reconciler, requestRerender, isMount);
    }

    void IItemsBinderStrategy.Unbind(FrameworkElement control, Reconciler reconciler)
    {
        var list = (ListBox)control;
        list.PreparingContainer -= s_preparing;
        list.ContainerPrepared -= s_prepared;
        list.ContainerClearing -= s_clearing;
        var state = Reconciler.GetOrCreateReactorState(list);
        if (state.ListBoxItems is not null)
        {
            ReleaseElements(list, state, reconciler);
            state.Element = null;
            return;
        }
        ItemsHostReconciliation.Unbind(s_values, list, reconciler);
    }

    private static bool HasElement(object[] items)
    {
        foreach (var item in items)
            if (item is Element) return true;
        return false;
    }

    // Mounts the slot's element when its container is first realised, and puts its control in the container.
    private static void Realise(ListBox list, ListBoxItemsState items, ListBoxItemSlot slot, ContentControl container)
    {
        if (items.Realised.TryGetValue(container, out var shown) && !ReferenceEquals(shown, slot)
            && ReferenceEquals(shown.Container, container))
            shown.Container = null;
        items.Realised[container] = slot;
        if (slot.Source is not Element element)
        {
            // A value in a list that mixes values and elements: the container shows the value, as Avalonia shows a string.
            container.Content = slot.Source;
            return;
        }
        if (!slot.Mounted)
        {
            // Mounted once, whatever happens: an item whose mount leaves (propagated) isn't tried again on every layout pass.
            try
            {
                using (items.Reconciler.EnterRealization(items.Context))
                    slot.Control = items.Reconciler.MountItem(element, items.RequestRerender);
            }
            finally
            {
                slot.Mounted = true;
            }
            slot.Control?.SetValue(Reconciler.ItemsOwnerProperty, list);
        }
        // Another container still showing the control (never seen; a control has one parent) gives it up first.
        if (slot.Container is { } previous && !ReferenceEquals(previous, container) && ReferenceEquals(previous.Content, slot.Control))
            previous.ClearValue(ContentControl.ContentProperty);
        slot.Container = container;
        // Set before Avalonia's preparation, which then leaves the content alone; null for an element that mounted nothing.
        container.Content = slot.Control;
    }

    private static void BindElements(ListBox list, ListBoxItemsState items, object[] source, Reconciler reconciler,
        Action requestRerender, bool isMount)
    {
        items.Reconciler = reconciler;
        items.RequestRerender = requestRerender;
        reconciler.RefreshContextCapture(items.Context);
        // The same items array: nothing to do unless a mounted item must still be reached (a component in it re-renders
        // itself, reads a context value that changed, or a theme or resource refresh is on).
        if (!isMount && ReferenceEquals(items.Source, source)
            && !items.AnyThemeSensitive
            && !reconciler.ResourceRefreshActive
            && !reconciler.ForceFullRenderActive
            && !reconciler.IsOnDirtyAncestorPath(list)
            && !HasChangedItemContext(items, reconciler))
        {
            reconciler.DebugElementsSkipped += source.Length;
            return;
        }

        var previous = items.Slots;
        var used = new bool[previous.Count];
        Dictionary<string, Queue<int>>? keyed = null;
        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Source is Element { Key: { } key })
            {
                keyed ??= new(StringComparer.Ordinal);
                if (!keyed.TryGetValue(key, out var indices))
                    keyed[key] = indices = new Queue<int>();
                indices.Enqueue(i);
            }
        }

        var next = new List<ListBoxItemSlot>(source.Length);
        bool themeSensitive = false;
        for (int i = 0; i < source.Length; i++)
        {
            var item = source[i];
            // An unkeyed item or a value matches the one at its place among the items shown.
            int place = next.Count;
            int match = -1;
            if (item is Element child)
            {
                switch (child)
                {
                    // An empty element isn't an item, as it mounted nothing in the items host.
                    case EmptyElement:
                        continue;
                    case GroupElement:
                        throw new NotSupportedException(
                            "A GroupElement is flattened into its container's children; it can't be a list box's item.");
                }
                themeSensitive |= ChildDiffHints.IsThemeSensitive(child);
                if (child.Key is { } key)
                {
                    if (keyed is not null && keyed.TryGetValue(key, out var indices) && indices.Count > 0)
                        match = indices.Dequeue();
                }
                else if (place < previous.Count && !used[place] && previous[place].Source is Element { Key: null })
                    match = place;
                if (match < 0)
                {
                    next.Add(new ListBoxItemSlot(child));
                    continue;
                }
                used[match] = true;
                var slot = previous[match];
                var old = (Element)slot.Source!;
                slot.Source = child;
                // Only a mounted item has a control to reconcile; the others mount from their latest element when shown.
                if (slot.Mounted)
                {
                    var existing = slot.Control;
                    var value = reconciler.ReconcileV1Child(old, child, existing, requestRerender);
                    if (!ReferenceEquals(existing, value))
                    {
                        slot.Control = value;
                        value?.SetValue(Reconciler.ItemsOwnerProperty, list);
                        if (slot.Container is { } shownIn) shownIn.Content = value;
                    }
                }
                next.Add(slot);
            }
            else
            {
                // A value, among elements: kept where an equal value was.
                if (place < previous.Count && !used[place] && previous[place].Source is not Element && Equals(previous[place].Source, item))
                {
                    used[place] = true;
                    next.Add(previous[place]);
                }
                else
                    next.Add(new ListBoxItemSlot(item));
            }
        }

        // The items that left: torn down while still shown, then taken out of the list, from the end so the indexes hold.
        IList<object>? collection = null;
        for (int i = previous.Count - 1; i >= 0; i--)
        {
            if (used[i]) continue;
            if (previous[i].Control is { } gone) reconciler.UnmountChild(gone);
            collection ??= new ListBoxItemsCollection(list);
            collection.RemoveAt(i);
        }

        // Then each item in its place: inserted, or moved there (one Move each).
        if (collection is not null || !SameOrder(previous, next))
        {
            collection ??= new ListBoxItemsCollection(list);
            uint edits = 0;
            for (int placement = 0; placement < next.Count; placement++)
            {
                var slot = next[placement];
                if (placement < collection.Count && ReferenceEquals(collection[placement], slot)) continue;
                int current = IndexOfReference(collection, slot, placement + 1);
                if (current < 0)
                    collection.Insert(placement, slot);
                else
                {
                    ((ListBoxItemsCollection)collection).Move(current, placement);
                    edits++;
                }
            }
            if (!isMount && edits != 0) Reconciler.CountPerfChanges(edits);
        }

        items.Slots = next;
        items.Source = source;
        items.AnyThemeSensitive = themeSensitive;
    }

    private static bool SameOrder(List<ListBoxItemSlot> previous, List<ListBoxItemSlot> next)
    {
        if (previous.Count != next.Count) return false;
        for (int i = 0; i < next.Count; i++)
            if (!ReferenceEquals(previous[i], next[i])) return false;
        return true;
    }

    // From both ends at once: an item moved from the far end (a reverse, a rotation) is found at once.
    private static int IndexOfReference(IList<object> collection, object value, int start)
    {
        for (int lo = start, hi = collection.Count - 1; lo <= hi; lo++, hi--)
        {
            if (ReferenceEquals(collection[lo], value)) return lo;
            if (ReferenceEquals(collection[hi], value)) return hi;
        }
        return -1;
    }

    private static bool HasChangedItemContext(ListBoxItemsState items, Reconciler reconciler)
    {
        if (!reconciler.HasActiveContextValues) return false;
        foreach (var slot in items.Slots)
            if (slot.Control is { } control && reconciler.ShouldDeclineSkip(control))
                return true;
        return false;
    }

    // Unmounts every mounted item, while still shown, and empties the list.
    private static void ReleaseElements(ListBox list, Reconciler.ReactorState state, Reconciler reconciler)
    {
        if (state.ListBoxItems is not { } items) return;
        state.ListBoxItems = null;
        foreach (var slot in items.Slots)
            if (slot.Control is { } control) reconciler.UnmountChild(control);
        ClearSuppressed(list);
    }

    // Emptying the list isn't a user's selection: in the echo-suppression scope, as the items host clears.
    private static void ClearSuppressed(ListBox list)
    {
        var state = Reconciler.GetOrCreateReactorState(list);
        state.EchoSuppressScopeDepth++;
        try { list.Items.Clear(); }
        finally { state.EchoSuppressScopeDepth--; }
    }
}
