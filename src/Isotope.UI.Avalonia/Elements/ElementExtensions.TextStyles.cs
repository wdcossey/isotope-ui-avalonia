using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3, #44) of upstream's ApplyStyle (src/Reactor/Elements/ElementExtensions.cs), on text blocks.

public static partial class ElementExtensions
{
    /// <summary>
    /// Styles the text block from a named style: the <see cref="Avalonia.Styling.ControlTheme"/> of key
    /// <paramref name="styleName"/> in the resources the control sees (an app's theme wins, and a change to it applies
    /// without a re-render), else Isotope's theme for WinUI's TextBlock style of that name, with WinUI's values
    /// (<c>TitleTextBlockStyle</c>, <c>BodyStrongTextBlockStyle</c>, <c>CaptionTextBlockStyle</c>…):
    /// <c>TextBlock("Hello").ApplyStyle("BodyTextBlockStyle")</c>. The element's members and modifiers win over the
    /// style's values.
    /// </summary>
    /// <remarks>
    /// Unlike upstream's, which applies a style at mount only, a changed name applies on the next render. As upstream's,
    /// a name that doesn't resolve keeps the default look and warns once (the <c>Isotope-UI</c> event source), once the text
    /// block is in a logical tree. An app style for every TextBlock outranks the theme, as Avalonia's styles do. Text
    /// blocks only here: upstream's takes any element.
    /// </remarks>
    public static TextBlockElement ApplyStyle(this TextBlockElement el, string styleName)
    {
        ArgumentNullException.ThrowIfNull(styleName);
        return el with { StyleKey = styleName };
    }
}
