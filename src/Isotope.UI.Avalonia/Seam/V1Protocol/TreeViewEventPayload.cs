namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// The per-control event payload of an Avalonia <see cref="Avalonia.Controls.TreeView"/> (#43): its tap, key and
/// expansion trampolines, in one payload because a control holds one.
/// </summary>
internal sealed class TreeViewEventPayload
{
    public EventHandler<Avalonia.Input.TappedEventArgs>? TappedTrampoline;
    public EventHandler<Avalonia.Input.KeyEventArgs>? KeyDownTrampoline;
    public EventHandler<Avalonia.Interactivity.RoutedEventArgs>? ExpandedTrampoline;
}
