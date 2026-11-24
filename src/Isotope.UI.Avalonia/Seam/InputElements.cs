using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Number entry and the separator (#26). NumericUpDown mirrors upstream's NumberBox, with Avalonia's member names (#31), as an
// Avalonia NumericUpDown; members with no Avalonia counterpart (Header, LargeChange, the spin-button placement, number
// formatters, expressions, validation mode, description) aren't on it. Separator is Avalonia-only, so Avalonia's name.

/// <summary>
/// A number box, as an Avalonia <see cref="Avalonia.Controls.NumericUpDown"/> (upstream's name: NumberBox).
/// <see cref="Value"/> defaults to <see cref="Optional{T}.Unset"/>, so the control owns its value until one is given;
/// <see cref="double.NaN"/> is an empty box, as on WinUI.
/// </summary>
/// <remarks>
/// The record keeps upstream's <see cref="double"/> values; Avalonia's control holds a <see cref="decimal"/>, so each
/// value is converted, clamped to <see cref="decimal"/>'s range (upstream's default bounds,
/// <see cref="double.MinValue"/> and <see cref="double.MaxValue"/>, become decimal's).
/// </remarks>
[GenerateReactorDescriptor(typeof(NumericUpDown), ClearValueOnUnset = true, Exclude = new[] { "ValueChanged", "Spinned" })]
[WrapManual("Value")]
[WrapManual("Minimum")]
[WrapManual("Maximum")]
[WrapManual("Increment")]
public partial record NumericUpDownElement(Optional<double> Value = default, Action<double>? OnValueChanged = null) : Element
{
    public double Minimum { get; init; } = double.MinValue;
    public double Maximum { get; init; } = double.MaxValue;
    public string? PlaceholderText { get; init; }

    /// <summary>The step of the spinner and the arrow keys (Reactor's <c>SmallChange</c>; upstream's default, 1).</summary>
    public double Increment { get; init; } = 1;

    /// <summary>How the value is shown, as a .NET format string (<c>"F2"</c>, <c>"C"</c>); Avalonia's member.</summary>
    public string? FormatString { get; init; }

    /// <summary>Whether the up and down buttons show; null keeps Avalonia's default (shown). Avalonia's member.</summary>
    public bool? ShowButtonSpinner { get; init; }

    internal Action<NumericUpDown>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnValueChanged is not null;

    private static readonly EventHandler<NumericUpDownValueChangedEventArgs> __ValueChangedTrampoline = (s, _) =>
    {
        var box = (NumericUpDown)s!;
        if (!Reconciler.TryGetReactorState(box, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as NumericUpDownElement)?.OnValueChanged?.Invoke(ToDouble(box.Value));
    };

    // As upstream's: Minimum and Maximum are written before Value (Customize's entries come first), so a new value lands
    // in the new range, and a bound that coerces the value drops the change event the coercion raises. Avalonia's
    // control coerces only with ClipValueToMinMax (off by default; a Setters action can turn it on), so only then does
    // a bound expect that event: a token for an event that never comes would swallow the next user change. Value is
    // controlled on the counter channel (Avalonia raises ValueChanged inside the write, and doesn't clip a written
    // value, even with ClipValueToMinMax). Values compare as the decimals the control holds, so a double that doesn't
    // round-trip through decimal isn't taken for drift (a write that changes nothing raises no event, and its token
    // would swallow the next user change).
    private static partial ControlDescriptor<NumericUpDownElement, NumericUpDown> Customize(ControlDescriptor<NumericUpDownElement, NumericUpDown> d)
        => d.CoercingOneWay(
                get:               static e => e.Minimum,
                set:               static (c, v) => c.Minimum = Clamp(v),
                coercesController: static (c, newMin) => c.ClipValueToMinMax && c.Value < Clamp(newMin))
            .CoercingOneWay(
                get:               static e => e.Maximum,
                set:               static (c, v) => c.Maximum = Clamp(v),
                coercesController: static (c, newMax) => c.ClipValueToMinMax && c.Value > Clamp(newMax))
            .OneWay(
                get: static e => e.Increment,
                set: static (c, v) => c.Increment = Clamp(v))
            .HandCodedControlled<V1Protocol.NumericUpDownEventPayload, double, EventHandler<NumericUpDownValueChangedEventArgs>>(
                get:         static e => e.Value,
                set:         static (c, v) => c.Value = ToDecimal(v),
                readBack:    static c => ToDouble(c.Value),
                subscribe:   static (c, h) => c.ValueChanged += h,
                callback:    static e => e.OnValueChanged,
                trampoline:  __ValueChangedTrampoline,
                slotIsNull:  static p => p.ValueChangedTrampoline is null,
                setSlot:     static (p, h) => p.ValueChangedTrampoline = h,
                comparer:    AsDecimal.Instance);

    /// <summary>The control's value for <paramref name="v"/>: null for NaN, else clamped to decimal's range.</summary>
    internal static decimal? ToDecimal(double v) => double.IsNaN(v) ? null : Clamp(v);

    internal static double ToDouble(decimal? v) => v is { } d ? (double)d : double.NaN;

    internal static decimal Clamp(double v)
    {
        if (double.IsNaN(v)) return 0;
        if (v <= (double)decimal.MinValue) return decimal.MinValue;
        if (v >= (double)decimal.MaxValue) return decimal.MaxValue;
        return (decimal)v;
    }

    private sealed class AsDecimal : IEqualityComparer<double>
    {
        internal static readonly AsDecimal Instance = new();
        public bool Equals(double a, double b) => ToDecimal(a) == ToDecimal(b);
        public int GetHashCode(double v) => ToDecimal(v).GetHashCode();
    }
}

/// <summary>A horizontal line between controls, as an Avalonia <see cref="Avalonia.Controls.Separator"/> (Avalonia-only).</summary>
[GenerateReactorDescriptor(typeof(Separator))]
public partial record SeparatorElement() : Element
{
    internal Action<Separator>[] Setters { get; init; } = [];
}
