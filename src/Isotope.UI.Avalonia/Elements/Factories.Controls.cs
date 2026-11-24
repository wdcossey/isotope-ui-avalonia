using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for the controls of #19, as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's names, parameter names and order, returning the same records, with Avalonia's value types. WrapPanel is
// Avalonia-only, so it has Avalonia's name.

public static partial class Factories
{
    // ── Toggles ─────────────────────────────────────────────────────

    /// <summary>
    /// A check box, as an Avalonia <see cref="Avalonia.Controls.CheckBox"/>. <paramref name="isChecked"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its checked state.
    /// </summary>
    public static CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? label = null)
        => new(isChecked, onIsCheckedChanged, label);

    /// <summary>
    /// A three-state check box: a click cycles checked, unchecked and indeterminate (null).
    /// <paramref name="checkedState"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its state.
    /// </summary>
    public static CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState = default, Action<bool?>? onCheckedStateChanged = null, string? label = null)
        => new(checkedState, Label: label)
        {
            IsThreeState = true,
            CheckedState = checkedState.HasValue ? checkedState.Value : null,
            OnCheckedStateChanged = onCheckedStateChanged,
        };

    /// <summary>
    /// An on/off switch, as an Avalonia <see cref="Avalonia.Controls.ToggleSwitch"/>. <paramref name="isOn"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its state.
    /// </summary>
    public static ToggleSwitchElement ToggleSwitch(Optional<bool> isOn = default, Action<bool>? onIsOnChanged = null,
        string? onContent = null, string? offContent = null, string? header = null)
        => new(isOn, onIsOnChanged, onContent, offContent) { Header = header };

    /// <summary>
    /// A radio button, as an Avalonia <see cref="Avalonia.Controls.RadioButton"/>. <paramref name="isChecked"/> defaults
    /// to <see cref="Optional{T}.Unset"/>: omit it to let the control own its checked state.
    /// </summary>
    public static RadioButtonElement RadioButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null,
        string? groupName = null)
        => new(label, isChecked, onIsCheckedChanged, groupName);

    // ── Selection ───────────────────────────────────────────────────

    /// <summary>
    /// A drop-down list, as an Avalonia <see cref="Avalonia.Controls.ComboBox"/>. <paramref name="selectedIndex"/>
    /// defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its selection; -1 selects nothing.
    /// </summary>
    public static ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
        => new(items, selectedIndex, onSelectedIndexChanged);

    /// <summary>
    /// A list to select from, as an Avalonia <see cref="Avalonia.Controls.ListBox"/>. <paramref name="selectedIndex"/>
    /// defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its selection; -1 selects nothing.
    /// </summary>
    public static ListBoxElement ListBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
        => new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };

    /// <summary>
    /// A date picker, as an Avalonia <see cref="Avalonia.Controls.DatePicker"/>. <paramref name="date"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its date.
    /// </summary>
    public static DatePickerElement DatePicker(Optional<DateTimeOffset> date = default, Action<DateTimeOffset>? onDateChanged = null)
        => new(date, onDateChanged);

    /// <summary>
    /// A time picker, as an Avalonia <see cref="Avalonia.Controls.TimePicker"/>. <paramref name="time"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its time.
    /// </summary>
    public static TimePickerElement TimePicker(Optional<TimeSpan> time = default, Action<TimeSpan>? onTimeChanged = null)
        => new(time, onTimeChanged);

    // ── Progress and media ──────────────────────────────────────────

    /// <summary>A progress bar at <paramref name="value"/> (0 to 100 by default), as an Avalonia <see cref="Avalonia.Controls.ProgressBar"/>.</summary>
    public static ProgressElement Progress(double value) => new(value);

    /// <summary>A progress bar with no value: Avalonia's indeterminate animation.</summary>
    public static ProgressElement ProgressIndeterminate() => new(null);

    /// <summary>
    /// An image from a file path, a <c>file://</c> URI or an <c>avares://</c> resource URI, as an Avalonia
    /// <see cref="Avalonia.Controls.Image"/>.
    /// </summary>
    public static ImageElement Image(string source) => new(source);

    // ── Containers ──────────────────────────────────────────────────

    /// <summary>A single child with a background, border and corners, as an Avalonia <see cref="Avalonia.Controls.Border"/>.</summary>
    public static BorderElement Border(Element? child) => new(child);

    /// <summary>A scrollable view of <paramref name="child"/>, as an Avalonia <see cref="Avalonia.Controls.ScrollViewer"/>.</summary>
    public static ScrollViewerElement ScrollViewer(Element child) => new(child);

    /// <summary>
    /// A header that shows or hides <paramref name="content"/>, as an Avalonia <see cref="Avalonia.Controls.Expander"/>.
    /// <paramref name="isExpanded"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its state.
    /// </summary>
    public static ExpanderElement Expander(string header, Element content, Optional<bool> isExpanded = default, Action<bool>? onIsExpandedChanged = null)
        => new(header, content, isExpanded, onIsExpandedChanged);

    /// <summary>
    /// Children in rows and columns, as an Avalonia <see cref="Avalonia.Controls.Grid"/>; place each with
    /// <c>.Grid(row, column)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> or <paramref name="rows"/> is null.</exception>
    public static GridElement Grid(GridSize[] columns, GridSize[] rows, params Element?[] children)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        return new(new GridDefinition(columns, rows), FilterChildren(children));
    }

    /// <summary>Children wrapped onto new lines, as an Avalonia <see cref="Avalonia.Controls.WrapPanel"/> (Avalonia's name).</summary>
    public static WrapPanelElement WrapPanel(params Element?[] children) => new(FilterChildren(children));
}
