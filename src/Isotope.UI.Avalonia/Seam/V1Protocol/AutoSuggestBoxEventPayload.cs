namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// The per-control event payload of an <see cref="Avalonia.Controls.AutoCompleteBox"/> (#29): its text, selection and
/// Enter-key trampolines, in one payload because a control holds one, and the state they share.
/// </summary>
internal sealed class AutoSuggestBoxEventPayload
{
    public EventHandler<Avalonia.Controls.TextChangedEventArgs>? TextChangedTrampoline;
    public EventHandler<Avalonia.Controls.SelectionChangedEventArgs>? SelectionChangedTrampoline;
    public EventHandler<Avalonia.Input.KeyEventArgs>? KeyDownTrampoline;

    /// <summary>The text the box had at the last change event (or when it was subscribed): an event that leaves it as it was is no change.</summary>
    public string? LastText;

    /// <summary>The text a pick put in the box, until the selection change that follows it reports the pick.</summary>
    public string? PickedText;
}
