using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for the button family of #26, as part of our own version (tier 3) of upstream's
// src/Reactor/Elements/Dsl.cs: upstream's names, parameter names and order, returning the same records, with Avalonia's
// value types. Each button takes a string or any element as its content. Upstream's Command overloads come when
// Command binding does.

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
        return new("", onClick) { ContentElement = content };
    }

    /// <summary>
    /// A button that stays pressed, as an Avalonia <see cref="Avalonia.Controls.Primitives.ToggleButton"/>.
    /// <paramref name="isChecked"/> is written when it changes; a click toggles the button and calls
    /// <paramref name="onIsCheckedChanged"/>.
    /// </summary>
    public static ToggleButtonElement ToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
        => new(label, isChecked, onIsCheckedChanged);

    /// <summary>A toggle button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ToggleButtonElement ToggleButton(Element content, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new("", isChecked, onIsCheckedChanged) { ContentElement = content };
    }

    /// <summary>A three-state toggle button: a click cycles checked, unchecked and indeterminate (null).</summary>
    public static ToggleButtonElement ThreeStateToggleButton(string label, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
        => new(label, checkedState == true) { IsThreeState = true, CheckedState = checkedState, OnCheckedStateChanged = onCheckedStateChanged };

    /// <summary>A three-state toggle button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static ToggleButtonElement ThreeStateToggleButton(Element content, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new("", checkedState == true)
        {
            ContentElement = content,
            IsThreeState = true,
            CheckedState = checkedState,
            OnCheckedStateChanged = onCheckedStateChanged,
        };
    }

    /// <summary>
    /// A button whose click repeats while it is held, as an Avalonia <see cref="Avalonia.Controls.RepeatButton"/>.
    /// </summary>
    public static RepeatButtonElement RepeatButton(string label, Action? onClick = null) => new(label, onClick);

    /// <summary>A repeat button with any element as its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static RepeatButtonElement RepeatButton(Element content, Action? onClick = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new("", onClick) { ContentElement = content };
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
        return new("", navigateUri, onClick) { ContentElement = content };
    }
}
