using Avalonia.Controls;
using Avalonia.Interactivity;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Tabs and the split view (#26): upstream's TabView, as an Avalonia TabControl, and SplitView. The record mirrors upstream's (Reactor's member names,
// Avalonia's value types); members with no Avalonia counterpart (add-tab button, close buttons, tab dragging, strip
// header and footer) aren't on it. The descriptor is generated; the tabs go through upstream's TabItemsHost strategy
// (vendored), which keeps each TabItem and reconciles its content in place, so a component in a tab keeps its state.

/// <summary>One tab of a <see cref="TabViewElement"/>: its header text and its content (upstream's record).</summary>
public record TabViewItemData(string Header, Element Content);

/// <summary>
/// Tabs, as an Avalonia <see cref="Avalonia.Controls.TabControl"/> (upstream's name: TabView). <see cref="SelectedIndex"/>
/// defaults to <see cref="Optional{T}.Unset"/>, so the control owns its selection until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(TabControl), ClearValueOnUnset = true, Exclude = new[] { "SelectionChanged" })]
[WrapManual("Tabs")]
[WrapManual("SelectedIndex")]
public partial record TabViewElement(TabViewItemData[] Tabs) : Element
{
    /// <summary>The selected tab's index; <see cref="Optional{T}.Unset"/> (the default) leaves it to the control.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }

    /// <summary>Which side the tab strip is on (Avalonia's member; upstream's TabView has none).</summary>
    public Avalonia.Controls.Dock? TabStripPlacement { get; init; }

    internal Action<TabControl>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;

    // SelectionChanged is a bubbling routed event on Avalonia: a ComboBox or ListBox inside a tab raises it on the
    // TabControl too, so only the TabControl's own selection is reported.
    private static readonly EventHandler<SelectionChangedEventArgs> __SelectionChangedTrampoline = (s, args) =>
    {
        var tabs = (TabControl)s!;
        if (!ReferenceEquals(args.Source, tabs)) return;
        if (!Reconciler.TryGetReactorState(tabs, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as TabViewElement)?.OnSelectedIndexChanged?.Invoke(tabs.SelectedIndex);
    };

    // As upstream's: the TabItemsHost (each TabViewItemData a TabItem, its content mounted as the item's content) and
    // the controlled SelectedIndex. Unlike upstream's, which diffs by value, the selection uses the counter channel, as
    // ComboBox's does: Avalonia raises SelectionChanged inside the SelectedIndex write.
    private static partial ControlDescriptor<TabViewElement, TabControl> Customize(ControlDescriptor<TabViewElement, TabControl> d)
    {
        d.Children = new TabItemsHost<TabViewElement, TabControl, TabViewItemData>(
            GetItems:        static e => e.Tabs,
            GetCollection:   static c => new EchoScopedItems(c, ItemsList.Of(c.Items)),
            GetContent:      static item => item.Content,
            CreateContainer: static (item, mounted) => new TabItem { Header = item.Header, Content = mounted },
            UpdateContainer: static (oldItem, newItem, container) =>
            {
                if (container is TabItem tab && !Equals(tab.Header, newItem.Header)) tab.Header = newItem.Header;
            });
        // Avalonia's TabControl always keeps a tab selected: adding the first tab, or removing the selected one, moves the
        // selection and raises SelectionChanged in the middle of the engine's update. That change is the engine's, not
        // the user's, so it is dropped (the controlled index, written next, corrects the selection when it differs).
        return d.HandCodedControlled<TabViewEventPayload, int, EventHandler<SelectionChangedEventArgs>>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  __SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);
    }
}

/// <summary>
/// The tab collection, with each change made in an echo-suppression scope on the TabControl, so the selection Avalonia
/// moves in answer to it isn't reported as the user's.
/// </summary>
internal sealed class EchoScopedItems(Control owner, IList<object> inner) : IList<object>
{
    private void Scoped(Action change)
    {
        var state = Reconciler.GetOrCreateReactorState(owner);
        state.EchoSuppressScopeDepth++;
        try { change(); }
        finally { state.EchoSuppressScopeDepth--; }
    }

    public object this[int index] { get => inner[index]; set => Scoped(() => inner[index] = value); }
    public int Count => inner.Count;
    public bool IsReadOnly => inner.IsReadOnly;
    public void Add(object item) => Scoped(() => inner.Add(item));
    public void Clear() => Scoped(inner.Clear);
    public bool Contains(object item) => inner.Contains(item);
    public void CopyTo(object[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
    public IEnumerator<object> GetEnumerator() => inner.GetEnumerator();
    public int IndexOf(object item) => inner.IndexOf(item);
    public void Insert(int index, object item) => Scoped(() => inner.Insert(index, item));
    public bool Remove(object item)
    {
        var removed = false;
        Scoped(() => removed = inner.Remove(item));
        return removed;
    }
    public void RemoveAt(int index) => Scoped(() => inner.RemoveAt(index));
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A pane beside (or over) the content, as an Avalonia <see cref="Avalonia.Controls.SplitView"/>. The record mirrors
/// upstream's (Reactor's member names, Avalonia's value types), with Avalonia's <see cref="PanePlacement"/>; upstream's
/// LightDismissOverlayMode has no Avalonia counterpart.
/// </summary>
/// <remarks>
/// As upstream's, <see cref="IsPaneOpen"/> is edge-triggered: it is written only when the declared value changes, so a
/// pane the control closed itself (light dismiss) stays closed when the same value renders again. Feed
/// <see cref="OnPaneOpenChanged"/> into the state that drives it.
/// </remarks>
[GenerateReactorDescriptor(typeof(SplitView), ClearValueOnUnset = true, Exclude = new[] { "PaneOpening", "PaneOpened", "PaneClosing", "PaneClosed" })]
[WrapManual("Pane")]
[WrapManual("Content")]
[WrapManual("IsPaneOpen")]
public partial record SplitViewElement(Element? Pane = null, Element? Content = null) : Element
{
    /// <summary>Whether the pane is open (upstream's default: true). Written only when it changes.</summary>
    public bool IsPaneOpen { get; init; } = true;

    public double OpenPaneLength { get; init; } = 320;
    public double CompactPaneLength { get; init; } = 48;
    public SplitViewDisplayMode DisplayMode { get; init; } = SplitViewDisplayMode.Overlay;

    /// <summary>The brush behind the pane; null keeps the theme's.</summary>
    public Avalonia.Media.IBrush? PaneBackground { get; init; }

    /// <summary>Which side the pane is on; null keeps Avalonia's default (left). Avalonia's member.</summary>
    public SplitViewPanePlacement? PanePlacement { get; init; }

    /// <summary>Called with true when the pane starts to open and false when it starts to close, for the user's actions only.</summary>
    public Action<bool>? OnPaneOpenChanged { get; init; }

    internal Action<SplitView>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnPaneOpenChanged is not null;

    // PaneOpening and PaneClosing are bubbling routed events on Avalonia: a SplitView in the pane raises them on this one
    // too, so only this control's own are reported. Avalonia raises them inside an IsPaneOpen write, which the engine
    // makes in an echo-suppression scope.
    private static readonly EventHandler<CancelRoutedEventArgs> __PaneOpeningTrampoline = (s, args) => Report((SplitView)s!, args, true);

    private static readonly EventHandler<CancelRoutedEventArgs> __PaneClosingTrampoline = (s, args) => Report((SplitView)s!, args, false);

    private static void Report(SplitView view, CancelRoutedEventArgs args, bool open)
    {
        if (!ReferenceEquals(args.Source, view)) return;
        if (!Reconciler.TryGetReactorState(view, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as SplitViewElement)?.OnPaneOpenChanged?.Invoke(open);
    }

    // As upstream's: Pane and Content are named slots, reconciled in place; IsPaneOpen edge-triggered; the two events
    // share one payload (a control holds one). Unlike upstream's, the engine's own IsPaneOpen write doesn't reach
    // OnPaneOpenChanged.
    private static partial ControlDescriptor<SplitViewElement, SplitView> Customize(ControlDescriptor<SplitViewElement, SplitView> d)
    {
        d.Children = new NamedSlots<SplitViewElement, SplitView>(new[]
        {
            new NamedSlot<SplitViewElement, SplitView>(
                Name: "Pane",
                GetChild: static e => e.Pane,
                SetChild: static (c, ui) => c.Pane = ui)
            {
                GetCurrentChild = static c => c.Pane as Control,
            },
            new NamedSlot<SplitViewElement, SplitView>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as Control,
            },
        });
        return d
            .ImperativeBridged(
                mount:  static (ctx, c, e) => WriteIsPaneOpen(c, e.IsPaneOpen),
                update: static (ctx, c, o, n) =>
                {
                    if (o.IsPaneOpen != n.IsPaneOpen) WriteIsPaneOpen(c, n.IsPaneOpen);
                })
            .HandCodedEvent<V1Protocol.SplitViewEventPayload, EventHandler<CancelRoutedEventArgs>>(
                subscribe:       static (c, h) => c.PaneOpening += h,
                callbackPresent: static e => e.OnPaneOpenChanged,
                trampoline:      __PaneOpeningTrampoline,
                slotIsNull:      static p => p.PaneOpeningTrampoline is null,
                setSlot:         static (p, h) => p.PaneOpeningTrampoline = h)
            .HandCodedEvent<V1Protocol.SplitViewEventPayload, EventHandler<CancelRoutedEventArgs>>(
                subscribe:       static (c, h) => c.PaneClosing += h,
                callbackPresent: static e => e.OnPaneOpenChanged,
                trampoline:      __PaneClosingTrampoline,
                slotIsNull:      static p => p.PaneClosingTrampoline is null,
                setSlot:         static (p, h) => p.PaneClosingTrampoline = h);
    }

    // The events the write raises are echoes: dropped in a scope rather than by a token, so a write the control
    // doesn't act on leaves nothing behind.
    private static void WriteIsPaneOpen(SplitView c, bool open)
    {
        if (c.IsPaneOpen == open) return;
        var state = Reconciler.GetOrCreateReactorState(c);
        state.EchoSuppressScopeDepth++;
        try { c.IsPaneOpen = open; }
        finally { state.EchoSuppressScopeDepth--; }
    }
}
