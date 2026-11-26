using Isotope.UI.Core;

namespace Isotope.UI;

// The list box factories for element and typed items (#94), as part of our own version (tier 3) of upstream's
// src/Reactor/Elements/Dsl.cs: Reactor's ListView is Avalonia's ListBox (tools/naming/renames.json), with upstream's
// overloads and parameter order. The string list, ListBox(string[], ...), is in Factories.Controls.cs.

public static partial class Factories
{
    /// <summary>
    /// A list of elements to select from, as an Avalonia <see cref="Avalonia.Controls.ListBox"/> (Reactor's ListView):
    /// <c>ListBox(TextBlock("Apples"), TextBlock("Pears"))</c>. Each item is reconciled as a panel's child is: give the items
    /// keys (<c>.WithKey(id)</c>) when they move. A null item is left out.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static ListBoxElement ListBox(params Element?[] items) => new(Items(items));

    /// <summary>
    /// A list of elements with a controlled selection: <paramref name="selectedIndex"/> is written when it changes (-1
    /// selects nothing), and <paramref name="onSelectedIndexChanged"/> is called with each index the user selects.
    /// </summary>
    public static ListBoxElement ListBox(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element?[] items)
        => new(Items(items)) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };

    /// <summary>A list of elements whose selection is controlled only when <paramref name="selectedIndex"/> has a value.</summary>
    public static ListBoxElement ListBox(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element?[] items)
        => new(Items(items))
        {
            SelectedIndex = selectedIndex is { } index ? index : Optional<int>.Unset,
            OnSelectedIndexChanged = onSelectedIndexChanged,
        };

    /// <summary>A list of elements whose selection is controlled only when <paramref name="selectedIndex"/> is set.</summary>
    public static ListBoxElement ListBox(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element?[] items)
        => new(Items(items)) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };

    private static Element[] Items(Element?[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return FilterChildren(items);
    }

    /// <summary>
    /// A list of data items, as an Avalonia <see cref="Avalonia.Controls.ListBox"/> (Reactor's typed ListView&lt;T&gt;):
    /// <paramref name="itemTemplate"/> builds each item's element from the item and its index, keyed by
    /// <paramref name="keySelector"/>, so an item that moves keeps its control and a component's state.
    /// <c>ListBox(people, p =&gt; p.Id, (p, i) =&gt; TextBlock(p.Name))</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ListBoxElement<T> ListBox<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> itemTemplate)
        => new(items, keySelector, itemTemplate);

    /// <summary>
    /// A list of data items that carry their own key (<see cref="IReactorKeyed"/>): <paramref name="itemTemplate"/>
    /// builds each item's element, keyed by the item's <see cref="IReactorKeyed.Key"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ListBoxElement<T> ListBox<T>(IReadOnlyList<T> items, Func<T, int, Element> itemTemplate) where T : IReactorKeyed
        => new(items, static t => t.Key, itemTemplate);
}
