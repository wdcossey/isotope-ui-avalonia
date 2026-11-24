using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3) of upstream's layout modifiers (src/Reactor/Elements/ElementExtensions.cs, #49): upstream's
// names and overloads, Avalonia's value types. Upstream's names are Avalonia's here (Padding, CornerRadius, BorderBrush,
// BorderThickness, MinWidth…, IsVisible, HorizontalContentAlignment), so none is renamed; WithBorder and Center are
// upstream's shorthands (their ThemeRef overloads are in ElementExtensions.Theme.cs, #41). Where each is written:
// Reconciler.ApplyLayoutModifiers. Not here yet: the BiDi-aware MarginInlineStart/PaddingInlineStart family and BorderInlineStart,
// the obsolete Visible, and upstream's HorizontalAlignment/VerticalAlignment aliases of HAlign/VAlign (#46 names those).

public static partial class ElementExtensions
{
    // ── Padding ─────────────────────────────────────────────────────

    /// <summary>The same padding on every side: space between the control's edge (or border) and its content.</summary>
    /// <remarks>
    /// Written on a templated control, a border and a text block. A stack, grid or wrap panel, which have no padding on
    /// Avalonia, is mounted inside a <see cref="Avalonia.Controls.Border"/> that has it; a flex panel ignores it, as
    /// upstream's does (use <see cref="FlexPadding(FlexElement, double)"/>).
    /// </remarks>
    public static T Padding<T>(this T el, double uniform) where T : Element => el.Padding(new Thickness(uniform));

    /// <summary>Horizontal first, then vertical, as everywhere in Reactor.</summary>
    public static T Padding<T>(this T el, double horizontal, double vertical) where T : Element =>
        el.Padding(new Thickness(horizontal, vertical, horizontal, vertical));

    /// <summary>A padding per side; unnamed sides are 0: <c>.Padding(top: 8)</c>.</summary>
    public static T Padding<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element =>
        el.Padding(new Thickness(left, top, right, bottom));

    public static T Padding<T>(this T el, Thickness thickness) where T : Element =>
        el.WithModifiers(m => m with { Padding = thickness });

    // ── Size limits ─────────────────────────────────────────────────

    public static T MinWidth<T>(this T el, double w) where T : Element =>
        el.WithModifiers(m => m with { MinWidth = w });

    public static T MinHeight<T>(this T el, double h) where T : Element =>
        el.WithModifiers(m => m with { MinHeight = h });

    public static T MaxWidth<T>(this T el, double w) where T : Element =>
        el.WithModifiers(m => m with { MaxWidth = w });

    public static T MaxHeight<T>(this T el, double h) where T : Element =>
        el.WithModifiers(m => m with { MaxHeight = h });

    // ── Alignment ───────────────────────────────────────────────────

    /// <summary>Centred both ways in the space its parent gives it (<c>HAlign</c> and <c>VAlign</c> <c>Center</c>).</summary>
    public static T Center<T>(this T el) where T : Element =>
        el.WithModifiers(m => m with
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });

    /// <summary>Where a content control (a button, a text box, …) places its content across.</summary>
    public static T HorizontalContentAlignment<T>(this T el, HorizontalAlignment alignment) where T : Element =>
        el.WithModifiers(m => m with { HorizontalContentAlignment = alignment });

    /// <summary>Where a content control (a button, a text box, …) places its content down.</summary>
    public static T VerticalContentAlignment<T>(this T el, VerticalAlignment alignment) where T : Element =>
        el.WithModifiers(m => m with { VerticalContentAlignment = alignment });

    // ── Visibility ──────────────────────────────────────────────────

    /// <summary>Whether the control is shown; a hidden control takes no space (Avalonia's <c>IsVisible</c>).</summary>
    public static T IsVisible<T>(this T el, bool isVisible) where T : Element =>
        el.WithModifiers(m => m with { IsVisible = isVisible });

    // ── Corners and borders (a templated control, a border, and a stack, grid or wrap panel) ──

    /// <summary>The same radius on every corner, in DIPs.</summary>
    public static T CornerRadius<T>(this T el, double radius) where T : Element => el.CornerRadius(new Avalonia.CornerRadius(radius));

    public static T CornerRadius<T>(this T el, double topLeft, double topRight, double bottomRight, double bottomLeft) where T : Element =>
        el.CornerRadius(new Avalonia.CornerRadius(topLeft, topRight, bottomRight, bottomLeft));

    public static T CornerRadius<T>(this T el, Avalonia.CornerRadius radius) where T : Element =>
        el.WithModifiers(m => m with { CornerRadius = radius });

    /// <summary>The border's colour, from a colour string Avalonia parses (<c>"#FF0000"</c>, <c>"Red"</c>).</summary>
    public static T BorderBrush<T>(this T el, string color) where T : Element => el.BorderBrush(Brush.Parse(color));

    /// <summary>The border's brush; a null brush leaves the control's own.</summary>
    public static T BorderBrush<T>(this T el, IBrush? brush) where T : Element =>
        el.WithModifiers(m => m with { BorderBrush = brush });

    /// <summary>The same border width on every side, in DIPs.</summary>
    public static T BorderThickness<T>(this T el, double thickness) where T : Element => el.BorderThickness(new Thickness(thickness));

    /// <summary>Horizontal first, then vertical.</summary>
    public static T BorderThickness<T>(this T el, double horizontal, double vertical) where T : Element =>
        el.BorderThickness(new Thickness(horizontal, vertical, horizontal, vertical));

    /// <summary>A border width per side; unnamed sides are 0.</summary>
    public static T BorderThickness<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element =>
        el.BorderThickness(new Thickness(left, top, right, bottom));

    public static T BorderThickness<T>(this T el, Thickness thickness) where T : Element =>
        el.WithModifiers(m => m with { BorderThickness = thickness });

    /// <summary>A border from a colour string, <paramref name="thickness"/> DIPs wide on every side.</summary>
    public static T WithBorder<T>(this T el, string color, double thickness = 1) where T : Element =>
        el.WithBorder(Brush.Parse(color), thickness);

    /// <summary>A border with <paramref name="brush"/>, <paramref name="thickness"/> DIPs wide on every side.</summary>
    public static T WithBorder<T>(this T el, IBrush brush, double thickness = 1) where T : Element =>
        el.WithModifiers(m => m with { BorderBrush = brush, BorderThickness = new Thickness(thickness) });

    // ── Stack and flex sugar ────────────────────────────────────────

    /// <summary>Space between the stack's children, in DIPs.</summary>
    public static StackElement Spacing(this StackElement el, double spacing) => el with { Spacing = spacing };

    /// <summary>The flex container's padding (Yoga's), on every side.</summary>
    public static FlexElement FlexPadding(this FlexElement el, double uniform) => el with { FlexPadding = new Thickness(uniform) };

    /// <summary>Horizontal first, then vertical.</summary>
    public static FlexElement FlexPadding(this FlexElement el, double horizontal, double vertical) =>
        el with { FlexPadding = new Thickness(horizontal, vertical, horizontal, vertical) };

    public static FlexElement FlexPadding(this FlexElement el, double left, double top, double right, double bottom) =>
        el with { FlexPadding = new Thickness(left, top, right, bottom) };
}
