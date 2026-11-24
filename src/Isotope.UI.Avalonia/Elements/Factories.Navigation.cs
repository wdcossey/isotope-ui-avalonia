using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for tabs (#26), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's names, parameter names and order, returning the same records, with Avalonia's value types.

public static partial class Factories
{
    /// <summary>
    /// Tabs, as an Avalonia <see cref="Avalonia.Controls.TabControl"/> (upstream's name: TabView); the control owns its
    /// selection. Build each tab with <see cref="Tab"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tabs"/> is null.</exception>
    public static TabViewElement TabView(params TabViewItemData[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return new(tabs);
    }

    /// <summary>
    /// Tabs with a controlled selected index. Any <see cref="int"/>, including <c>default</c> (0), selects that index;
    /// pass <c>(int?)null</c> or <see cref="Optional{T}.Unset"/> to leave the selection to the control.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tabs"/> is null.</exception>
    public static TabViewElement TabView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
        => TabView(Optional<int>.Of(selectedIndex), onSelectedIndexChanged, tabs);

    /// <summary>Tabs whose selected index is left to the control when <paramref name="selectedIndex"/> is null.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tabs"/> is null.</exception>
    public static TabViewElement TabView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
        => TabView(selectedIndex is { } i ? Optional<int>.Of(i) : Optional<int>.Unset, onSelectedIndexChanged, tabs);

    /// <summary>Tabs whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tabs"/> is null.</exception>
    public static TabViewElement TabView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return new(tabs) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>One tab: its header text and its content.</summary>
    public static TabViewItemData Tab(string header, Element content) => new(header, content);
}
