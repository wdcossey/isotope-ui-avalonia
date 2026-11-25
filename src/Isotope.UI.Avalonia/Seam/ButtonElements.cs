using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The rest of the button family (#26): ToggleButton, RepeatButton and HyperlinkButton. Records mirror upstream's, with
// Avalonia's member names (#31) and value types; descriptors are generated. Each takes a string or any element as its
// Content, as Avalonia's (WithContent, as upstream's). Each takes upstream's Command (#29), mapped to Avalonia's
// ICommand (CommandBindings).

/// <summary>
/// A button that stays pressed, as an Avalonia <see cref="Avalonia.Controls.Primitives.ToggleButton"/>.
/// <see cref="IsChecked"/> is written when it changes, as upstream's: a click toggles the control and reaches
/// <see cref="OnIsCheckedChanged"/>, and the control keeps the toggled state until the element's value changes.
/// </summary>
[GenerateReactorDescriptor(typeof(ToggleButton), Exclude = new[] { "IsCheckedChanged" })]
[WrapManual("Content")]
[WrapManual("IsThreeState")]
[WrapManual("IsChecked")]
[WrapManual("Command")]
public partial record ToggleButtonElement(object? Content, bool? IsChecked = false, Action<bool?>? OnIsCheckedChanged = null) : Element
{
    /// <summary>Whether a click cycles through the indeterminate state (a null <see cref="IsChecked"/>) too.</summary>
    public bool IsThreeState { get; init; }

    /// <summary>
    /// The command each toggle runs (upstream's member, #29), unless <see cref="OnIsCheckedChanged"/> is set, which wins.
    /// It disables the button while <see cref="Core.Command.IsEnabled"/> is false.
    /// </summary>
    public Command? Command { get; init; }

    internal Action<ToggleButton>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => CommandBindings.EffectiveCallback(OnIsCheckedChanged, Command) is not null;

    // As upstream's: the click reads the toggled value back and calls the callback. Avalonia's ToggleButton toggles in
    // OnClick before it raises Click, as WinUI's does. Listening to Click, not IsCheckedChanged, means the engine's own
    // writes never reach the callback.
    private static readonly EventHandler<RoutedEventArgs> __ClickTrampoline = (s, _) =>
    {
        var t = (ToggleButton)s!;
        if (Reconciler.GetElementTag(t) is not ToggleButtonElement live) return;
        live.OnIsCheckedChanged?.Invoke(t.IsChecked);
    };

    // As upstream's: IsThreeState is written before IsChecked. One event payload holds the click trampoline (a control
    // holds one payload).
    private static partial ControlDescriptor<ToggleButtonElement, ToggleButton> Customize(ControlDescriptor<ToggleButtonElement, ToggleButton> d)
        => d.WithContent(static e => e.Content)
            .OneWay(
                get: static e => e.IsThreeState,
                set: static (c, v) => c.IsThreeState = v)
            .OneWay<bool?>(
                get: static e => e.IsChecked,
                set: static (c, v) => c.IsChecked = v)
            .HandCodedEvent<V1Protocol.ToggleButtonEventPayload, EventHandler<RoutedEventArgs>>(
                subscribe:       static (c, h) => c.Click += h,
                callbackPresent: static e => e.OnIsCheckedChanged,
                trampoline:      __ClickTrampoline,
                slotIsNull:      static p => p.ClickTrampoline is null,
                setSlot:         static (p, h) => p.ClickTrampoline = h)
            .OneWayCommand(static e => e.Command, static e => e.OnIsCheckedChanged is not null);
}

/// <summary>
/// A button that raises its click again and again while held, as an Avalonia <see cref="Avalonia.Controls.RepeatButton"/>:
/// the first repeat after <see cref="Delay"/> ms, then every <see cref="Interval"/> ms.
/// </summary>
[GenerateReactorDescriptor(typeof(RepeatButton))]
[WrapManual("Content")]
[WrapManual("Command")]
public partial record RepeatButtonElement(object? Content, Action? OnClick = null) : Element
{
    /// <summary>Milliseconds before the first repeat (upstream's default, 250).</summary>
    public int Delay { get; init; } = 250;

    /// <summary>Milliseconds between repeats (upstream's default, 50).</summary>
    public int Interval { get; init; } = 50;

    /// <summary>The command each repeat runs (upstream's member, #29), unless <see cref="OnClick"/> is set, which wins.</summary>
    public Command? Command { get; init; }

    internal Action<RepeatButton>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    private static partial ControlDescriptor<RepeatButtonElement, RepeatButton> Customize(ControlDescriptor<RepeatButtonElement, RepeatButton> d)
        => d.WithContent(static e => e.Content)
            .OneWayCommand(static e => e.Command, static e => e.OnClick is not null);
}

/// <summary>
/// A button that looks like a link, as an Avalonia <see cref="Avalonia.Controls.HyperlinkButton"/>. A click calls
/// <see cref="OnClick"/>, and Avalonia opens <see cref="NavigateUri"/>, when set, with the platform's launcher.
/// </summary>
[GenerateReactorDescriptor(typeof(HyperlinkButton), ClearValueOnUnset = true)]
[WrapManual("Content")]
[WrapManual("Command")]
public partial record HyperlinkButtonElement(object? Content, Uri? NavigateUri = null, Action? OnClick = null) : Element
{
    /// <summary>The command a click runs (upstream's member, #29), unless <see cref="OnClick"/> is set, which wins.</summary>
    public Command? Command { get; init; }

    internal Action<HyperlinkButton>[] Setters { get; init; } = [];

    protected internal override bool HasCallbacks => CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    private static partial ControlDescriptor<HyperlinkButtonElement, HyperlinkButton> Customize(ControlDescriptor<HyperlinkButtonElement, HyperlinkButton> d)
        => d.WithContent(static e => e.Content)
            .OneWayCommand(static e => e.Command, static e => e.OnClick is not null);
}
