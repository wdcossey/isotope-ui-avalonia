using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Isotope.UI.Core;

// Layout modifiers (#49): upstream's padding, border, corner, size-limit, content-alignment and visibility modifiers,
// with Avalonia's value types. The reconciler writes them (Reconciler.ApplyLayoutModifiers).

public partial record ElementModifiers
{
    /// <summary>
    /// Space between the control's edge (or border) and its content (Avalonia's <c>Padding</c>), on a templated control,
    /// a border or a text block. A stack, grid or wrap panel has none on Avalonia: it is mounted inside a border that has
    /// it (<see cref="HasPanelChrome"/>). A flex panel ignores it, as upstream's does (<c>FlexPadding</c> is Yoga's).
    /// </summary>
    public Thickness? Padding { get; init; }

    /// <summary>The border's brush, on a templated control or a border (and a panel in its border, as <see cref="Padding"/>).</summary>
    public IBrush? BorderBrush { get; init; }

    /// <summary>The corners' radii, where <see cref="BorderBrush"/> is written.</summary>
    public CornerRadius? CornerRadius { get; init; }

    public double? MinWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxWidth { get; init; }
    public double? MaxHeight { get; init; }

    /// <summary>Where a control that has the property (a content control, a text box, …) places its content across.</summary>
    public HorizontalAlignment? HorizontalContentAlignment { get; init; }

    /// <summary>Where a control that has the property places its content down.</summary>
    public VerticalAlignment? VerticalContentAlignment { get; init; }

    /// <summary>Whether the control is shown; a hidden control takes no space (Avalonia's <c>IsVisible</c>).</summary>
    public bool? IsVisible { get; init; }

    /// <summary>
    /// Whether a stack, grid or wrap panel with these modifiers needs a border around it (<see cref="PanelChrome"/>):
    /// WinUI's StackPanel and Grid have <c>Padding</c>, <c>BorderBrush</c>, <c>BorderThickness</c> and
    /// <c>CornerRadius</c>, and Avalonia's panels have none of them. A brush alone doesn't count: with no width it
    /// draws nothing, and a brush that comes and goes then doesn't mount the panel again.
    /// </summary>
    internal bool HasPanelChrome => Padding is not null || BorderThickness is not null || CornerRadius is not null;
}

/// <summary>
/// The border a stack, grid or wrap panel element is mounted inside when its modifiers give it padding, a border or
/// rounded corners (#49), which Avalonia's panels don't have: the Avalonia way to pad a panel. It holds the element's
/// modifiers (margin, size, alignment, background, tooltip…) and is the control in the parent's slot; the panel inside
/// it stays the element's control for its own members and children.
/// </summary>
internal sealed class PanelChrome : Border
{
}
