namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// The per-control event payload of a <see cref="Avalonia.Controls.TextBox"/> (upstream's
/// <c>Core/V1Protocol/ControlEventPayloads.cs</c> is tier 3: its payloads hold WinUI delegate types). The slot holds
/// the trampoline subscribed to <c>TextChanged</c> for the control's lifetime.
/// </summary>
internal sealed class TextBoxEventPayload
{
    public EventHandler<Avalonia.Controls.TextChangedEventArgs>? TextChangedTrampoline;
}

/// <summary>
/// The per-control event payload of a <see cref="Avalonia.Controls.ListBox"/> (#19): the trampoline subscribed to
/// <c>SelectionChanged</c>, which reports both the selected index and the multiple-selection snapshot.
/// </summary>
internal sealed class ListBoxEventPayload
{
    public EventHandler<Avalonia.Controls.SelectionChangedEventArgs>? SelectionChangedTrampoline;
}

/// <summary>
/// The per-control event payload of a <see cref="Avalonia.Controls.ComboBox"/> (#19): its selection and its two drop-down
/// events, in one payload because a control holds one (<c>Reconciler.GetOrCreateControlEventPayload</c>).
/// </summary>
internal sealed class ComboBoxEventPayload
{
    public EventHandler<Avalonia.Controls.SelectionChangedEventArgs>? SelectionChangedTrampoline;
    public EventHandler? DropDownOpenedTrampoline;
    public EventHandler? DropDownClosedTrampoline;
}

/// <summary>The per-control event payload of a <see cref="Avalonia.Controls.NumericUpDown"/> (#26): its value change.</summary>
internal sealed class NumberBoxEventPayload
{
    public EventHandler<Avalonia.Controls.NumericUpDownValueChangedEventArgs>? ValueChangedTrampoline;
}

/// <summary>The per-control event payload of a <see cref="Avalonia.Controls.SplitView"/> (#26): its pane opening and closing.</summary>
internal sealed class SplitViewEventPayload
{
    public EventHandler<Avalonia.Interactivity.CancelRoutedEventArgs>? PaneOpeningTrampoline;
    public EventHandler<Avalonia.Interactivity.CancelRoutedEventArgs>? PaneClosingTrampoline;
}

/// <summary>The per-control event payload of a <see cref="Avalonia.Controls.TabControl"/> (#26): its selection.</summary>
internal sealed class TabViewEventPayload
{
    public EventHandler<Avalonia.Controls.SelectionChangedEventArgs>? SelectionChangedTrampoline;
}

/// <summary>The per-control event payload of a <see cref="Avalonia.Controls.Primitives.ToggleButton"/> (#26): its click.</summary>
internal sealed class ToggleButtonEventPayload
{
    public EventHandler<Avalonia.Interactivity.RoutedEventArgs>? ClickTrampoline;
}
