using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for number entry and the separator (#26), as part of our own version (tier 3) of upstream's
// src/Reactor/Elements/Dsl.cs: upstream's names and parameter order, with Avalonia's value types. Separator is
// Avalonia-only, so it has Avalonia's name.

public static partial class Factories
{
    /// <summary>
    /// A number box, as an Avalonia <see cref="Avalonia.Controls.NumericUpDown"/> (upstream's name: NumberBox).
    /// <paramref name="value"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its value;
    /// <see cref="double.NaN"/> is an empty box. Upstream's <c>header</c> parameter isn't here: Avalonia's control has no
    /// header.
    /// </summary>
    public static NumberBoxElement NumberBox(Optional<double> value = default, Action<double>? onValueChanged = null)
        => new(value, onValueChanged);

    /// <summary>A horizontal line between controls, as an Avalonia <see cref="Avalonia.Controls.Separator"/>.</summary>
    public static SeparatorElement Separator() => new();
}
