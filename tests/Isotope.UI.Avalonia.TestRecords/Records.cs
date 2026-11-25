using Avalonia.Controls;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Avalonia.TestRecords;

// Records outside the library (#77), over Avalonia's Slider, the built-in SliderElement's control, so the tests can mount
// both side by side. Setters is internal, as a record's
// is (a public one would be ISOTOPEGEN001); the tests see it to count the escape hatch's runs.

/// <summary>
/// A library's slider record with nothing but its members: the generator writes its descriptor and its skip
/// (ShallowEqualsCore, HasCallbacks).
/// </summary>
[GenerateReactorDescriptor(typeof(Slider))]
public partial record LibrarySliderElement(Optional<double> Value = default, Action<double>? OnValueChanged = null) : Element
{
    public double Maximum { get; init; } = 100;

    internal Action<Slider>[] Setters { get; init; } = [];
}

/// <summary>
/// A library's slider record that decides its own skip: the generator writes its descriptor and leaves ShallowEqualsCore
/// and HasCallbacks to it. It compares only Value and Setters, so a change to Maximum alone is skipped, which shows that
/// this override, not a generated one, is used.
/// </summary>
[GenerateReactorDescriptor(typeof(Slider))]
public partial record HandSliderElement(Optional<double> Value = default, Action<double>? OnValueChanged = null) : Element
{
    public double Maximum { get; init; } = 100;

    internal Action<Slider>[] Setters { get; init; } = [];

    protected override bool HasCallbacks => OnValueChanged is not null;

    protected override bool ShallowEqualsCore(Element other)
        => other is HandSliderElement o && Value.Equals(o.Value) && ReferenceEquals(Setters, o.Setters)
            && (OnValueChanged is null) == (o.OnValueChanged is null);
}
