using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Tabs (#26): upstream's TabView, as an Avalonia TabControl. The record mirrors upstream's (Reactor's member names,
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
            GetCollection:   static c => ItemsList.Of(c.Items),
            GetContent:      static item => item.Content,
            CreateContainer: static (item, mounted) => new TabItem { Header = item.Header, Content = mounted },
            UpdateContainer: static (oldItem, newItem, container) =>
            {
                if (container is TabItem tab && !Equals(tab.Header, newItem.Header)) tab.Header = newItem.Header;
            });
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
