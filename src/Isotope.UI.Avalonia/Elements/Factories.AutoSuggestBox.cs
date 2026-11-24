using Isotope.UI.Core;

namespace Isotope.UI;

// The auto-suggest box's factory (#29), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's name, parameter names and order, returning the same record, with Avalonia's value types.

public static partial class Factories
{
    /// <summary>
    /// A text box that suggests completions, as an Avalonia <see cref="Avalonia.Controls.AutoCompleteBox"/> (upstream's
    /// name: AutoSuggestBox). <paramref name="text"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the
    /// control own its text. Give the suggestions with <c>with { Suggestions = ... }</c>; a picked suggestion reaches
    /// <see cref="AutoSuggestBoxElement.OnSuggestionChosen"/>.
    /// </summary>
    public static AutoSuggestBoxElement AutoSuggestBox(Optional<string> text = default, Action<string>? onTextChanged = null, Action<string>? onQuerySubmitted = null)
        => new(text, onTextChanged, onQuerySubmitted);
}
