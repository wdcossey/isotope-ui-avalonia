using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Isotope.UI.Core;

// Layout modifiers (#49), written where Avalonia has the property, as upstream writes them where WinUI has it.
public sealed partial class Reconciler
{
    // A stack, grid or wrap panel whose modifiers ask for padding, a border width or rounded corners is mounted inside a
    // PanelChrome border. A flex panel isn't, as upstream's FlexPanel ignores them (its padding is FlexPadding). A border
    // brush alone (a brush modifier or token) draws nothing with no width, so it doesn't ask for one: a brush that comes
    // and goes, as a selection highlight's does, doesn't mount the panel again.
    // Which elements' controls lack the four properties is known from the element type, so the choice can be made from
    // the elements alone (CanUpdate): a change in it mounts the element afresh, as a change of control type does.
    internal static bool NeedsPanelChrome(Element element, ElementModifiers? modifiers) =>
        modifiers is { HasPanelChrome: true } && element is StackElement or GridElement or WrapPanelElement;

    // The control a handler mounted for the element in this slot: the panel inside a chrome border, or the control.
    private static UIElement HandlerControl(UIElement control) => control is PanelChrome { Child: { } panel } ? panel : control;

    private static PanelChrome WrapInPanelChrome(UIElement panel) => new() { Child = panel };

    // The element a slot's control represents is recorded on the control, and on the panel inside a chrome border too:
    // the handler tags its panel when it mounts or updates it, and a skip, which doesn't reach the handler, refreshes both.
    private static void SetMountedElement(UIElement control, Element element)
    {
        GetOrCreateReactorState(control).Element = element;
        if (control is PanelChrome { Child: { } panel }) GetOrCreateReactorState(panel).Element = element;
    }

    private static void ApplyLayoutModifiers(UIElement control, Element? oldEl, ElementModifiers? oldM, Element newEl, ElementModifiers? newM)
    {
        // A Border element's own Padding, BorderBrush, BorderThickness and CornerRadius and the modifiers of the same
        // name are written as one value, the element's first, so neither overwrites the other (as Background is).
        var oldB = oldEl as BorderElement;
        var newB = newEl as BorderElement;
        if (oldM is null && newM is null && oldB is null && newB is null) return;
        Set(control, Layoutable.MinWidthProperty, oldM?.MinWidth, newM?.MinWidth);
        Set(control, Layoutable.MinHeightProperty, oldM?.MinHeight, newM?.MinHeight);
        Set(control, Layoutable.MaxWidthProperty, oldM?.MaxWidth, newM?.MaxWidth);
        Set(control, Layoutable.MaxHeightProperty, oldM?.MaxHeight, newM?.MaxHeight);
        Set(control, Visual.IsVisibleProperty, oldM?.IsVisible, newM?.IsVisible);
        // Content alignment, on the controls that declare it (ContentControl's property, which TextBox, ComboBox,
        // NumericUpDown, … add as owners), as upstream writes it on a WinUI Control.
        if ((oldM?.HorizontalContentAlignment ?? newM?.HorizontalContentAlignment) is not null
            && AvaloniaPropertyRegistry.Instance.IsRegistered(control, ContentControl.HorizontalContentAlignmentProperty))
            Set(control, ContentControl.HorizontalContentAlignmentProperty, oldM?.HorizontalContentAlignment, newM?.HorizontalContentAlignment);
        if ((oldM?.VerticalContentAlignment ?? newM?.VerticalContentAlignment) is not null
            && AvaloniaPropertyRegistry.Instance.IsRegistered(control, ContentControl.VerticalContentAlignmentProperty))
            Set(control, ContentControl.VerticalContentAlignmentProperty, oldM?.VerticalContentAlignment, newM?.VerticalContentAlignment);
        switch (control)
        {
            // A Border element, a panel's chrome border, or a component's wrapper.
            case Border:
                Set(control, Decorator.PaddingProperty, oldB?.Padding ?? oldM?.Padding, newB?.Padding ?? newM?.Padding);
                SetRef(control, Border.BorderBrushProperty, oldB?.BorderBrush ?? oldM?.BorderBrush, newB?.BorderBrush ?? newM?.BorderBrush);
                Set(control, Border.BorderThicknessProperty, Uniform(oldB?.BorderThickness) ?? oldM?.BorderThickness,
                    Uniform(newB?.BorderThickness) ?? newM?.BorderThickness);
                Set(control, Border.CornerRadiusProperty, Corners(oldB?.CornerRadius) ?? oldM?.CornerRadius,
                    Corners(newB?.CornerRadius) ?? newM?.CornerRadius);
                break;
            case TemplatedControl:
                Set(control, TemplatedControl.PaddingProperty, oldM?.Padding, newM?.Padding);
                SetRef(control, TemplatedControl.BorderBrushProperty, oldM?.BorderBrush, newM?.BorderBrush);
                Set(control, TemplatedControl.BorderThicknessProperty, oldM?.BorderThickness, newM?.BorderThickness);
                Set(control, TemplatedControl.CornerRadiusProperty, oldM?.CornerRadius, newM?.CornerRadius);
                break;
            // A text block has padding, and no border (as WinUI's).
            case TextBlock:
                Set(control, TextBlock.PaddingProperty, oldM?.Padding, newM?.Padding);
                break;
        }
    }

    private static Thickness? Uniform(double? width) => width is { } w ? new Thickness(w) : null;

    private static CornerRadius? Corners(double? radius) => radius is { } r ? new CornerRadius(r) : null;
}
