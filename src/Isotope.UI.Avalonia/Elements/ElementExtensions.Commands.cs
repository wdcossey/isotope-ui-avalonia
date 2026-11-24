using Isotope.UI.Core;

namespace Isotope.UI;

// The .Command modifiers (#29), from upstream's src/Reactor/Elements/ElementExtensions.cs: upstream's names. Each binds a
// command to a button with any content, and clears its explicit callback, so the command takes the click over.

public static partial class ElementExtensions
{
    /// <summary>
    /// Binds a command to a button: <c>Button(HStack(Image("run.png"), TextBlock("Run"))).Command(run)</c> runs it on a
    /// click and is disabled while it can't run. Clears <see cref="ButtonElement.OnClick"/>.
    /// </summary>
    public static ButtonElement Command(this ButtonElement el, Core.Command command) =>
        el with { Command = command, OnClick = null };

    /// <summary>Binds a command to a hyperlink button. Clears <see cref="HyperlinkButtonElement.OnClick"/>.</summary>
    public static HyperlinkButtonElement Command(this HyperlinkButtonElement el, Core.Command command) =>
        el with { Command = command, OnClick = null };

    /// <summary>Binds a command to a repeat button. Clears <see cref="RepeatButtonElement.OnClick"/>.</summary>
    public static RepeatButtonElement Command(this RepeatButtonElement el, Core.Command command) =>
        el with { Command = command, OnClick = null };

    /// <summary>Binds a command to a toggle button: each toggle runs it. Clears the checked-changed callbacks.</summary>
    public static ToggleButtonElement Command(this ToggleButtonElement el, Core.Command command) =>
        el with { Command = command, OnIsCheckedChanged = null, OnCheckedStateChanged = null };
}
