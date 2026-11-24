using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvMenu = Avalonia.Controls.Menu;
using AvMenuItem = Avalonia.Controls.MenuItem;
using AvToolTip = Avalonia.Controls.ToolTip;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Menus, context menus and tooltips (#26): a menu bar as an Avalonia Menu and a context menu as a MenuFlyout, their
/// items built from data and updated in place, clicks reaching the current render's callbacks, toggle and radio items;
/// the tooltip modifier.
/// </summary>
public sealed class MenuAndToolTipTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static List<object> Items(ItemsControl c) => c.Items.Cast<object>().ToList();

    private static AvMenuItem Item(ItemsControl c, int index) => (AvMenuItem)c.Items[index]!;

    private void Show(TestHost host)
    {
        if (TopLevel.GetTopLevel(host.Root) is not null) return;
        var window = new Window { Width = 400, Height = 300, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    // A real headless click, through Avalonia's menu interaction handler (which toggles a check-box item before it
    // raises Click): the item's parent menus are opened first, or its context flyout is shown on its control.
    private void Click(TestHost host, AvMenuItem item, Control? flyoutTarget = null)
    {
        Show(host);
        if (flyoutTarget?.ContextFlyout is { } flyout)
        {
            flyout.ShowAt(flyoutTarget);
            Dispatcher.UIThread.RunJobs();
        }
        var parents = new List<AvMenuItem>();
        for (var p = item.Parent; p is AvMenuItem parent; p = parent.Parent) parents.Insert(0, parent);
        foreach (var parent in parents)
        {
            parent.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
        }
        var top = TopLevel.GetTopLevel(item) ?? throw new InvalidOperationException("the item isn't shown");
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("the item isn't shown");
        top.MouseDown(centre, MouseButton.Left);
        top.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MenuBar_mounts_its_menus_and_items()
    {
        var host = new TestHost(() => MenuBar(
            Menu("File", MenuItem("Open"), MenuSeparator(), MenuSubItem("Recent", MenuItem("a.txt"), MenuItem("b.txt"))),
            Menu("Edit", MenuItem("Undo") with { IsEnabled = false })));
        var menu = host.All<AvMenu>()[0];
        Assert.Equal(["File", "Edit"], Items(menu).Cast<AvMenuItem>().Select(i => i.Header));
        var file = Item(menu, 0);
        Assert.Equal("Open", Item(file, 0).Header);
        Assert.IsType<Separator>(file.Items[1]);
        var recent = Item(file, 2);
        Assert.Equal(["a.txt", "b.txt"], Items(recent).Cast<AvMenuItem>().Select(i => i.Header));
        Assert.False(Item(Item(menu, 1), 0).IsEnabled);
    }

    [AvaloniaFact]
    public void MenuBar_updates_its_items_in_place_and_a_click_calls_the_current_callback()
    {
        var log = new List<string>();
        var n = 0;
        var host = new TestHost(() =>
        {
            var current = n;
            return MenuBar(Menu("File", MenuItem("Open " + current, () => log.Add("open " + current))));
        });
        var menu = host.All<AvMenu>()[0];
        var open = Item(Item(menu, 0), 0);

        n = 1;
        host.Render();
        Assert.Same(menu, host.All<AvMenu>()[0]);
        Assert.Same(open, Item(Item(menu, 0), 0));
        Assert.Equal("Open 1", open.Header);

        Click(host, open);
        Assert.Equal(["open 1"], log);
    }

    [AvaloniaFact]
    public void MenuBar_replaces_an_item_whose_kind_changes_and_drops_removed_items()
    {
        var long_ = true;
        var host = new TestHost(() => long_
            ? MenuBar(Menu("File", MenuItem("Open"), MenuItem("Save"), MenuItem("Close")))
            : MenuBar(Menu("File", MenuItem("Open"), MenuSeparator())));
        var file = Item(host.All<AvMenu>()[0], 0);
        var open = Item(file, 0);

        long_ = false;
        host.Render();
        Assert.Equal(2, file.Items.Count);
        Assert.Same(open, file.Items[0]);
        Assert.IsType<Separator>(file.Items[1]);
    }

    [AvaloniaFact]
    public void ToggleMenuItem_click_toggles_and_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (wrap, setWrap) = ctx.UseState(false);
            set = setWrap;
            return VStack(TextBlock("wrap " + wrap),
                MenuBar(Menu("View", ToggleMenuItem("Word wrap", wrap, v => { reported.Add(v); setWrap(v); }))));
        }));
        var item = Item(Item(host.All<AvMenu>()[0], 0), 0);
        Assert.Equal((MenuItemToggleType.CheckBox, false), (item.ToggleType, item.IsChecked));

        Click(host, item);
        host.Flush();
        Assert.Equal([true], reported);
        Assert.Equal(["wrap True"], host.Texts());
        Assert.True(item.IsChecked);

        set!(false); // from the state: written, not reported
        host.Flush();
        Assert.False(item.IsChecked);
        Assert.Equal([true], reported);
    }

    [AvaloniaFact]
    public void ToggleMenuItem_inserted_before_another_gets_its_own_check_state()
    {
        // Items are paired by position: the item that takes another's place has its own value written.
        var reported = new List<bool>();
        var both = false;
        var host = new TestHost(() => MenuBar(Menu("Format", both
            ? [ToggleMenuItem("Bold", false), ToggleMenuItem("Wrap", false, v => reported.Add(v))]
            : [ToggleMenuItem("Wrap", false, v => reported.Add(v))])));
        var format = Item(host.All<AvMenu>()[0], 0);
        Click(host, Item(format, 0)); // the user ticks Wrap; the app keeps its state as it was
        Assert.Equal([true], reported);
        format.IsSubMenuOpen = false;

        both = true;
        host.Render();
        Assert.Equal(["Bold", "Wrap"], Items(format).Cast<AvMenuItem>().Select(i => i.Header));
        Assert.False(Item(format, 0).IsChecked);
    }

    [AvaloniaFact]
    public void Menu_factories_reject_null_items()
    {
        Assert.Throws<ArgumentNullException>(() => Menu("File", null!));
        Assert.Throws<ArgumentNullException>(() => MenuSubItem("Recent", null!));
        Assert.Throws<ArgumentNullException>(() => MenuItems(null!));
        Assert.Throws<ArgumentNullException>(() => MenuItems(PlacementMode.Bottom, null!));
        Assert.Throws<ArgumentNullException>(() => Tab("A", null!));
    }

    [AvaloniaFact]
    public void RadioMenuItem_click_checks_it_unchecks_its_group_and_calls_back()
    {
        var log = new List<string>();
        var host = new TestHost(() => MenuBar(Menu("Size",
            RadioMenuItem("Small", "size", true, () => log.Add("small")),
            RadioMenuItem("Large", "size", false, () => log.Add("large")))));
        var size = Item(host.All<AvMenu>()[0], 0);
        var (small, large) = (Item(size, 0), Item(size, 1));
        Assert.Equal((MenuItemToggleType.Radio, "size"), (large.ToggleType, large.GroupName));
        Assert.True(small.IsChecked);

        Click(host, large);
        Assert.Equal(["large"], log);
        Assert.True(large.IsChecked);
        Assert.False(small.IsChecked);
    }

    [AvaloniaFact]
    public void Context_flyout_is_a_menu_flyout_updated_in_place_and_removed_with_the_modifier()
    {
        var log = new List<string>();
        var n = 0;
        var on = true;
        var host = new TestHost(() =>
        {
            var current = n;
            var text = TextBlock("row");
            return on ? text.WithContextFlyout(MenuItems(MenuItem("Delete " + current, () => log.Add("delete " + current)))) : text;
        });
        var text = host.All<TextBlock>()[0];
        var flyout = Assert.IsType<MenuFlyout>(text.ContextFlyout);
        var delete = (AvMenuItem)flyout.Items[0]!;
        Assert.Equal("Delete 0", delete.Header);

        n = 1;
        host.Render();
        Assert.Same(flyout, text.ContextFlyout);
        Assert.Same(delete, flyout.Items[0]);
        Click(host, delete, text);
        Assert.Equal(["delete 1"], log);

        on = false;
        host.Render();
        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Null(text.ContextFlyout);
    }

    [AvaloniaFact]
    public void Context_flyout_toggle_item_click_reports_the_new_value()
    {
        var reported = new List<bool>();
        var host = new TestHost(() => TextBlock("row").WithContextFlyout(MenuItems(ToggleMenuItem("Pinned", true, v => reported.Add(v)))));
        var text = host.All<TextBlock>()[0];
        var pinned = (AvMenuItem)((MenuFlyout)text.ContextFlyout!).Items[0]!;

        Click(host, pinned, text);
        Assert.Equal([false], reported);
        Assert.False(pinned.IsChecked);
    }

    [AvaloniaFact]
    public void Context_flyout_placement_is_written_and_cleared()
    {
        PlacementMode? placement = PlacementMode.Right;
        var host = new TestHost(() => TextBlock("row").WithContextFlyout(
            placement is { } p ? MenuItems(p, MenuItem("Copy")) : MenuItems(MenuItem("Copy"))));
        var flyout = Assert.IsType<MenuFlyout>(host.All<TextBlock>()[0].ContextFlyout);
        Assert.Equal(PlacementMode.Right, flyout.Placement);

        placement = null;
        host.Render();
        Assert.Equal(new MenuFlyout().Placement, flyout.Placement);
    }

    [AvaloniaFact]
    public void Context_flyout_wins_over_a_setter_and_leaves_a_flyout_it_did_not_create()
    {
        // As the other modifiers, it is written after the setters; it never fills or removes a flyout it didn't create.
        var own = new MenuFlyout();
        var on = true;
        var host = new TestHost(() =>
        {
            var b = TextBlock("row") with { Setters = [t => t.ContextFlyout = own] };
            return on ? b.WithContextFlyout(MenuItems(MenuItem("Copy"))) : b;
        });
        var text = host.All<TextBlock>()[0];
        var flyout = Assert.IsType<MenuFlyout>(text.ContextFlyout);
        Assert.NotSame(own, flyout);
        Assert.Single(flyout.Items);
        Assert.Empty(own.Items);

        host.Render(); // the setter puts its flyout back each render; the modifier's own one is reused
        Assert.Same(flyout, text.ContextFlyout);

        on = false;
        host.Render();
        Assert.Same(own, text.ContextFlyout);
        Assert.Empty(own.Items);
    }

    [AvaloniaFact]
    public void ToolTip_modifier_writes_the_tip_and_placement_and_clears_them()
    {
        var on = true;
        var host = new TestHost(() => on ? Button("Save").ToolTip("Save (Ctrl+S)", PlacementMode.Bottom) : Button("Save"));
        var button = host.All<Button>()[0];
        Assert.Equal("Save (Ctrl+S)", AvToolTip.GetTip(button));
        Assert.Equal(PlacementMode.Bottom, AvToolTip.GetPlacement(button));

        on = false;
        host.Render();
        Assert.Same(button, host.All<Button>()[0]);
        Assert.Null(AvToolTip.GetTip(button));
        Assert.False(button.IsSet(AvToolTip.PlacementProperty));
    }

    [AvaloniaFact]
    public void ToolTip_text_changes_in_place()
    {
        var tip = "one";
        var host = new TestHost(() => TextBlock("x").ToolTip(tip));
        var text = host.All<TextBlock>()[0];
        tip = "two";
        host.Render();
        Assert.Equal("two", AvToolTip.GetTip(text));
    }
}
