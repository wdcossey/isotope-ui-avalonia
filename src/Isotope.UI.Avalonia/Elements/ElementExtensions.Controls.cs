using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3) of more of upstream's src/Reactor/Elements/ElementExtensions.cs (#26): upstream's names,
// Avalonia's value types.

public static partial class ElementExtensions
{
    /// <summary>An element as the expander's header, in place of its string header (upstream's modifier).</summary>
    public static ExpanderElement HeaderContent(this ExpanderElement el, Element header) =>
        el with { HeaderContent = header };
}
