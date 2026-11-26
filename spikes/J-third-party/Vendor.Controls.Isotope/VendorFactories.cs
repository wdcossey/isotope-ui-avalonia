using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;

namespace Vendor.Controls.Isotope;

// The #region blocks are C# blocks of docs/guide/writing-controls.md (DocSnippetTests): change both together.

#region controls-factories
/// <summary>
/// The factories, one per control, named after it (tools/naming/README.md), with the members' names in camel case:
/// <c>using static Vendor.Controls.Isotope.VendorFactories;</c> beside <c>using static Isotope.UI.Factories;</c>. The class
/// isn't called <c>Factories</c>, so a file that imports both namespaces can still name either class.
/// </summary>
public static class VendorFactories
{
    /// <summary>A rating of <paramref name="maximum"/> squares: <c>RatingBar(stars, onValueChanged: setStars)</c>.</summary>
    public static RatingBarElement RatingBar(Optional<int> value = default, int maximum = 5, Action<int>? onValueChanged = null,
        Action<int>? onCleared = null) =>
        new(value, maximum, onValueChanged) { OnCleared = onCleared };

    /// <summary>A panel of children side by side: <c>SpreadPanel(RatingBar(1), RatingBar(2))</c>.</summary>
    public static SpreadPanelElement SpreadPanel(params Element[] children) => new(children);

    /// <summary>A card with a header and one child: <c>CalloutCard("Tip", TextBlock("..."))</c>.</summary>
    public static CalloutCardElement CalloutCard(string? header, Element content, Action? onDismissed = null) =>
        new(header, content) { OnDismissed = onDismissed };
}
#endregion

#region controls-modifiers
/// <summary>The modifiers of the vendor's elements, named after the property each sets.</summary>
public static partial class VendorElementExtensions
{
    public static RatingBarElement Fill(this RatingBarElement el, IBrush? brush) => el with { Fill = brush };

    public static CalloutCardElement Accent(this CalloutCardElement el, IBrush? brush) => el with { Accent = brush };

    /// <summary>Runs <paramref name="configure"/> on the control after every mount and update (the escape hatch).</summary>
    public static RatingBarElement Set(this RatingBarElement el, Action<RatingBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    /// <summary>Runs <paramref name="configure"/> on the control after every mount and update (the escape hatch).</summary>
    public static CalloutCardElement Set(this CalloutCardElement el, Action<CalloutCard> configure) =>
        el with { Setters = [.. el.Setters, configure] };
}
#endregion

#region controls-token-modifiers
public static partial class VendorElementExtensions
{
    /// <summary>The fill from a theme token; it follows the control's theme with no re-render, as Isotope's own do.</summary>
    public static RatingBarElement Fill(this RatingBarElement el, ThemeRef token) => WithToken(el, "Fill", token);

    /// <summary>The accent from a theme token; it follows the control's theme with no re-render.</summary>
    public static CalloutCardElement Accent(this CalloutCardElement el, ThemeRef token) => WithToken(el, "Accent", token);

    // Element.ThemeBindings maps a property name to a token, which the engine binds on the control. Isotope's own token
    // modifiers write it too; the dictionary is public, so a library does the same for its own brush properties.
    private static T WithToken<T>(T el, string property, ThemeRef token) where T : Element =>
        el with
        {
            ThemeBindings = el.ThemeBindings is null
                ? new Dictionary<string, ThemeRef> { [property] = token }
                : new Dictionary<string, ThemeRef>(el.ThemeBindings) { [property] = token },
        };
}
#endregion
