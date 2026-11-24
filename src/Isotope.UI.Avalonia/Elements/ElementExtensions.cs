using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3) of the key and modifier helpers in upstream's src/Reactor/Elements/ElementExtensions.cs,
// for what ElementModifiers holds today. Upstream's names and overloads; Avalonia's value types.

/// <summary>Keys and modifiers on elements: <c>TextBlock("Hi").Margin(8).WithKey("greeting")</c>.</summary>
public static partial class ElementExtensions
{
    /// <summary>This element with <paramref name="key"/>: its stable identity among its siblings.</summary>
    public static T WithKey<T>(this T el, string key) where T : Element =>
        ReferenceEquals(el.Key, key) ? el : el with { Key = key };

    /// <summary>The same margin on every side.</summary>
    public static T Margin<T>(this T el, double uniform) where T : Element => el.Margin(new Thickness(uniform));

    /// <summary>Horizontal first, then vertical, as everywhere in Reactor.</summary>
    public static T Margin<T>(this T el, double horizontal, double vertical) where T : Element =>
        el.Margin(new Thickness(horizontal, vertical, horizontal, vertical));

    /// <summary>A margin per side; unnamed sides are 0: <c>.Margin(top: 10)</c>.</summary>
    public static T Margin<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element =>
        el.Margin(new Thickness(left, top, right, bottom));

    public static T Margin<T>(this T el, Thickness thickness) where T : Element =>
        el.WithModifiers(m => m with { Margin = thickness });

    public static T Width<T>(this T el, double width) where T : Element =>
        el.WithModifiers(m => m with { Width = width });

    public static T Height<T>(this T el, double height) where T : Element =>
        el.WithModifiers(m => m with { Height = height });

    public static T Size<T>(this T el, double width, double height) where T : Element =>
        el.WithModifiers(m => m with { Width = width, Height = height });

    public static T HorizontalAlignment<T>(this T el, HorizontalAlignment alignment) where T : Element =>
        el.WithModifiers(m => m with { HorizontalAlignment = alignment });

    public static T VerticalAlignment<T>(this T el, VerticalAlignment alignment) where T : Element =>
        el.WithModifiers(m => m with { VerticalAlignment = alignment });

    public static T Opacity<T>(this T el, double opacity) where T : Element =>
        el.WithModifiers(m => m with { Opacity = opacity });

    /// <summary>Whether the control takes input; <c>.IsEnabled(false)</c> disables it.</summary>
    public static T IsEnabled<T>(this T el, bool enabled = true) where T : Element =>
        el.WithModifiers(m => m with { IsEnabled = enabled });

    /// <summary>A background from a colour string Avalonia parses (<c>"#FF0000"</c>, <c>"Red"</c>).</summary>
    public static T Background<T>(this T el, string color) where T : Element => el.Background(Brush.Parse(color));

    /// <summary>A background brush; <see langword="null"/> leaves the control's own.</summary>
    public static T Background<T>(this T el, IBrush? brush) where T : Element =>
        el.WithModifiers(m => m with { Background = brush });

    /// <summary>The name assistive technology reads for this control.</summary>
    public static T AutomationName<T>(this T el, string name) where T : Element =>
        el.WithModifiers(m => m with { AutomationName = name });
}
