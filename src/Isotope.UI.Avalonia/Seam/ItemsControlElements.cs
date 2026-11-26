using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The items controls (#19): ComboBox and ListBox. Records mirror upstream's (Avalonia's member names, #31, and value
// types). Their items go through upstream's items host: Avalonia's ItemCollection is a non-generic IList, which
// ItemsList.Of adapts (#12). The items are written before the other members (DescriptorHandler), so a first
// SelectedIndex lands in a populated list. Avalonia raises SelectionChanged synchronously inside a SelectedIndex
// write, so the selection uses upstream's counter channel. Reactor's ListView is the list box too (#94): its element
// items are the list box's Items, and its typed ListView<T> is ListBoxElement<T>.

/// <summary>
/// A drop-down list of strings, as an Avalonia <see cref="Avalonia.Controls.ComboBox"/>. <see cref="SelectedIndex"/>
/// defaults to <see cref="Optional{T}.Unset"/>, so the control owns its selection until one is given; -1 selects nothing.
/// </summary>
[GenerateReactorDescriptor(typeof(ComboBox), ClearValueOnUnset = true, Exclude = new[] { "SelectionChanged", "DropDownOpened", "DropDownClosed" })]
[WrapManual("SelectedIndex")]
[WrapManual("MaxDropDownHeight")]
public partial record ComboBoxElement(
    string[] Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null) : Element
{
    public string? PlaceholderText { get; init; }

    /// <summary>Whether the user can type into the box.</summary>
    public bool IsEditable { get; init; }

    /// <summary>The open drop-down's greatest height, in DIPs; NaN (the default) keeps Avalonia's.</summary>
    public double MaxDropDownHeight { get; init; } = double.NaN;

    /// <summary>Raised when the user opens the drop-down list.</summary>
    public Action? OnDropDownOpened { get; init; }

    /// <summary>Raised when the drop-down list closes.</summary>
    public Action? OnDropDownClosed { get; init; }

    internal Action<ComboBox>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnDropDownOpened is not null || OnDropDownClosed is not null;

    private static readonly EventHandler<SelectionChangedEventArgs> __SelectionChangedTrampoline = (s, _) =>
    {
        var cb = (ComboBox)s!;
        if (!Reconciler.TryGetReactorState(cb, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as ComboBoxElement)?.OnSelectedIndexChanged?.Invoke(cb.SelectedIndex);
    };

    private static readonly EventHandler __DropDownOpenedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((ComboBox)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();

    private static readonly EventHandler __DropDownClosedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((ComboBox)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();

    // As upstream's: SelectedIndex is the controlled value and the two drop-down events are hand-wired (the generator
    // pairs no callback with a non-generic EventHandler event), all three through one payload type, because a control
    // holds one event payload; MaxDropDownHeight's NaN means "Avalonia's", through the ClearValue channel as the other
    // members (upstream's skips the write, leaving the last height). Unlike upstream's, the selection
    // uses the counter channel rather than the value-diff one: Avalonia raises SelectionChanged inside the write.
    private static partial ControlDescriptor<ComboBoxElement, ComboBox> Customize(ControlDescriptor<ComboBoxElement, ComboBox> d)
        => d.HandCodedControlled<ComboBoxEventPayload, int, EventHandler<SelectionChangedEventArgs>>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) => c.SelectedIndex = v,
                readBack:    static c => c.SelectedIndex,
                subscribe:   static (c, h) => c.SelectionChanged += h,
                callback:    static e => e.OnSelectedIndexChanged,
                trampoline:  __SelectionChangedTrampoline,
                slotIsNull:  static p => p.SelectionChangedTrampoline is null,
                setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
            .HandCodedEvent<ComboBoxEventPayload, EventHandler>(
                subscribe:       static (c, h) => c.DropDownOpened += h,
                callbackPresent: static e => e.OnDropDownOpened,
                trampoline:      __DropDownOpenedTrampoline,
                slotIsNull:      static p => p.DropDownOpenedTrampoline is null,
                setSlot:         static (p, h) => p.DropDownOpenedTrampoline = h)
            .HandCodedEvent<ComboBoxEventPayload, EventHandler>(
                subscribe:       static (c, h) => c.DropDownClosed += h,
                callbackPresent: static e => e.OnDropDownClosed,
                trampoline:      __DropDownClosedTrampoline,
                slotIsNull:      static p => p.DropDownClosedTrampoline is null,
                setSlot:         static (p, h) => p.DropDownClosedTrampoline = h)
            .OneWay<double>(
                get: static e => double.IsNaN(e.MaxDropDownHeight) ? Optional<double>.Unset : e.MaxDropDownHeight,
                set: static (c, v) => c.MaxDropDownHeight = v,
                dp:  ComboBox.MaxDropDownHeightProperty);
}

/// <summary>
/// A list to select from, as an Avalonia <see cref="Avalonia.Controls.ListBox"/> (Reactor's ListBox, and its ListView).
/// Its <see cref="Items"/> are Avalonia's: strings, shown through Avalonia's recycled containers, or elements, each
/// mounted and reconciled as a panel's children are (by key, else by position among the unkeyed; updated in place, a
/// keyed one moved, and a component in one keeps its state). <see cref="SelectedIndex"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the control owns its selection until one is given; -1 selects nothing.
/// </summary>
[GenerateReactorDescriptor(typeof(ListBox), ClearValueOnUnset = true, Exclude = new[] { "SelectionChanged" })]
[WrapManual("Items")]
[WrapManual("SelectedIndex")]
[WrapManual("OnItemClick")]
public partial record ListBoxElement(object[] Items) : Element
{
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }

    /// <summary>
    /// The selection callback for a multiple-selection <see cref="SelectionMode"/>: receives every selected index, in
    /// ascending order, after each change.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }

    /// <summary>
    /// Called with an item's index when the user taps it (not a focusable control in it, such as a button or a text box; each
    /// tap of a double tap), or presses Enter or Space on it (Reactor's name: Avalonia's ListBox has no item-click event). A tap selects the item first, so
    /// <see cref="OnSelectedIndexChanged"/> comes before it.
    /// </summary>
    public Action<int>? OnItemClick { get; init; }

    /// <summary>Single (the default), multiple, toggle or always-selected selection; Avalonia's flags.</summary>
    public SelectionMode? SelectionMode { get; init; }

    internal Action<ListBox>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => OnSelectedIndexChanged is not null || HasSelectionCallback || HasItemClick;

    // The typed list (ListBoxElement<T>) reports its own item and selection callbacks through these.
    private protected virtual bool HasItemClick => OnItemClick is not null;

    private protected virtual bool HasSelectionCallback => OnSelectionChanged is not null;

    private protected virtual void RaiseItemClick(int index) => OnItemClick?.Invoke(index);

    private protected virtual void RaiseSelectionChanged(IReadOnlyList<int> indexes) => OnSelectionChanged?.Invoke(indexes);

    // As upstream's: one trampoline on SelectionChanged reports the selected index and, when asked for, every selected
    // index. SelectionChanged bubbles on Avalonia, so a combo box or list box in an item raises it on this list too:
    // only the list's own is reported.
    private static readonly EventHandler<SelectionChangedEventArgs> __SelectionChangedTrampoline = (s, args) =>
    {
        var lb = (ListBox)s!;
        if (!ReferenceEquals(args.Source, lb)) return;
        if (!Reconciler.TryGetReactorState(lb, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        if (state.Element is not ListBoxElement el) return;
        el.OnSelectedIndexChanged?.Invoke(lb.SelectedIndex);
        // Unlike upstream's, which maps each selected item back with IndexOf, the indexes come from the selection model,
        // so equal strings at two positions are told apart.
        if (el.HasSelectionCallback)
        {
            var snapshot = lb.Selection.SelectedIndexes.ToList();
            snapshot.Sort();
            el.RaiseSelectionChanged(snapshot);
        }
    };

    private static void ItemClicked(ListBox list, int index)
    {
        if (index >= 0 && Reconciler.GetElementTag(list) is ListBoxElement el) el.RaiseItemClick(index);
    }

    /// <summary>
    /// The index of the item of <paramref name="list"/> an input event's source is in: -1 outside one, in a nested list's
    /// item, or in a focusable control in the item (a button, a text box, a slider: it takes its own pointer input, and
    /// WinUI's ItemClick isn't raised for it either).
    /// </summary>
    internal static int IndexAt(ListBox list, object? source)
    {
        for (var v = source as Visual; v is not null && !ReferenceEquals(v, list); v = v.GetVisualParent())
        {
            if (v is ListBoxItem item)
                return ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), list) ? list.IndexFromContainer(item) : -1;
            if (v is InputElement { Focusable: true }) return -1;
        }
        return -1;
    }

    private static readonly EventHandler<TappedEventArgs> __TappedTrampoline = (s, args) =>
    {
        var list = (ListBox)s!;
        ItemClicked(list, IndexAt(list, args.Source));
    };

    private static readonly EventHandler<KeyEventArgs> __KeyDownTrampoline = (s, args) =>
    {
        // Unmodified, on a focused item itself: Ctrl+Space and Shift+Space change a multiple selection, and a key typed
        // into a text box in an item is the text box's.
        if (args.Key is not (Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space) || args.KeyModifiers != KeyModifiers.None) return;
        var list = (ListBox)s!;
        if (args.Source is ListBoxItem item && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), list))
            ItemClicked(list, list.IndexFromContainer(item));
    };

    private static readonly Action<int> __NoOpSelectedIndexChanged = static _ => { };

    private static partial ControlDescriptor<ListBoxElement, ListBox> Customize(ControlDescriptor<ListBoxElement, ListBox> d)
    {
        // The items, strings and elements alike, through upstream's items host (written before the other members, so a
        // first SelectedIndex lands in a populated list), edited through ListBoxItemsCollection: a keyed move is one Move
        // and keeps its selection.
        d.Children = new ItemsHost<ListBoxElement, ListBox>(
            GetItems:      static e => e.Items,
            GetCollection: static c => new ListBoxItemsCollection(c));
        return d.HandCodedControlled<ListBoxEventPayload, int, EventHandler<SelectionChangedEventArgs>>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) => c.SelectedIndex = v,
                readBack:    static c => c.SelectedIndex,
                subscribe:   static (c, h) => c.SelectionChanged += h,
                // Subscribes when either callback is present; the trampoline calls each one that is.
                callback:    static e => e.OnSelectedIndexChanged ?? (e.HasSelectionCallback ? __NoOpSelectedIndexChanged : null),
                trampoline:  __SelectionChangedTrampoline,
                slotIsNull:  static p => p.SelectionChangedTrampoline is null,
                setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
            // OnItemClick as the tree view's OnItemInvoked: Avalonia's ListBox has no item-click event, so a tap on an
            // item (handled ones too: the item handles the press that selects it) and Enter or Space on one.
            .HandCodedEvent<ListBoxEventPayload, EventHandler<TappedEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.TappedEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.HasItemClick ? __NoOpSelectedIndexChanged : null,
                trampoline:      __TappedTrampoline,
                slotIsNull:      static p => p.TappedTrampoline is null,
                setSlot:         static (p, h) => p.TappedTrampoline = h)
            // A second tap soon after the first is Avalonia's DoubleTapped, not a Tapped: it is a click too, as each click
            // of a double click is WinUI's ItemClick.
            .HandCodedEvent<ListBoxEventPayload, EventHandler<TappedEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.DoubleTappedEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.HasItemClick ? __NoOpSelectedIndexChanged : null,
                trampoline:      __TappedTrampoline,
                slotIsNull:      static p => p.DoubleTappedTrampoline is null,
                setSlot:         static (p, h) => p.DoubleTappedTrampoline = h)
            .HandCodedEvent<ListBoxEventPayload, EventHandler<KeyEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.KeyDownEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.HasItemClick ? __NoOpSelectedIndexChanged : null,
                trampoline:      __KeyDownTrampoline,
                slotIsNull:      static p => p.KeyDownTrampoline is null,
                setSlot:         static (p, h) => p.KeyDownTrampoline = h);
    }
}

/// <summary>
/// A list of data items, each shown by the element <see cref="ListBoxElement"/> builds from it, as an Avalonia
/// <see cref="Avalonia.Controls.ListBox"/> (Reactor's typed ListView&lt;T&gt;): <see cref="ListBoxElement.Items"/> holds an
/// element per item of <see cref="ItemsSource"/>, keyed by its key selector, so items that move keep their controls and
/// a component's state. <see cref="OnItemClick"/> and <see cref="OnSelectionChanged"/> receive the items.
/// </summary>
/// <remarks>
/// The elements are built when the record is, by calling the item template once per item, and every one is mounted:
/// Avalonia realises containers only for the items in view.
/// </remarks>
public sealed record ListBoxElement<T> : ListBoxElement
{
    /// <summary>
    /// The list of <paramref name="itemsSource"/>: <paramref name="itemTemplate"/> builds each item's element from the item
    /// and its index, keyed by <paramref name="keySelector"/> (its key wins over one the element has).
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="itemTemplate"/> returned null or a Group for an item. An
    /// item it returns Empty() for isn't shown.</exception>
    public ListBoxElement(IReadOnlyList<T> itemsSource, Func<T, string> keySelector, Func<T, int, Element> itemTemplate)
        : this(itemsSource, Build(itemsSource, keySelector, itemTemplate))
    {
    }

    private ListBoxElement(IReadOnlyList<T> itemsSource, (object[] Items, int[] SourceIndexes) built) : base(built.Items)
    {
        ItemsSource = itemsSource;
        _sourceIndexes = built.SourceIndexes;
    }

    // For each of Items, the index of its item in ItemsSource: an item whose template returned Empty() has no element.
    private readonly int[] _sourceIndexes;

    private bool TryGetItem(int index, out T item)
    {
        if (index >= 0 && index < _sourceIndexes.Length && _sourceIndexes[index] < ItemsSource.Count)
        {
            item = ItemsSource[_sourceIndexes[index]];
            return true;
        }
        item = default!;
        return false;
    }

    /// <summary>The data items, in order; <see cref="ListBoxElement.Items"/> holds the element built for each.</summary>
    public IReadOnlyList<T> ItemsSource { get; }

    /// <summary>
    /// Called with the item the user taps (not a button in it), or presses Enter or Space on. A tap selects the item first.
    /// </summary>
    public new Action<T>? OnItemClick { get; init; }

    /// <summary>The selection callback for a multiple-selection mode: receives every selected item, in order, after each change.</summary>
    public new Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }

    private protected override bool HasItemClick => OnItemClick is not null || base.HasItemClick;

    private protected override bool HasSelectionCallback => OnSelectionChanged is not null || base.HasSelectionCallback;

    private protected override void RaiseItemClick(int index)
    {
        base.RaiseItemClick(index);
        if (OnItemClick is { } click && TryGetItem(index, out var item)) click(item);
    }

    private protected override void RaiseSelectionChanged(IReadOnlyList<int> indexes)
    {
        base.RaiseSelectionChanged(indexes);
        if (OnSelectionChanged is not { } changed) return;
        var selected = new List<T>(indexes.Count);
        foreach (var i in indexes)
            if (TryGetItem(i, out var item)) selected.Add(item);
        changed(selected);
    }

    private static (object[], int[]) Build(IReadOnlyList<T> itemsSource, Func<T, string> keySelector, Func<T, int, Element> itemTemplate)
    {
        ArgumentNullException.ThrowIfNull(itemsSource);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(itemTemplate);
        var items = new List<object>(itemsSource.Count);
        var sourceIndexes = new List<int>(itemsSource.Count);
        for (int i = 0; i < itemsSource.Count; i++)
        {
            var item = itemsSource[i];
            var element = itemTemplate(item, i);
            switch (element)
            {
                case null:
                    throw new InvalidOperationException($"The item template returned null for the item at {i}: return an element, Empty() for none.");
                case EmptyElement:
                    continue;
                case GroupElement:
                    throw new InvalidOperationException($"The item template returned a Group for the item at {i}: return one element per item (a VStack or HStack for several).");
            }
            items.Add(element.WithKey(keySelector(item)));
            sourceIndexes.Add(i);
        }
        return (items.ToArray(), sourceIndexes.ToArray());
    }
}

/// <summary>
/// A list box's items as upstream's items host edits them (#94). A keyed move is a single Move of Avalonia's items list:
/// the items host moves an item with <c>Move</c> only on an <see cref="ObservableCollection{T}"/>, and upstream's removal
/// and insertion would take the item out of the list box's logical children and put it back, which costs a scan of
/// them each time (a keyed reverse of 10,000 rows took over 2 s headless). Avalonia's selection treats a move as a
/// removal and an insertion and drops the item's selection, so an item selected before it moves is selected again in its
/// new place, in an echo-suppression scope: the selection follows its item, unreported. An item added or removed is
/// reported as Avalonia changes the selection, as before #94 (a selected item removed: -1).
/// </summary>
/// <remarks>
/// The base collection mirrors the list box's items, copied when the items host asks for the collection; every edit is
/// made to both.
/// </remarks>
internal sealed class ListBoxItemsCollection(ListBox owner) : ObservableCollection<object>(owner.Items.Cast<object>())
{
    private void Scoped(Action change)
    {
        var state = Reconciler.GetOrCreateReactorState(owner);
        state.EchoSuppressScopeDepth++;
        try { change(); }
        finally { state.EchoSuppressScopeDepth--; }
    }

    protected override void InsertItem(int index, object item)
    {
        owner.Items.Insert(index, item);
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        owner.Items.RemoveAt(index);
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, object item)
    {
        owner.Items[index] = item;
        base.SetItem(index, item);
    }

    protected override void ClearItems()
    {
        owner.Items.Clear();
        base.ClearItems();
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        Scoped(() =>
        {
            var selected = owner.Selection.IsSelected(oldIndex);
            if (owner.Items.Source is AvaloniaList<object?> source) source.Move(oldIndex, newIndex);
            else
            {
                var item = owner.Items[oldIndex];
                owner.Items.RemoveAt(oldIndex);
                owner.Items.Insert(newIndex, item);
            }
            if (selected) owner.Selection.Select(newIndex);
        });
        base.MoveItem(oldIndex, newIndex);
    }
}
