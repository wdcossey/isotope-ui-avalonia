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
