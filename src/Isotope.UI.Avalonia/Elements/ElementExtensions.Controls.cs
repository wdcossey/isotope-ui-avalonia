using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3) of more of upstream's src/Reactor/Elements/ElementExtensions.cs (#26): upstream's names,
// Avalonia's value types.

public static partial class ElementExtensions
{
    /// <summary>A text tooltip, shown when the pointer rests on the control: <c>Button("Save", onSave).ToolTip("Save (Ctrl+S)")</c>.</summary>
    public static T ToolTip<T>(this T el, string tip) where T : Element =>
        el.WithModifiers(m => m with { ToolTip = tip });

    /// <summary>A text tooltip that opens on the given side of the control.</summary>
    public static T ToolTip<T>(this T el, string tip, Avalonia.Controls.PlacementMode placement) where T : Element =>
        el.WithModifiers(m => m with { ToolTip = tip, ToolTipPlacement = placement });

    /// <summary>Which side of the control its tooltip opens on.</summary>
    public static T ToolTipPlacement<T>(this T el, Avalonia.Controls.PlacementMode placement) where T : Element =>
        el.WithModifiers(m => m with { ToolTipPlacement = placement });

    /// <summary>
    /// A context menu: <c>TextBlock(name).WithContextFlyout(MenuItems(MenuItem("Rename", rename), MenuItem("Delete", delete)))</c>,
    /// opened by a right-click or the menu key, as an Avalonia <see cref="Avalonia.Controls.MenuFlyout"/> on the
    /// control's <c>ContextFlyout</c>.
    /// </summary>
    public static T WithContextFlyout<T>(this T el, MenuFlyoutContentElement contextFlyout) where T : Element =>
        el.WithModifiers(m => m with { ContextFlyout = contextFlyout });

    /// <summary>An element as the expander's header, in place of its string header (upstream's modifier).</summary>
    public static ExpanderElement HeaderContent(this ExpanderElement el, Element header) =>
        el with { HeaderContent = header };
}
