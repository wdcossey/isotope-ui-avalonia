using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Menus (#26): upstream's menu data records (MenuFlyoutItemBase and its kinds), a menu bar as an Avalonia Menu, and a
// context menu as an Avalonia MenuFlyout on a control's ContextFlyout (the WithContextFlyout modifier). The records
// mirror upstream's (Reactor's member names); members with no Avalonia counterpart (icons, keyboard accelerators,
// access keys, descriptions) aren't on them yet. Menu items hold no elements, so they are Avalonia MenuItems built from
// the data and updated in place by position (MenuItemsSync); a click reads the item's current data, so it calls the
// current render's callback.

/// <summary>An item of a menu: <see cref="MenuFlyoutItemData"/>, a toggle, a radio item, a separator or a submenu.</summary>
public abstract record MenuFlyoutItemBase;

/// <summary>A menu item that runs <see cref="OnClick"/> when chosen.</summary>
public record MenuFlyoutItemData(string Text, Action? OnClick = null) : MenuFlyoutItemBase
{
    public bool IsEnabled { get; init; } = true;
}

/// <summary>A line between menu items.</summary>
public record MenuFlyoutSeparatorData() : MenuFlyoutItemBase;

/// <summary>A submenu: <see cref="Text"/>, opening <see cref="Items"/>.</summary>
public record MenuFlyoutSubItemData(string Text, MenuFlyoutItemBase[] Items) : MenuFlyoutItemBase;

/// <summary>
/// A menu item with a check mark. <see cref="IsChecked"/> is written when it changes; choosing the item toggles it and
/// calls <see cref="OnIsCheckedChanged"/> with the new value.
/// </summary>
public record ToggleMenuFlyoutItemData(string Text, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null) : MenuFlyoutItemBase;

/// <summary>
/// A menu item that is one of a group (<see cref="GroupName"/>): choosing it checks it and unchecks the others, and
/// calls <see cref="OnClick"/>.
/// </summary>
public record RadioMenuFlyoutItemData(string Text, string GroupName, bool IsChecked = false, Action? OnClick = null) : MenuFlyoutItemBase;

/// <summary>A top-level menu of a <see cref="MenuBarElement"/>: its title and its items.</summary>
public record MenuBarItemData(string Title, MenuFlyoutItemBase[] Items);

/// <summary>
/// The menu a <c>.WithContextFlyout(...)</c> modifier opens, as an Avalonia <see cref="Avalonia.Controls.MenuFlyout"/>.
/// Not mounted on its own: it describes a flyout.
/// </summary>
public record MenuFlyoutContentElement(MenuFlyoutItemBase[] Items) : Element
{
    /// <summary>Where the flyout opens relative to its control; null keeps Avalonia's default.</summary>
    public Avalonia.Controls.PlacementMode? Placement { get; init; }
}

/// <summary>A menu bar, as an Avalonia <see cref="Avalonia.Controls.Menu"/>: one top-level menu per item.</summary>
[GenerateReactorDescriptor(typeof(Menu))]
[WrapManual("Items")]
public partial record MenuBarElement(MenuBarItemData[] Items) : Element
{
    internal Action<Menu>[] Setters { get; init; } = [];

    // The items are written on every update, by position; they hold no elements, so there is nothing to tear down.
    private static partial ControlDescriptor<MenuBarElement, Menu> Customize(ControlDescriptor<MenuBarElement, Menu> d)
    {
        d.Children = new Imperative<MenuBarElement, Menu>(static (ctx, oldElement, newElement, control) =>
            MenuItemsSync.Sync(ItemsList.Of(control.Items), MenuItemsSync.TopLevel(newElement.Items)));
        return d;
    }
}

/// <summary>Builds Avalonia menu items from menu data and keeps them in step with it, by position.</summary>
internal static class MenuItemsSync
{
    private sealed class DataOwner;

    // The data a MenuItem was last written with; its click handler reads it.
    private static readonly AttachedProperty<MenuFlyoutItemBase?> DataProperty =
        AvaloniaProperty.RegisterAttached<DataOwner, MenuItem, MenuFlyoutItemBase?>("IsotopeMenuData");

    // The items a MenuItem is one of, so a radio item can uncheck the others of its group.
    private static readonly AttachedProperty<IList<object>?> SiblingsProperty =
        AvaloniaProperty.RegisterAttached<DataOwner, MenuItem, IList<object>?>("IsotopeMenuSiblings");

    internal static MenuFlyoutItemBase[] TopLevel(MenuBarItemData[] items)
    {
        var result = new MenuFlyoutItemBase[items.Length];
        for (int i = 0; i < items.Length; i++) result[i] = new MenuFlyoutSubItemData(items[i].Title, items[i].Items);
        return result;
    }

    /// <summary>Makes <paramref name="items"/> show <paramref name="data"/>: each item updated in place when its kind matches.</summary>
    internal static void Sync(IList<object> items, MenuFlyoutItemBase[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            var existing = i < items.Count ? items[i] : null;
            if (!TryUpdate(existing, data[i]))
            {
                var created = Create(data[i]);
                if (existing is null) items.Add(created);
                else
                {
                    items.RemoveAt(i);
                    items.Insert(i, created);
                }
            }
            if (items[i] is MenuItem item) item.SetValue(SiblingsProperty, items);
        }
        for (int i = items.Count - 1; i >= data.Length; i--) items.RemoveAt(i);
    }

    private static object Create(MenuFlyoutItemBase data)
    {
        if (data is MenuFlyoutSeparatorData) return new Separator();
        var item = new MenuItem();
        item.Click += OnClick;
        Write(item, data);
        return item;
    }

    private static bool TryUpdate(object? existing, MenuFlyoutItemBase data)
    {
        if (data is MenuFlyoutSeparatorData) return existing is Separator;
        if (existing is not MenuItem item || item.GetValue(DataProperty) is not { } old || old.GetType() != data.GetType())
            return false;
        Write(item, data);
        return true;
    }

    private static void Write(MenuItem item, MenuFlyoutItemBase data)
    {
        var old = item.GetValue(DataProperty);
        item.SetValue(DataProperty, data);
        switch (data)
        {
            case MenuFlyoutItemData d:
                SetHeader(item, d.Text);
                if (item.IsEnabled != d.IsEnabled) item.IsEnabled = d.IsEnabled;
                break;
            case ToggleMenuFlyoutItemData t:
                SetHeader(item, t.Text);
                item.ToggleType = MenuItemToggleType.CheckBox;
                // As upstream's: written when the declared value changes, so the control keeps a toggle until then.
                if (old is not ToggleMenuFlyoutItemData ot || ot.IsChecked != t.IsChecked) item.IsChecked = t.IsChecked;
                break;
            case RadioMenuFlyoutItemData r:
                SetHeader(item, r.Text);
                item.ToggleType = MenuItemToggleType.Radio;
                if (item.GroupName != r.GroupName) item.GroupName = r.GroupName;
                if (old is not RadioMenuFlyoutItemData or_ || or_.IsChecked != r.IsChecked) item.IsChecked = r.IsChecked;
                break;
            case MenuFlyoutSubItemData s:
                SetHeader(item, s.Text);
                Sync(ItemsList.Of(item.Items), s.Items);
                break;
        }
    }

    private static void SetHeader(MenuItem item, string text)
    {
        if (!Equals(item.Header, text)) item.Header = text;
    }

    // Click bubbles: a submenu's item raises it on its parent items too, so each item handles only its own.
    private static void OnClick(object? sender, RoutedEventArgs args)
    {
        var item = (MenuItem)sender!;
        if (!ReferenceEquals(args.Source, item)) return;
        switch (item.GetValue(DataProperty))
        {
            case MenuFlyoutItemData d:
                d.OnClick?.Invoke();
                break;
            case ToggleMenuFlyoutItemData t:
                // Avalonia's menu interaction handler toggles a check-box item before it raises Click, as WinUI's does.
                t.OnIsCheckedChanged?.Invoke(item.IsChecked);
                break;
            case RadioMenuFlyoutItemData r:
                // Its handler checks a radio item too; as WinUI's, the others of its group among its siblings are
                // unchecked here, which doesn't depend on the item being in a window's group scope.
                item.IsChecked = true;
                if (item.GetValue(SiblingsProperty) is { } siblings)
                    foreach (var other in siblings)
                        if (other is MenuItem o && !ReferenceEquals(o, item) && o.GetValue(DataProperty) is RadioMenuFlyoutItemData od
                            && od.GroupName == r.GroupName)
                            o.IsChecked = false;
                r.OnClick?.Invoke();
                break;
        }
    }

    // ── The context flyout modifier ─────────────────────────────────

    private sealed class FlyoutOwner;

    // Marks a MenuFlyout this modifier created, so one set by a setter or another source is never taken over.
    private static readonly AttachedProperty<bool> OwnedFlyoutProperty =
        AvaloniaProperty.RegisterAttached<FlyoutOwner, MenuFlyout, bool>("IsotopeOwnedFlyout");

    // The flyout this modifier created for a control, reused when a setter has put another one in its place.
    private static readonly AttachedProperty<MenuFlyout?> ControlFlyoutProperty =
        AvaloniaProperty.RegisterAttached<FlyoutOwner, Control, MenuFlyout?>("IsotopeContextFlyout");

    /// <summary>
    /// Writes the <c>.WithContextFlyout(...)</c> modifier: a MenuFlyout created once and updated in place, removed when
    /// the modifier goes.
    /// </summary>
    internal static void ApplyContextFlyout(Control control, MenuFlyoutContentElement? oldMenu, MenuFlyoutContentElement? newMenu)
    {
        if (newMenu is null)
        {
            if (oldMenu is null) return;
            if (control.ContextFlyout is MenuFlyout gone && gone.GetValue(OwnedFlyoutProperty))
                control.ClearValue(Control.ContextFlyoutProperty);
            control.ClearValue(ControlFlyoutProperty);
            return;
        }
        if (ReferenceEquals(oldMenu, newMenu) && control.ContextFlyout is MenuFlyout same && same.GetValue(OwnedFlyoutProperty))
            return;
        if (control.GetValue(ControlFlyoutProperty) is not { } flyout)
        {
            flyout = new MenuFlyout();
            flyout.SetValue(OwnedFlyoutProperty, true);
            control.SetValue(ControlFlyoutProperty, flyout);
        }
        // As the other modifiers, it is written after the setters, so it wins over a flyout a setter assigned.
        if (!ReferenceEquals(control.ContextFlyout, flyout)) control.ContextFlyout = flyout;
        if (newMenu.Placement is { } placement) flyout.Placement = placement;
        else flyout.ClearValue(Avalonia.Controls.Primitives.PopupFlyoutBase.PlacementProperty);
        Sync(ItemsList.Of(flyout.Items), newMenu.Items);
    }
}
