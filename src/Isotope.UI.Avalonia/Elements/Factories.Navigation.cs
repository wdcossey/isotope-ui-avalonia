using Isotope.UI.Core;

namespace Isotope.UI;

// The factories for tabs and menus (#26), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's names and parameter order, returning the same records, with Avalonia's member names as parameter names
// (#31) and Avalonia's value types.

public static partial class Factories
{
    /// <summary>
    /// Tabs, as an Avalonia <see cref="Avalonia.Controls.TabControl"/> (upstream's name: TabView); the control owns its
    /// selection. Build each tab with <see cref="Tab"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static TabViewElement TabView(params TabViewItemData[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items);
    }

    /// <summary>
    /// Tabs with a controlled selected index. Any <see cref="int"/>, including <c>default</c> (0), selects that index;
    /// pass <c>(int?)null</c> or <see cref="Optional{T}.Unset"/> to leave the selection to the control.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static TabViewElement TabView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] items)
        => TabView(Optional<int>.Of(selectedIndex), onSelectedIndexChanged, items);

    /// <summary>Tabs whose selected index is left to the control when <paramref name="selectedIndex"/> is null.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static TabViewElement TabView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] items)
        => TabView(selectedIndex is { } i ? Optional<int>.Of(i) : Optional<int>.Unset, onSelectedIndexChanged, items);

    /// <summary>Tabs whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static TabViewElement TabView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>One tab: its header text and its content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static TabViewItemData Tab(string header, Element content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(header, content);
    }

    // ── Split view ──────────────────────────────────────────────────

    /// <summary>
    /// A pane beside (or over) the content, as an Avalonia <see cref="Avalonia.Controls.SplitView"/>. The pane starts
    /// open, as upstream's; set <c>IsPaneOpen</c> and <c>OnIsPaneOpenChanged</c> to drive it from state.
    /// </summary>
    public static SplitViewElement SplitView(Element? pane = null, Element? content = null) => new(pane, content);
    // ── Menus ───────────────────────────────────────────────────────

    /// <summary>A menu bar, as an Avalonia <see cref="Avalonia.Controls.Menu"/>: <c>MenuBar(Menu("File", MenuItem("Open", open)))</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuBarElement MenuBar(params MenuBarItemData[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items);
    }

    /// <summary>A top-level menu of a <see cref="MenuBar"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuBarItemData Menu(string header, params MenuFlyoutItemBase[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(header, items);
    }

    /// <summary>A menu item that runs <paramref name="onClick"/> when chosen.</summary>
    public static MenuFlyoutItemData MenuItem(string header, Action? onClick = null) => new(header, onClick);

    /// <summary>A menu item with a check mark; choosing it toggles it and calls <paramref name="onIsCheckedChanged"/>.</summary>
    public static ToggleMenuFlyoutItemData ToggleMenuItem(string header, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
        => new(header, isChecked, onIsCheckedChanged);

    /// <summary>A menu item that is one of the group <paramref name="groupName"/>; choosing it checks it and unchecks the others.</summary>
    public static RadioMenuFlyoutItemData RadioMenuItem(string header, string groupName, bool isChecked = false, Action? onClick = null)
        => new(header, groupName, isChecked, onClick);

    /// <summary>A line between menu items.</summary>
    public static MenuFlyoutSeparatorData MenuSeparator() => new();

    /// <summary>A submenu.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuFlyoutSubItemData MenuSubItem(string header, params MenuFlyoutItemBase[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(header, items);
    }

    /// <summary>The items of a context menu, for <c>.WithContextFlyout(...)</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuFlyoutContentElement MenuItems(params MenuFlyoutItemBase[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items);
    }

    /// <summary>The items of a context menu that opens on the given side of its control.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static MenuFlyoutContentElement MenuItems(Avalonia.Controls.PlacementMode placement, params MenuFlyoutItemBase[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items) { Placement = placement };
    }
}
