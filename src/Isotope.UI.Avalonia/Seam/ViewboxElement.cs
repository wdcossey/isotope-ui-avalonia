using Avalonia.Controls;
using Avalonia.Media;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The Viewbox (#43): upstream's ViewboxElement, as an Avalonia Viewbox (the same name and members in both). As
// upstream's, the descriptor is generated: the child is the control's single content slot, reconciled in place, and a
// member left null gives the control its default again (Avalonia's ClearValue channel; upstream's leaves the last value).

/// <summary>
/// One child scaled to fill the space, as an Avalonia <see cref="Avalonia.Controls.Viewbox"/>. <see cref="Stretch"/> and
/// <see cref="StretchDirection"/> left null keep Avalonia's defaults (<see cref="Avalonia.Media.Stretch.Uniform"/>, both
/// directions).
/// </summary>
[GenerateReactorDescriptor(typeof(Viewbox), ClearValueOnUnset = true)]
public partial record ViewboxElement(Element Child) : Element
{
    public Stretch? Stretch { get; init; }

    public StretchDirection? StretchDirection { get; init; }

    internal Action<Viewbox>[] Setters { get; init; } = [];
}
