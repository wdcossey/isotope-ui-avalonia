using Avalonia.Controls;
using Avalonia.Interactivity;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The toggle controls (#19): CheckBox, ToggleSwitch and RadioButton, which Avalonia derives from ToggleButton. Records
// mirror upstream's, with Avalonia's member names (#31) and value types; descriptors are generated, with the controlled
// IsChecked written in Customize because the record's value type can differ from the control's (bool? on Avalonia).
// Avalonia raises IsCheckedChanged synchronously inside the write. The controlled entries are upstream's Controlled
// entry, which arms the value it writes and drops the change event that reads that value back, so a write the engine
// makes doesn't echo and a user's click reaches the callback. The
// IsCheckedChanged event is excluded from the generated entries, as upstream excludes the events it hand-codes, so
// OnIsCheckedChanged isn't also wired as a plain event callback.

/// <summary>
/// A check box, as an Avalonia <see cref="Avalonia.Controls.CheckBox"/>. <see cref="IsChecked"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the control owns its checked state until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(CheckBox), ClearValueOnUnset = true, Exclude = new[] { "IsCheckedChanged" })]
[WrapManual("IsChecked")]
[WrapManual("Content")]
public partial record CheckBoxElement(
    Optional<bool?> IsChecked = default,
    Action<bool?>? OnIsCheckedChanged = null,
    string? Content = null) : Element
{
    /// <summary>Whether a click cycles through the indeterminate state (a null <see cref="IsChecked"/>) too.</summary>
    public bool IsThreeState { get; init; }

    internal Action<CheckBox>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => OnIsCheckedChanged is not null;

    // As upstream's: IsChecked is the controlled value, and the callback receives the control's value (null:
    // indeterminate; Factories.CheckBox's two-state callback is called only for a checked or unchecked value).
    // IsThreeState is generated and written after this entry, which Avalonia allows (it doesn't coerce IsChecked by
    // IsThreeState).
    private static partial ControlDescriptor<CheckBoxElement, CheckBox> Customize(ControlDescriptor<CheckBoxElement, CheckBox> d)
        => d.WithContent(static e => e.Content)
            .Controlled<bool?, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) => ((CheckBox)fe).IsCheckedChanged += h,
            unsubscribe: static (fe, h) => { /* the trampoline lives as long as the control, as upstream's */ },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked);
}

/// <summary>
/// An on/off switch, as an Avalonia <see cref="Avalonia.Controls.ToggleSwitch"/> (a ToggleButton on Avalonia: Reactor's
/// <c>IsOn</c> is its <see cref="IsChecked"/>, and Reactor's <c>Header</c> its <see cref="Content"/>).
/// <see cref="IsChecked"/> defaults to <see cref="Optional{T}.Unset"/>, so the control owns its state until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(ToggleSwitch), ClearValueOnUnset = true, Exclude = new[] { "IsCheckedChanged" })]
[WrapManual("IsChecked")]
[WrapManual("Content")]
public partial record ToggleSwitchElement(
    Optional<bool> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? OnContent = null,
    string? OffContent = null) : Element
{
    /// <summary>The text beside the switch (Reactor's <c>Header</c>).</summary>
    public string? Content { get; init; }

    internal Action<ToggleSwitch>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => OnIsCheckedChanged is not null;

    // The record's value is a bool, the control's a bool? (null never arises: the switch isn't three-state). The
    // content is a value, not a child slot, as Reactor's Header was (WithContent writes a string as it is).
    private static partial ControlDescriptor<ToggleSwitchElement, ToggleSwitch> Customize(ControlDescriptor<ToggleSwitchElement, ToggleSwitch> d)
        => d.WithContent(static e => e.Content)
            .Controlled<bool, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) => ((ToggleSwitch)fe).IsCheckedChanged += h,
            unsubscribe: static (fe, h) => { },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked ?? false);
}

/// <summary>
/// A radio button, as an Avalonia <see cref="Avalonia.Controls.RadioButton"/>. Buttons with the same
/// <see cref="GroupName"/> (or the same parent, without one) uncheck each other; that uncheck reaches
/// <see cref="OnIsCheckedChanged"/> as a change. <see cref="IsChecked"/> defaults to <see cref="Optional{T}.Unset"/>.
/// </summary>
[GenerateReactorDescriptor(typeof(RadioButton), ClearValueOnUnset = true, Exclude = new[] { "IsCheckedChanged" })]
[WrapManual("IsChecked")]
[WrapManual("Content")]
public partial record RadioButtonElement(
    string Content,
    Optional<bool> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? GroupName = null) : Element
{
    internal Action<RadioButton>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => OnIsCheckedChanged is not null;

    // As upstream's: the record's value is a bool, the control's a bool?, so the read-back maps null to false. The write
    // of a button doesn't echo to it; the uncheck it causes in another button of the group reaches that button's
    // callback, as on WinUI, so state kept per button follows the group.
    private static partial ControlDescriptor<RadioButtonElement, RadioButton> Customize(ControlDescriptor<RadioButtonElement, RadioButton> d)
        => d.WithContent(static e => e.Content)
            .Controlled<bool, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) => ((RadioButton)fe).IsCheckedChanged += h,
            unsubscribe: static (fe, h) => { },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked ?? false);
}
