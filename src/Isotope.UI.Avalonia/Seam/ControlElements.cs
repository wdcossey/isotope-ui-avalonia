using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The element records for the first Avalonia controls (#12). Hand-written for now; their descriptors are generated
// by upstream's wrappers generator, built for Avalonia (src/Isotope.UI.Avalonia.Generators), from
// [GenerateReactorDescriptor]: each member maps to the Avalonia control's property of the same name, [WrapAlias]
// bridges Reactor's member names to Avalonia's, and [WrapManual] members are written in Customize. These records
// mirror upstream's, so they keep Reactor's member names; their value types are Avalonia's.

/// <summary>Text, as an Avalonia <see cref="Avalonia.Controls.TextBlock"/>.</summary>
[GenerateReactorDescriptor(typeof(TextBlock))]
[WrapAlias("Content", "Text")]
public partial record TextBlockElement(string Content) : Element
{
    internal Action<TextBlock>[] Setters { get; init; } = [];
}

/// <summary>
/// A push button, as an Avalonia <see cref="Avalonia.Controls.Button"/>: a text <see cref="Label"/>, or any element as
/// its content (<see cref="ContentElement"/>, #26).
/// </summary>
[GenerateReactorDescriptor(typeof(Button))]
[WrapManual("Label")]
[WrapManual("IsEnabled")]
public partial record ButtonElement(string Label, Action? OnClick = null) : Element
{
    public bool IsEnabled { get; init; } = true;

    /// <summary>Declarative content, taking precedence over <see cref="Label"/> (upstream's member).</summary>
    public Element? ContentElement { get; init; }

    internal Action<Button>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnClick is not null;

    // IsEnabled has a second source, the IsEnabled modifier: the reconciler writes the two as one value
    // (Reconciler.ApplyEnabled), so neither overwrites the other, and the descriptor leaves it alone. As upstream's,
    // the content is a child subtree when ContentElement is set, else the label (WithContent, #26).
    private static partial ControlDescriptor<ButtonElement, Button> Customize(ControlDescriptor<ButtonElement, Button> d)
        => d.WithContent(static e => e.ContentElement, static e => e.Label);
}

/// <summary>Children stacked in one direction, as an Avalonia <see cref="Avalonia.Controls.StackPanel"/>.</summary>
[GenerateReactorDescriptor(typeof(StackPanel))]
public partial record StackElement(Orientation Orientation, Element[] Children) : Element
{
    private readonly Element[] _children = Reconciler.Flatten(Children);

    /// <summary>
    /// The children, with any <see cref="GroupElement"/> flattened into them, as upstream's container factories
    /// flatten them (the panel strategy mounts what it is given).
    /// </summary>
    public Element[] Children
    {
        get => _children;
        init => _children = Reconciler.Flatten(value);
    }

    /// <summary>Spacing between children, in DIPs (upstream's default: 8).</summary>
    public double Spacing { get; init; } = 8;

    internal Action<StackPanel>[] Setters { get; init; } = [];
}

/// <summary>
/// A text box, as an Avalonia <see cref="Avalonia.Controls.TextBox"/>. <see cref="Value"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the text the user types is kept unless a value is given.
/// </summary>
[GenerateReactorDescriptor(typeof(TextBox), ClearValueOnUnset = true)]
[WrapManual("Value")]
[WrapManual("AcceptsReturn")]
[WrapManual("TextWrapping")]
public partial record TextBoxElement(
    Optional<string> Value = default,
    Action<string>? OnChanged = null,
    string? PlaceholderText = null) : Element
{
    public bool? IsReadOnly { get; init; }
    public bool? AcceptsReturn { get; init; }
    public TextWrapping? TextWrapping { get; init; }

    /// <summary>The most characters allowed; 0 (the default) means no limit.</summary>
    public int MaxLength { get; init; }

    internal Action<TextBox>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnChanged is not null;

    // Reports user edits, as upstream's does: the echo of each write of the engine's own (the controlled Value) carries
    // a suppression token, which the trampoline consumes. Avalonia posts TextChanged, so the event arrives after the
    // write, and every write of a new text posts one.
    private static readonly EventHandler<TextChangedEventArgs> __TextChangedTrampoline = (s, _) =>
    {
        var tb = (TextBox)s!;
        if (!Reconciler.TryGetReactorState(tb, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as TextBoxElement)?.OnChanged?.Invoke(tb.Text ?? string.Empty);
    };

    // As upstream's: AcceptsReturn and TextWrapping are written before the text (a single-line box strips line
    // breaks from the text it's given), and Value is a controlled value whose update writes carry a suppression token
    // (HandCodedControlled). Unlike upstream's:
    // - the mount write carries a token too (the entry just before HandCodedControlled), because Avalonia raises
    //   TextChanged for it even before the box is in a window, after the trampoline is wired;
    // - the nullable members go through the ClearValue channel (ClearValueOnUnset, OneWay with a property), so setting
    //   one back to null gives the control its default again instead of leaving the old value.
    private static partial ControlDescriptor<TextBoxElement, TextBox> Customize(ControlDescriptor<TextBoxElement, TextBox> d)
        => d.OneWay<bool>(
                get:  static e => e.AcceptsReturn.HasValue ? e.AcceptsReturn.Value : Optional<bool>.Unset,
                set:  static (c, v) => c.AcceptsReturn = v,
                dp:   TextBox.AcceptsReturnProperty)
            .OneWay<TextWrapping>(
                get:  static e => e.TextWrapping.HasValue ? e.TextWrapping.Value : Optional<TextWrapping>.Unset,
                set:  static (c, v) => c.TextWrapping = v,
                dp:   TextBox.TextWrappingProperty)
            .ImperativeBridged(
                mount:       static (ctx, c, e) =>
                {
                    if (e.Value.HasValue && (c.Text ?? string.Empty) != e.Value.Value)
                        ReactorBinding.WriteSuppressed(c, () => c.Text = e.Value.Value);
                },
                update:      static (ctx, c, o, n) => { })
            .HandCodedControlled<TextBoxEventPayload, string, EventHandler<TextChangedEventArgs>>(
                get:         static e => e.Value,
                set:         static (c, v) => c.Text = v,
                readBack:    static c => c.Text ?? string.Empty,
                subscribe:   static (c, h) => c.TextChanged += h,
                callback:    static e => e.OnChanged,
                trampoline:  __TextChangedTrampoline,
                slotIsNull:  static p => p.TextChangedTrampoline is null,
                setSlot:     static (p, h) => p.TextChangedTrampoline = h);
}

/// <summary>
/// A slider, as an Avalonia <see cref="Avalonia.Controls.Slider"/>. <see cref="Value"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the control owns its value until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(Slider))]
[WrapManual("Min")]
[WrapManual("Max")]
public partial record SliderElement(
    Optional<double> Value = default,
    double Min = 0,
    double Max = 100,
    Action<double>? OnValueChanged = null) : Element
{
    public Orientation Orientation { get; init; } = Orientation.Horizontal;

    /// <summary>Interval between tick marks; 0 (the default) shows none.</summary>
    public double TickFrequency { get; init; }

    public TickPlacement TickPlacement { get; init; } = TickPlacement.None;

    internal Action<Slider>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnValueChanged is not null;

    // As upstream's: Min and Max are written before Value (Customize's entries come first), so the first value
    // lands in the right range, and a bound that coerces the value drops the change event the coercion raises.
    private static partial ControlDescriptor<SliderElement, Slider> Customize(ControlDescriptor<SliderElement, Slider> d)
        => d.CoercingOneWay(
                get:               static e => e.Min,
                set:               static (c, v) => c.Minimum = v,
                coercesController: static (c, newMin) => c.Value < newMin)
            .CoercingOneWay(
                get:               static e => e.Max,
                set:               static (c, v) => c.Maximum = v,
                coercesController: static (c, newMax) => c.Value > newMax);
}
