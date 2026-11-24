using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for tabs and menus (#26), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
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
    // ── Menus ───────────────────────────────────────────────────────

    /// <summary>A menu bar, as an Avalonia <see cref="Avalonia.Controls.Menu"/>: <c>MenuBar(Menu("File", MenuItem("Open", open)))</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuBarElement MenuBar(params MenuBarItemData[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items);
    }

    /// <summary>A top-level menu of a <see cref="MenuBar"/>.</summary>
    public static MenuBarItemData Menu(string title, params MenuFlyoutItemBase[] items) => new(title, items);

    /// <summary>A menu item that runs <paramref name="onClick"/> when chosen.</summary>
    public static MenuFlyoutItemData MenuItem(string text, Action? onClick = null) => new(text, onClick);

    /// <summary>A menu item with a check mark; choosing it toggles it and calls <paramref name="onIsCheckedChanged"/>.</summary>
    public static ToggleMenuFlyoutItemData ToggleMenuItem(string text, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
        => new(text, isChecked, onIsCheckedChanged);

    /// <summary>A menu item that is one of the group <paramref name="groupName"/>; choosing it checks it and unchecks the others.</summary>
    public static RadioMenuFlyoutItemData RadioMenuItem(string text, string groupName, bool isChecked = false, Action? onClick = null)
        => new(text, groupName, isChecked, onClick);

    /// <summary>A line between menu items.</summary>
    public static MenuFlyoutSeparatorData MenuSeparator() => new();

    /// <summary>A submenu.</summary>
    public static MenuFlyoutSubItemData MenuSubItem(string text, params MenuFlyoutItemBase[] items) => new(text, items);

    /// <summary>The items of a context menu, for <c>.WithContextFlyout(...)</c>.</summary>
    public static MenuFlyoutContentElement MenuItems(params MenuFlyoutItemBase[] items) => new(items);

    /// <summary>The items of a context menu that opens on the given side of its control.</summary>
    public static MenuFlyoutContentElement MenuItems(Avalonia.Controls.PlacementMode placement, params MenuFlyoutItemBase[] items)
        => new(items) { Placement = placement };
}
