using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The auto-suggest box (#29): upstream's AutoSuggestBoxElement, as an Avalonia AutoCompleteBox. The record mirrors
// upstream's (Reactor's member names, Avalonia's value types); members with no Avalonia counterpart (Header, QueryIcon)
// aren't on it. FilterMode is Avalonia's member, and defaults to none: as on WinUI, the app's suggestions are shown as
// given, so a function of the typed text decides them.
//
// What the AutoCompleteBox does, measured in a headless window (#29): a Text write raises TextChanged inside the write
// when the box is templated, and none outside a window; attaching a box with text to a window raises one TextChanged
// with the text it already has; arrow keys in the open list set SelectedItem and the text (TextChanged, then
// SelectionChanged); typing a suggestion's exact text selects it too; a Return reaches a handled-too KeyDown handler
// (handled when the list was open); suggestions set after a keystroke don't open the list by themselves.

/// <summary>
/// A text box that suggests completions, as an Avalonia <see cref="Avalonia.Controls.AutoCompleteBox"/> (upstream's name:
/// AutoSuggestBox). <see cref="Text"/> defaults to <see cref="Optional{T}.Unset"/>, so the text the user types is kept
/// unless a value is given.
/// </summary>
/// <remarks>
/// As upstream's, <see cref="OnTextChanged"/> reports what the user types; a suggestion the user picks (arrow keys or a
/// click) reaches <see cref="OnSuggestionChosen"/> instead, and <see cref="OnQuerySubmitted"/> runs on Enter with the
/// box's text. Unlike WinUI's, Avalonia selects a suggestion the user types out exactly, so that reaches
/// <see cref="OnSuggestionChosen"/> too, and a click on a suggestion doesn't submit the query.
/// </remarks>
[GenerateReactorDescriptor(typeof(AutoCompleteBox), ClearValueOnUnset = true, Exclude = new[] { "TextChanged", "SelectionChanged" })]
[WrapManual("Text")]
[WrapManual("Suggestions")]
[WrapManual("FilterMode")]
[WrapAlias("IsSuggestionListOpen", "IsDropDownOpen")]
public partial record AutoSuggestBoxElement(
    Optional<string> Text = default,
    Action<string>? OnTextChanged = null,
    Action<string>? OnQuerySubmitted = null,
    Action<string>? OnSuggestionChosen = null) : Element
{
    /// <summary>The suggestions the list shows, in order (upstream's member; Avalonia's <c>ItemsSource</c>).</summary>
    public string[] Suggestions { get; init; } = [];

    public string? PlaceholderText { get; init; }

    /// <summary>Opens or closes the suggestion list (Avalonia's <c>IsDropDownOpen</c>). Defaults to false.</summary>
    public bool IsSuggestionListOpen { get; init; }

    /// <summary>
    /// How Avalonia filters <see cref="Suggestions"/> by the typed text (Avalonia's member). Defaults to
    /// <see cref="AutoCompleteFilterMode.None"/>: every suggestion is shown, as on WinUI.
    /// </summary>
    public AutoCompleteFilterMode FilterMode { get; init; } = AutoCompleteFilterMode.None;

    internal Action<AutoCompleteBox>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnTextChanged is not null || OnQuerySubmitted is not null || OnSuggestionChosen is not null;

    // Dropped: the controlled text's echo (the value-diff arm, as upstream's), a change that leaves the text as it was
    // (the echo Avalonia raises when it fills a template's text box, which the mount write's lack of an arm would let
    // through), and the selection's text, which OnSuggestionChosen reports (upstream reports only the user's typing). A
    // text that equals the selected suggestion's is the selection's: a keystroke always changes the text, and the box
    // selects a suggestion only after the text it matches is in place.
    private static readonly EventHandler<TextChangedEventArgs> __TextChangedTrampoline = (s, _) =>
    {
        var box = (AutoCompleteBox)s!;
        if (!Reconciler.TryGetReactorState(box, out var state)) return;
        var text = box.Text ?? string.Empty;
        var payload = Reconciler.GetOrCreateControlEventPayload<AutoSuggestBoxEventPayload>(box);
        var changed = !string.Equals(payload.LastText, text, StringComparison.Ordinal);
        payload.LastText = text;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(state, text)) return;
        if (!changed) return;
        if (box.SelectedItem is { } selected && string.Equals(selected.ToString(), text, StringComparison.Ordinal)) return;
        (state.Element as AutoSuggestBoxElement)?.OnTextChanged?.Invoke(text);
    };

    // A selection the engine's own write made (a written text that matches a suggestion) isn't the user's.
    private static readonly EventHandler<SelectionChangedEventArgs> __SelectionChangedTrampoline = (s, args) =>
    {
        var box = (AutoCompleteBox)s!;
        if (!ReferenceEquals(args.Source, box) || box.SelectedItem is not { } selected) return;
        if (Reconciler.GetOrCreateControlEventPayload<AutoSuggestBoxEventPayload>(box).EngineWriteDepth > 0) return;
        (Reconciler.GetElementTag(box) as AutoSuggestBoxElement)?.OnSuggestionChosen?.Invoke(selected.ToString() ?? string.Empty);
    };

    private static readonly EventHandler<KeyEventArgs> __KeyDownTrampoline = (s, args) =>
    {
        if (args.Key != Avalonia.Input.Key.Enter) return;
        var box = (AutoCompleteBox)s!;
        (Reconciler.GetElementTag(box) as AutoSuggestBoxElement)?.OnQuerySubmitted?.Invoke(box.Text ?? string.Empty);
    };

    // As upstream's: the suggestions are written before the text, so a first text lands with its list in place; the text
    // is controlled on the value-diff channel (an echo comes inside the write in a window and not at all outside one, so
    // a counter token could be left for the user's next keystroke). FilterMode comes first of all, so the first
    // suggestions are filtered as declared. Suggestions compare by content: a list rebuilt each render with the same
    // strings isn't rewritten, which would reset the open list. New suggestions while the user types re-populate the
    // list, as WinUI's does when its items change (Avalonia's doesn't).
    private static partial ControlDescriptor<AutoSuggestBoxElement, AutoCompleteBox> Customize(ControlDescriptor<AutoSuggestBoxElement, AutoCompleteBox> d)
        => d.OneWay(
                get: static e => e.FilterMode,
                set: static (c, v) => c.FilterMode = v)
            .OneWay(
                get: static e => e.Suggestions,
                set: static (c, v) => EngineWrite(c, () =>
                {
                    c.ItemsSource = v.Length > 0 ? v : null;
                    if (c.IsKeyboardFocusWithin) c.PopulateComplete();
                }),
                comparer: SameStrings.Instance)
            .HandCodedControlled<AutoSuggestBoxEventPayload, string, EventHandler<TextChangedEventArgs>>(
                get:         static e => e.Text,
                set:         static (c, v) => EngineWrite(c, () => c.Text = v),
                readBack:    static c => c.Text ?? string.Empty,
                subscribe:   static (c, h) =>
                {
                    Reconciler.GetOrCreateControlEventPayload<AutoSuggestBoxEventPayload>(c).LastText = c.Text ?? string.Empty;
                    c.TextChanged += h;
                },
                callback:    static e => e.OnTextChanged,
                trampoline:  __TextChangedTrampoline,
                slotIsNull:  static p => p.TextChangedTrampoline is null,
                setSlot:     static (p, h) => p.TextChangedTrampoline = h,
                valueDiffEcho: true)
            .HandCodedEvent<AutoSuggestBoxEventPayload, EventHandler<SelectionChangedEventArgs>>(
                subscribe:       static (c, h) => c.SelectionChanged += h,
                callbackPresent: static e => e.OnSuggestionChosen,
                trampoline:      __SelectionChangedTrampoline,
                slotIsNull:      static p => p.SelectionChangedTrampoline is null,
                setSlot:         static (p, h) => p.SelectionChangedTrampoline = h)
            .HandCodedEvent<AutoSuggestBoxEventPayload, EventHandler<KeyEventArgs>>(
                subscribe:       static (c, h) => c.AddHandler(InputElement.KeyDownEvent, h, RoutingStrategies.Bubble, handledEventsToo: true),
                callbackPresent: static e => e.OnQuerySubmitted,
                trampoline:      __KeyDownTrampoline,
                slotIsNull:      static p => p.KeyDownTrampoline is null,
                setSlot:         static (p, h) => p.KeyDownTrampoline = h);

    private static void EngineWrite(AutoCompleteBox box, Action write)
    {
        var payload = Reconciler.GetOrCreateControlEventPayload<AutoSuggestBoxEventPayload>(box);
        payload.EngineWriteDepth++;
        try { write(); }
        finally { payload.EngineWriteDepth--; }
    }

    private sealed class SameStrings : IEqualityComparer<string[]>
    {
        internal static readonly SameStrings Instance = new();
        public bool Equals(string[]? a, string[]? b) => ReferenceEquals(a, b) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));
        public int GetHashCode(string[] v) => v.Length;
    }
}
