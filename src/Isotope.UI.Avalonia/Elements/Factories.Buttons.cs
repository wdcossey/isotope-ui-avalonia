using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for the button family of #26, as part of our own version (tier 3) of upstream's
// src/Reactor/Elements/Dsl.cs: upstream's names and parameter order, returning the same records, with Avalonia's member
// names as parameter names (#31) and Avalonia's value types. Each button takes a string or any element as its content.

public static partial class Factories
{
    /// <summary>
    /// A push button with any element as its content, as an Avalonia <see cref="Avalonia.Controls.Button"/>:
    /// <c>Button(HStack(Image("save.png"), TextBlock("Save")), onSave)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ButtonElement Button(Element content, Action? onClick = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, onClick);
    }

    /// <summary>
    /// A button that stays pressed, as an Avalonia <see cref="Avalonia.Controls.Primitives.ToggleButton"/>.
    /// <paramref name="isChecked"/> is written when it changes; a click toggles the button and calls
    /// <paramref name="onIsCheckedChanged"/>.
    /// </summary>
    public static ToggleButtonElement ToggleButton(string content, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
        => new(content, isChecked, TwoStateToggle(onIsCheckedChanged));

    /// <summary>A toggle button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ToggleButtonElement ToggleButton(Element content, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, isChecked, TwoStateToggle(onIsCheckedChanged));
    }

    /// <summary>A three-state toggle button: a click cycles checked, unchecked and indeterminate (null).</summary>
    public static ToggleButtonElement ThreeStateToggleButton(string content, bool? isChecked = null, Action<bool?>? onIsCheckedChanged = null)
        => new(content, isChecked, onIsCheckedChanged) { IsThreeState = true };

    /// <summary>A three-state toggle button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ToggleButtonElement ThreeStateToggleButton(Element content, bool? isChecked = null, Action<bool?>? onIsCheckedChanged = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, isChecked, onIsCheckedChanged) { IsThreeState = true };
    }

    // A two-state toggle's callback on the record's three-state one (a two-state button is never indeterminate).
    private static Action<bool?>? TwoStateToggle(Action<bool>? callback) =>
        callback is null ? null : value => callback(value ?? false);

    /// <summary>
    /// A button whose click repeats while it is held, as an Avalonia <see cref="Avalonia.Controls.RepeatButton"/>.
    /// </summary>
    public static RepeatButtonElement RepeatButton(string content, Action? onClick = null) => new(content, onClick);

    /// <summary>A repeat button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static RepeatButtonElement RepeatButton(Element content, Action? onClick = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, onClick);
    }

    /// <summary>
    /// A button that looks like a link, as an Avalonia <see cref="Avalonia.Controls.HyperlinkButton"/>; Avalonia opens
    /// <paramref name="navigateUri"/>, when given, on a click.
    /// </summary>
    public static HyperlinkButtonElement HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null)
        => new(content, navigateUri, onClick);

    /// <summary>A hyperlink button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static HyperlinkButtonElement HyperlinkButton(Element content, Uri? navigateUri = null, Action? onClick = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, navigateUri, onClick);
    }
}
