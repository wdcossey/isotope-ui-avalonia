using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Vendor.Controls.Isotope;

// The element records, as Isotope's own are written (src/Isotope.UI.Avalonia/Seam): partial records with
// [GenerateReactorDescriptor], members named after the control's (tools/naming/README.md), and the generator writing
// each descriptor and registering it.

/// <summary>
/// A <see cref="RatingBar"/>. <see cref="Value"/> is controlled: Unset (the default) leaves the control's own value, and a
/// value is written whenever the control drifts from it; <see cref="OnValueChanged"/> reports the user's changes.
/// </summary>
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record RatingBarElement(
    Optional<int> Value = default,
    int Maximum = 5,
    Action<int>? OnValueChanged = null) : Element
{
    /// <summary>The fill of the chosen squares; null leaves the control's (or a theme token's, <c>.Fill(Theme.Accent)</c>).</summary>
    public IBrush? Fill { get; init; }

    public double ItemSize { get; init; } = 16;

    internal Action<RatingBar>[] Setters { get; init; } = [];
}

/// <summary>A <see cref="SpreadPanel"/>: child elements side by side, matched by key across renders.</summary>
[GenerateReactorDescriptor(typeof(SpreadPanel))]
public partial record SpreadPanelElement(Element[] Children) : Element
{
    public double Spacing { get; init; }

    internal Action<SpreadPanel>[] Setters { get; init; } = [];
}

/// <summary>
/// A <see cref="CalloutCard"/>: a header, one child element in its content slot (reconciled in place across renders,
/// so a component there keeps its state) and an accent stripe. A member set back to null clears the control's value
/// (<c>ClearValueOnUnset</c>, as Isotope's Border has it); without it the generator leaves the last value written.
/// </summary>
[GenerateReactorDescriptor(typeof(CalloutCard), ClearValueOnUnset = true)]
public partial record CalloutCardElement(string? Header, Element Content) : Element
{
    /// <summary>The stripe's brush; null leaves the control's (or a theme token's, <c>.Accent(Theme.Accent)</c>).</summary>
    public IBrush? Accent { get; init; }

    /// <summary>Called when the card's close button is pressed.</summary>
    public Action? OnDismissed { get; init; }

    internal Action<CalloutCard>[] Setters { get; init; } = [];
}
