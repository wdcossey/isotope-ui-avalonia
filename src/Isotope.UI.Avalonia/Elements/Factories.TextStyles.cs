using Avalonia;
using Avalonia.Media;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3, #44) of upstream's named text styles: Heading, SubHeading and Caption (src/Reactor/Elements/
// Dsl.cs) and the type-ramp factories and Card (src/Reactor/Elements/Factories.NamedStyles.cs). Upstream's names, kept
// (tools/naming/renames.json): Avalonia has no named text-style factories. The parameter is `text`, as TextBlock's.

public static partial class Factories
{
    // ── Upstream's convenience text styles: fixed values ────────────

    /// <summary>
    /// A heading: 28 DIPs, bold, and automation heading level 1, so screen readers announce it as a landmark (upstream's
    /// values). Sized for the type ramp's Title slot; use it for page and section titles.
    /// </summary>
    public static TextBlockElement Heading(string text) =>
        new(text) { FontSize = 28, FontWeight = FontWeight.Bold, HeadingLevel = 1 };

    /// <summary>A sub-heading: 20 DIPs, semibold, and automation heading level 2 (upstream's values); pairs with
    /// <see cref="Heading"/>.</summary>
    public static TextBlockElement SubHeading(string text) =>
        new(text) { FontSize = 20, FontWeight = FontWeight.SemiBold, HeadingLevel = 2 };

    /// <summary>A caption: 12 DIPs (upstream's value), for secondary text such as timestamps, help and hints.</summary>
    public static TextBlockElement Caption(string text) => new(text) { FontSize = 12 };

    // ── The type ramp: WinUI's TextBlock styles, by key ─────────────
    // Each is TextBlock(text).ApplyStyle(key), as upstream's: an app's ControlTheme of that key wins, else Isotope's
    // theme with WinUI's values. Chained members and modifiers (.FontSize, .Bold(), .Foreground) win over the theme.

    /// <summary>WinUI's <c>TitleTextBlockStyle</c>: a 28 DIP semibold heading.</summary>
    public static TextBlockElement Title(string text) => TextBlock(text).ApplyStyle("TitleTextBlockStyle");

    /// <summary>WinUI's <c>SubtitleTextBlockStyle</c>: a 20 DIP semibold sub-heading.</summary>
    public static TextBlockElement Subtitle(string text) => TextBlock(text).ApplyStyle("SubtitleTextBlockStyle");

    /// <summary>WinUI's <c>BodyTextBlockStyle</c>: 14 DIP regular body text.</summary>
    public static TextBlockElement Body(string text) => TextBlock(text).ApplyStyle("BodyTextBlockStyle");

    /// <summary>WinUI's <c>BodyStrongTextBlockStyle</c>: 14 DIP semibold body text.</summary>
    public static TextBlockElement BodyStrong(string text) => TextBlock(text).ApplyStyle("BodyStrongTextBlockStyle");

    /// <summary>WinUI's <c>BodyLargeTextBlockStyle</c>: 18 DIP regular body text.</summary>
    public static TextBlockElement BodyLarge(string text) => TextBlock(text).ApplyStyle("BodyLargeTextBlockStyle");

    /// <summary>WinUI's <c>TitleLargeTextBlockStyle</c>: a 40 DIP semibold title, for the main title of a feature or
    /// landing page.</summary>
    public static TextBlockElement TitleLarge(string text) => TextBlock(text).ApplyStyle("TitleLargeTextBlockStyle");

    /// <summary>WinUI's <c>DisplayTextBlockStyle</c>: 68 DIP semibold hero text. Use it sparingly, at most once a page.</summary>
    public static TextBlockElement Display(string text) => TextBlock(text).ApplyStyle("DisplayTextBlockStyle");

    // ── Card ────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="child"/> in a card: a border with WinUI's card look (upstream's values), an 8 DIP corner radius,
    /// 16 DIP padding, the <see cref="Theme.CardBackground"/> fill and a 1 DIP <see cref="Theme.CardStroke"/> border.
    /// The brushes follow the theme. Chain on it to change any of them:
    /// <c>Card(child) with { Padding = new Thickness(24) }</c>, <c>.Background(Theme.SubtleFill)</c>.
    /// </summary>
    public static BorderElement Card(Element child) =>
        (Border(child) with { CornerRadius = 8, Padding = new Thickness(16) })
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, thickness: 1);
}
