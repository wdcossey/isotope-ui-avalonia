using Avalonia;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3, #41) of upstream's ThemeRef modifiers (src/Reactor/Elements/ElementExtensions.cs):
// .Background(Theme.CardBackground), .Foreground(Theme.SecondaryText), .WithBorder(Theme.CardStroke)…

public static partial class ElementExtensions
{
    /// <summary>
    /// The background from a theme token: <c>VStack(children).Background(Theme.CardBackground)</c>. It follows the
    /// control's theme (light, dark, high contrast) without a re-render; a background brush set on the element wins.
    /// </summary>
    public static T Background<T>(this T el, ThemeRef theme) where T : Element => ModifyTheme(el, "Background", theme);

    /// <summary>
    /// The foreground from a theme token, on a text block or a templated control:
    /// <c>TextBlock("Hello").Foreground(Theme.SecondaryText)</c>. It follows the control's theme.
    /// </summary>
    public static T Foreground<T>(this T el, ThemeRef theme) where T : Element => ModifyTheme(el, "Foreground", theme);

    /// <summary>The border brush from a theme token, on a border or a templated control. It follows the control's theme.</summary>
    public static T BorderBrush<T>(this T el, ThemeRef theme) where T : Element => ModifyTheme(el, "BorderBrush", theme);

    /// <summary>
    /// A border from a theme token, <paramref name="thickness"/> wide on every side:
    /// <c>Border(child).WithBorder(Theme.CardStroke)</c>. On a border element the width is its own
    /// <see cref="BorderElement.BorderThickness"/>; on a templated control, the modifier's. Avalonia's panels have no border.
    /// </summary>
    public static T WithBorder<T>(this T el, ThemeRef theme, double thickness = 1) where T : Element
        => ModifyTheme((T)WithBorderWidth(el, thickness), "BorderBrush", theme);

    // A border element's own width, also one a ModifiedElement wraps (whose control is the border's); else the modifier.
    private static Element WithBorderWidth(Element el, double thickness) => el switch
    {
        BorderElement border => border with { BorderThickness = thickness },
        ModifiedElement { Inner: var inner } wrapper when Innermost(inner) is BorderElement =>
            wrapper with { Inner = WithBorderWidth(inner, thickness) },
        _ => el.WithModifiers(m => m with { BorderThickness = new Thickness(thickness) }),
    };

    private static Element Innermost(Element el) => el is ModifiedElement m ? Innermost(m.Inner) : el;

    /// <summary>The brush behind a split view's pane, from a theme token; the element's own pane brush wins.</summary>
    public static SplitViewElement PaneBackground(this SplitViewElement el, ThemeRef theme) => ModifyTheme(el, "PaneBackground", theme);

    private static T ModifyTheme<T>(T el, string property, ThemeRef theme) where T : Element
    {
        var bindings = el.ThemeBindings is not null
            ? new Dictionary<string, ThemeRef>(el.ThemeBindings) { [property] = theme }
            : new Dictionary<string, ThemeRef> { [property] = theme };
        return el with { ThemeBindings = bindings };
    }
}
