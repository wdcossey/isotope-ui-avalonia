using Isotope.UI.Core;

namespace Isotope.UI;

// The list box's modifier (#94), from upstream's ListView modifiers in src/Reactor/Elements/ElementExtensions.cs, with
// Avalonia's value type: Avalonia's SelectionMode is a flags enum.

public static partial class ElementExtensions
{
    /// <summary>
    /// How the user selects: <c>.SelectionMode(SelectionMode.Multiple)</c>. Avalonia's flags: Single (the default),
    /// Multiple (Ctrl and Shift add to the selection; Reactor's Extended), Toggle (a tap selects or deselects; with
    /// Multiple, Reactor's Multiple) and AlwaysSelected.
    /// </summary>
    public static T SelectionMode<T>(this T el, Avalonia.Controls.SelectionMode selectionMode) where T : ListBoxElement =>
        el with { SelectionMode = selectionMode };
}
