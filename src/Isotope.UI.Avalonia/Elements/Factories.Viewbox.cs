using Isotope.UI.Core;

namespace Isotope.UI;

// The Viewbox factory (#43), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs: upstream's
// name and parameter, which are Avalonia's too.

public static partial class Factories
{
    /// <summary>
    /// <paramref name="child"/> scaled to fill the space, as an Avalonia <see cref="Avalonia.Controls.Viewbox"/>; set
    /// <c>with { Stretch = ... }</c> to choose how.
    /// </summary>
    public static ViewboxElement Viewbox(Element child) => new(child);
}
