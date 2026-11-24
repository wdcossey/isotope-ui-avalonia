using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for the controls of #19, as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's names and parameter order, returning the same records, with Avalonia's member names as parameter names
// (#31) and Avalonia's value types. WrapPanel is Avalonia-only, so it has Avalonia's name.

public static partial class Factories
{
    // ── Toggles ─────────────────────────────────────────────────────

    /// <summary>
    /// A check box, as an Avalonia <see cref="Avalonia.Controls.CheckBox"/>. <paramref name="isChecked"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its checked state. <paramref name="onIsCheckedChanged"/>
    /// is called with each checked or unchecked value the user gives it.
    /// </summary>
    public static CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? content = null)
        => new(isChecked, TwoState(onIsCheckedChanged), content);

    /// <summary>
    /// A three-state check box: a click cycles checked, unchecked and indeterminate (null).
    /// <paramref name="isChecked"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its state.
    /// </summary>
    public static CheckBoxElement ThreeStateCheckBox(Optional<bool?> isChecked = default, Action<bool?>? onIsCheckedChanged = null, string? content = null)
        => new(isChecked, onIsCheckedChanged, content) { IsThreeState = true };

    // A two-state callback on a record's three-state one (CheckBox, ToggleButton): called only for a checked or unchecked
    // value, so a control set three-state with a with-expression doesn't report its indeterminate state as unchecked.
    private static Action<bool?>? TwoState(Action<bool>? callback) =>
        callback is null ? null : value =>
        {
            if (value.HasValue) callback(value.Value);
        };

    /// <summary>
    /// An on/off switch, as an Avalonia <see cref="Avalonia.Controls.ToggleSwitch"/>. <paramref name="isChecked"/>
    /// defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its state. <paramref name="content"/>
    /// is the text beside it (Reactor's <c>header</c>).
    /// </summary>
    public static ToggleSwitchElement ToggleSwitch(Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null,
        string? onContent = null, string? offContent = null, string? content = null)
        => new(isChecked, onIsCheckedChanged, onContent, offContent) { Content = content };

    /// <summary>
    /// A radio button, as an Avalonia <see cref="Avalonia.Controls.RadioButton"/>. <paramref name="isChecked"/> defaults
    /// to <see cref="Optional{T}.Unset"/>: omit it to let the control own its checked state.
    /// </summary>
    public static RadioButtonElement RadioButton(string content, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null,
        string? groupName = null)
        => new(content, isChecked, onIsCheckedChanged, groupName);

    // ── Selection ───────────────────────────────────────────────────

    /// <summary>
    /// A drop-down list, as an Avalonia <see cref="Avalonia.Controls.ComboBox"/>. <paramref name="selectedIndex"/>
    /// defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its selection; -1 selects nothing.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items, selectedIndex, onSelectedIndexChanged);
    }

    /// <summary>
    /// A list to select from, as an Avalonia <see cref="Avalonia.Controls.ListBox"/>. <paramref name="selectedIndex"/>
    /// defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its selection; -1 selects nothing.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static ListBoxElement ListBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// A date picker, as an Avalonia <see cref="Avalonia.Controls.DatePicker"/>. <paramref name="selectedDate"/> defaults
    /// to <see cref="Optional{T}.Unset"/>: omit it to let the control own its date.
    /// </summary>
    public static DatePickerElement DatePicker(Optional<DateTimeOffset> selectedDate = default, Action<DateTimeOffset>? onSelectedDateChanged = null)
        => new(selectedDate, onSelectedDateChanged);

    /// <summary>
    /// A time picker, as an Avalonia <see cref="Avalonia.Controls.TimePicker"/>. <paramref name="selectedTime"/> defaults
    /// to <see cref="Optional{T}.Unset"/>: omit it to let the control own its time.
    /// </summary>
    public static TimePickerElement TimePicker(Optional<TimeSpan> selectedTime = default, Action<TimeSpan>? onSelectedTimeChanged = null)
        => new(selectedTime, onSelectedTimeChanged);

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

    /// <summary>A scrollable view of <paramref name="content"/>, as an Avalonia <see cref="Avalonia.Controls.ScrollViewer"/>.</summary>
    public static ScrollViewerElement ScrollViewer(Element content) => new(content);

    /// <summary>
    /// A header that shows or hides <paramref name="content"/>, as an Avalonia <see cref="Avalonia.Controls.Expander"/>.
    /// <paramref name="isExpanded"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own its state.
    /// </summary>
    public static ExpanderElement Expander(string header, Element content, Optional<bool> isExpanded = default, Action<bool>? onIsExpandedChanged = null)
        => new(header, content, isExpanded, onIsExpandedChanged);

    /// <summary>
    /// Children in rows and columns, as an Avalonia <see cref="Avalonia.Controls.Grid"/>; place each with
    /// <c>.Grid(row, column)</c>. Build tracks with <see cref="GridSize"/> (<c>GridSize.Star()</c>, <c>GridSize.Auto</c>,
    /// <c>GridSize.Px(120)</c>) or <see cref="GridSize.Parse"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="columnDefinitions"/> or <paramref name="rowDefinitions"/> is null.</exception>
    public static GridElement Grid(GridSize[] columnDefinitions, GridSize[] rowDefinitions, params Element?[] children)
    {
        ArgumentNullException.ThrowIfNull(columnDefinitions);
        ArgumentNullException.ThrowIfNull(rowDefinitions);
        return new(columnDefinitions, rowDefinitions, FilterChildren(children));
    }

    /// <summary>Children wrapped onto new lines, as an Avalonia <see cref="Avalonia.Controls.WrapPanel"/> (Avalonia's name).</summary>
    public static WrapPanelElement WrapPanel(params Element?[] children) => new(FilterChildren(children));
}
