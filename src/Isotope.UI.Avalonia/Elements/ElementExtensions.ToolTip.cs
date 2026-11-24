using Isotope.UI.Core;

namespace Isotope.UI;

// Element tooltips (#29), from upstream's src/Reactor/Elements/ElementExtensions.cs: upstream's names, Avalonia's value
// types.

public static partial class ElementExtensions
{
    /// <summary>
    /// Any element as the control's tooltip, shown when the pointer rests on it:
    /// <c>Button("Save", onSave).WithToolTip(VStack(TextBlock("Save"), TextBlock("Ctrl+S")))</c>. It is a child subtree:
    /// a component inside keeps its state across renders, and is unmounted when the tooltip goes. It takes precedence
    /// over a text <c>.ToolTip(...)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tooltip"/> is null.</exception>
    public static T WithToolTip<T>(this T el, Element tooltip) where T : Element
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        return el.WithModifiers(m => m with { RichToolTip = tooltip });
    }

    /// <summary>An element tooltip that opens on the given side of the control.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tooltip"/> is null.</exception>
    public static T WithToolTip<T>(this T el, Element tooltip, Avalonia.Controls.PlacementMode placement) where T : Element
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        return el.WithModifiers(m => m with { RichToolTip = tooltip, ToolTipPlacement = placement });
    }
}
