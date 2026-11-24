using Isotope.UI.Core;

namespace Isotope.UI;

// The auto-suggest box's modifiers (#29), from upstream's src/Reactor/Elements/ElementExtensions.Events.cs and
// ElementExtensions.cs: upstream's names.

public static partial class ElementExtensions
{
    /// <summary>Wires the text-changed handler (what the user types). Passing <c>null</c> clears.</summary>
    public static AutoSuggestBoxElement TextChanged(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnTextChanged = handler };

    /// <summary>Wires the query-submitted handler (Enter). Passing <c>null</c> clears.</summary>
    public static AutoSuggestBoxElement QuerySubmitted(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnQuerySubmitted = handler };

    /// <summary>Wires the suggestion-chosen handler. Passing <c>null</c> clears.</summary>
    public static AutoSuggestBoxElement SuggestionChosen(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnSuggestionChosen = handler };

    /// <summary>The faint text shown while the box is empty.</summary>
    public static AutoSuggestBoxElement PlaceholderText(this AutoSuggestBoxElement el, string text) =>
        el with { PlaceholderText = text };
}
