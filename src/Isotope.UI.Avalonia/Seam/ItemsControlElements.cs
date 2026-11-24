using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The items controls (#19): ComboBox and ListBox. Records mirror upstream's (Reactor's member names, Avalonia's value
// types). Their string items go through the generated items host: Avalonia's ItemCollection is a non-generic IList,
// which ItemsList.Of adapts (#12). The items are written before the other members (DescriptorHandler), so a first
// SelectedIndex lands in a populated list. Avalonia raises SelectionChanged synchronously inside a SelectedIndex
// write, so the selection uses upstream's counter channel.

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

    /// <summary>The open drop-down's most height, in DIPs; NaN (the default) keeps Avalonia's.</summary>
    public double MaxDropDownHeight { get; init; } = double.NaN;

    /// <summary>Raised when the user opens the drop-down list.</summary>
    public Action? OnDropDownOpened { get; init; }

    /// <summary>Raised when the drop-down list closes.</summary>
    public Action? OnDropDownClosed { get; init; }

    internal Action<ComboBox>[] Setters { get; init; } = [];

    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnDropDownOpened is not null || OnDropDownClosed is not null;

    private static readonly EventHandler __DropDownOpenedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((ComboBox)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();

    private static readonly EventHandler __DropDownClosedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((ComboBox)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();

    // As upstream's: SelectedIndex is the controlled value, the two drop-down events are hand-wired (the generator
    // pairs no callback with a non-generic EventHandler event), and MaxDropDownHeight's NaN means "leave the control's".
    private static partial ControlDescriptor<ComboBoxElement, ComboBox> Customize(ControlDescriptor<ComboBoxElement, ComboBox> d)
        => d.Controlled<int, SelectionChangedEventArgs>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) => c.SelectedIndex = v,
                subscribe:   static (fe, h) => ((ComboBox)fe).SelectionChanged += h,
                unsubscribe: static (fe, h) => { },
                callback:    static e => e.OnSelectedIndexChanged,
                readBack:    static c => c.SelectedIndex)
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
            .OneWayConditional(
                get:         static e => e.MaxDropDownHeight,
                set:         static (c, v) => c.MaxDropDownHeight = v,
                shouldWrite: static e => !double.IsNaN(e.MaxDropDownHeight));
}

/// <summary>
/// A list of strings to select from, as an Avalonia <see cref="Avalonia.Controls.ListBox"/>.
/// <see cref="SelectedIndex"/> defaults to <see cref="Optional{T}.Unset"/>, so the control owns its selection until one
/// is given; -1 selects nothing.
/// </summary>
[GenerateReactorDescriptor(typeof(ListBox), ClearValueOnUnset = true, Exclude = new[] { "SelectionChanged" })]
[WrapManual("SelectedIndex")]
public partial record ListBoxElement(string[] Items) : Element
{
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }

    /// <summary>
    /// The selection callback for a multiple-selection <see cref="SelectionMode"/>: receives every selected index, in
    /// selection order, after each change.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }

    /// <summary>Single (the default), multiple, toggle or always-selected selection; Avalonia's.</summary>
    public SelectionMode? SelectionMode { get; init; }

    internal Action<ListBox>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnSelectedIndexChanged is not null || OnSelectionChanged is not null;

    // As upstream's: one trampoline on SelectionChanged reports the selected index and, when asked for, every selected
    // index.
    private static readonly EventHandler<SelectionChangedEventArgs> __SelectionChangedTrampoline = (s, _) =>
    {
        var lb = (ListBox)s!;
        if (!Reconciler.TryGetReactorState(lb, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        if (state.Element is not ListBoxElement el) return;
        el.OnSelectedIndexChanged?.Invoke(lb.SelectedIndex);
        if (el.OnSelectionChanged is { } h)
        {
            var selected = lb.SelectedItems;
            var snapshot = new List<int>(selected?.Count ?? 0);
            if (selected is not null)
                for (int i = 0; i < selected.Count; i++)
                {
                    var idx = lb.Items.IndexOf(selected[i]);
                    if (idx >= 0) snapshot.Add(idx);
                }
            h(snapshot);
        }
    };

    private static readonly Action<int> __NoOpSelectedIndexChanged = static _ => { };

    private static partial ControlDescriptor<ListBoxElement, ListBox> Customize(ControlDescriptor<ListBoxElement, ListBox> d)
        => d.HandCodedControlled<ListBoxEventPayload, int, EventHandler<SelectionChangedEventArgs>>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            // Subscribes when either callback is present; the trampoline calls each one that is.
            callback:    static e => e.OnSelectedIndexChanged ?? (e.OnSelectionChanged is null ? null : __NoOpSelectedIndexChanged),
            trampoline:  __SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);
}
