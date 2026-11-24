using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Isotope.UI.Core;
using Isotope.UI.Samples;
using Xunit;
using AvMenuItem = Avalonia.Controls.MenuItem;
using AvSplitView = Avalonia.Controls.SplitView;
using AvToolTip = Avalonia.Controls.ToolTip;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The sample app's pages (#30), headless: the whole app in a headless window (no real window), each page selected with a
/// headless click on its tab and its main interaction driven with headless mouse input, then rendered to PNG. What only a
/// real window shows (hover tooltips appearing, how menus and the split view look) is the developer's check, from
/// samples/Isotope.UI.Samples/README.md.
/// </summary>
public sealed class SampleAppTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private (DispatcherHost Host, Window Window) Open()
    {
        var host = new DispatcherHost(() => new FuncElement(SampleApp.Root));
        var window = new Window
        {
            Width = SampleApp.WindowWidth, Height = SampleApp.WindowHeight, SizeToContent = SizeToContent.Manual, Background = Brushes.White, Content = host.Root,
        };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (host, window);
    }

    private static void Click(Control control, MouseButton button = MouseButton.Left)
    {
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("not shown");
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("not shown");
        top.MouseDown(centre, button);
        top.MouseUp(centre, button);
        Dispatcher.UIThread.RunJobs(); // the posted root pass and the layout that follows it
    }

    private static List<T> All<T>(Control root) where T : Control => root.GetSelfAndVisualDescendantsOfType<T>();

    private static List<string> Texts(Control root) => All<TextBlock>(root).Select(t => t.Text ?? "").ToList();

    private static T Labelled<T>(Control root, string label) where T : ContentControl =>
        All<T>(root).FirstOrDefault(c => Equals(c.Content, label)) ?? throw new InvalidOperationException("nothing labelled " + label);

    private static TabControl Tabs(DispatcherHost host) => All<TabControl>(host.Root).Single();

    // Selects a page as the user does: a click on its tab's header.
    private static Control Page(DispatcherHost host, string name)
    {
        var tabs = Tabs(host);
        var tab = tabs.Items.Cast<TabItem>().Single(t => Equals(t.Header, name));
        Click(tab);
        Assert.Same(tab, tabs.SelectedItem);
        return (Control)tab.Content!;
    }

    // Opens a menu bar's top-level menu with a click on its header, then clicks the item.
    private static void ChooseFromMenuBar(DispatcherHost host, string menu, string item)
    {
        var top = All<AvMenuItem>(host.Root).Single(i => Equals(i.Header, menu));
        Click(top);
        Assert.True(top.IsSubMenuOpen);
        Click(top.Items.Cast<AvMenuItem>().Single(i => Equals(i.Header, item)));
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        var path = Path.Combine(AppContext.BaseDirectory, name);
#pragma warning disable CS0618 // Save(string) is obsolete in 12.x but still the plain PNG path (as in Spike D)
        frame.Save(path);
#pragma warning restore CS0618
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [AvaloniaFact]
    public void The_app_shows_a_tab_per_page_and_the_Basics_counter_and_todo_work()
    {
        var (host, window) = Open();
        var tabs = Tabs(host);
        Assert.Equal(SampleApp.Pages.Select(p => p.Name), tabs.Items.Cast<TabItem>().Select(t => (string)t.Header!));
        Assert.Equal(0, tabs.SelectedIndex);

        Click(Labelled<Button>(host.Root, "Click"));
        Assert.Contains("Clicked 1 times", Texts(host.Root));

        var box = All<TextBox>(host.Root).Single();
        box.Focus();
        window.KeyTextInput("Water the plants");
        Dispatcher.UIThread.RunJobs();
        Click(Labelled<Button>(host.Root, "Add"));
        Assert.Contains("[ ] Water the plants", Texts(host.Root));
        Assert.Contains("1 of 1 left", Texts(host.Root));
        Save(window, "sample-basics.png");
    }

    [AvaloniaFact]
    public void The_Go_menu_and_the_tabs_share_the_page_and_the_View_menu_turns_tooltips_off()
    {
        var (host, window) = Open();
        var tabs = Tabs(host);

        ChooseFromMenuBar(host, "Go", "Numbers");
        Assert.Equal(3, tabs.SelectedIndex);
        Assert.Contains("Value: 20", Texts(host.Root));

        Page(host, "Buttons"); // a tab click moves the Go menu's radio item
        var go = All<AvMenuItem>(host.Root).Single(i => Equals(i.Header, "Go"));
        Assert.Equal(["Buttons"], go.Items.Cast<AvMenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header!));

        var bold = Labelled<ToggleButton>(host.Root, "Bold");
        Assert.Equal("Stays pressed until clicked again", AvToolTip.GetTip(bold));
        ChooseFromMenuBar(host, "View", "Show tooltips");
        Assert.Null(AvToolTip.GetTip(bold));
        ChooseFromMenuBar(host, "View", "Show tooltips");
        Assert.Equal("Stays pressed until clicked again", AvToolTip.GetTip(bold));
    }

    [AvaloniaFact]
    public void Buttons_page_element_content_toggle_three_state_repeat_and_hyperlink_buttons_work()
    {
        var (host, window) = Open();
        var page = Page(host, "Buttons");

        var save = All<Button>(page).Single(b => b.Content is StackPanel); // element content: a badge and a label
        Assert.Equal(["S", "Save"], Texts(save));
        Click(save);
        Assert.Contains("Saved once", Texts(page));

        Click(Labelled<ToggleButton>(page, "Bold"));
        Assert.Contains("Bold: on", Texts(page));

        var filter = Labelled<ToggleButton>(page, "Filter");
        var states = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            Click(filter);
            states.Add(Texts(page).Single(t => t.StartsWith("Filter: ")));
        }
        Assert.Equal(["Filter: on", "Filter: mixed", "Filter: off"], states);

        Click(Labelled<RepeatButton>(page, "+"));
        Click(Labelled<RepeatButton>(page, "+"));
        Click(Labelled<RepeatButton>(page, "-"));
        Assert.Contains("Volume 51", Texts(page));

        Click(Labelled<HyperlinkButton>(page, "A link-styled button"));
        Assert.Contains("Link clicked once", Texts(page));
        Save(window, "sample-buttons.png");
    }

    [AvaloniaFact]
    public void Menus_page_right_click_menu_pins_labels_and_deletes_a_file()
    {
        var (host, window) = Open();
        var page = Page(host, "Menus");
        Border Row(string name) => All<TextBlock>(page).Single(t => t.Text?.StartsWith(name) == true).GetVisualAncestorsOfType<Border>().First();

        void Choose(string file, params string[] path)
        {
            Click(Row(file), MouseButton.Right); // the context flyout opens on a right-click
            var flyout = Assert.IsType<MenuFlyout>(Row(file).ContextFlyout);
            Assert.True(flyout.IsOpen);
            var items = flyout.Items.Cast<object>();
            AvMenuItem? item = null;
            foreach (var header in path)
            {
                if (item is not null)
                {
                    item.IsSubMenuOpen = true;
                    Dispatcher.UIThread.RunJobs();
                }
                item = items.OfType<AvMenuItem>().Single(i => Equals(i.Header, header));
                items = item.Items.Cast<object>();
            }
            Click(item!);
        }

        Choose("notes.txt", "Pinned");
        Assert.Contains("notes.txt (pinned)", Texts(page));
        Assert.Contains("Last command: Pinned notes.txt", Texts(page));

        Choose("report.docx", "Label", "green");
        Assert.Contains("report.docx [green]", Texts(page));

        Choose("photo.png", "Delete");
        Assert.DoesNotContain(Texts(page), t => t.StartsWith("photo.png"));
        Assert.Contains("Last command: Deleted photo.png", Texts(page));

        Click(Labelled<Button>(page, "Bring the files back"));
        Assert.Contains("photo.png", Texts(page));
        Save(window, "sample-menus.png");
    }

    [AvaloniaFact]
    public void Numbers_page_number_box_and_slider_show_one_value()
    {
        var (host, window) = Open();
        var page = Page(host, "Numbers");
        var box = All<NumericUpDown>(page).Single();
        var slider = All<Slider>(page).Single();
        Assert.Equal((20m, 20.0), (box.Value, slider.Value));

        // The spinner's up button, with the mouse: one step of 5.
        Click(All<RepeatButton>(box).Single(b => b.Name == "PART_IncreaseButton"));
        Assert.Equal((25m, 25.0), (box.Value, slider.Value));
        Assert.Contains("Value: 25", Texts(page));

        slider.Value = 69.6; // as a drag does: the page keeps whole numbers
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((70m, 70.0), (box.Value, slider.Value));
        Assert.Contains("Value: 70", Texts(page));

        // Emptied with the keyboard: the box reports no number, and the value stays as it was.
        var text = All<TextBox>(box).Single();
        text.Focus();
        text.SelectAll();
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(box.Value);
        Assert.Equal(70.0, slider.Value);
        Assert.Contains("Value: 70", Texts(page));

        Click(Labelled<Button>(page, "Reset"));
        Assert.Equal((20m, 20.0), (box.Value, slider.Value));
        Save(window, "sample-numbers.png");
    }

    [AvaloniaFact]
    public void Split_view_page_toggle_drives_the_pane_and_a_light_dismiss_comes_back_to_it()
    {
        var (host, window) = Open();
        var page = Page(host, "Split view");
        var view = All<AvSplitView>(page).Single();
        var toggle = Labelled<ToggleButton>(page, "Pane");
        Assert.True(view.IsPaneOpen);

        Click(Labelled<Button>(page, "Archive")); // a pane button picks the content
        Assert.Contains("Showing Archive", Texts(page));
        Save(window, "sample-split-view.png");

        Click(toggle);
        Assert.False(view.IsPaneOpen);
        Assert.Contains("Pane closed", Texts(page));

        All<ComboBox>(page).Single().SelectedIndex = 1; // Overlay, as choosing it from the drop-down does
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SplitViewDisplayMode.Overlay, view.DisplayMode);
        Click(toggle);
        Assert.True(view.IsPaneOpen);

        // A click on the content, outside the overlaid pane: the control closes it and the state follows.
        var content = All<TextBlock>(page).Single(t => t.Text?.StartsWith("In an overlay mode") == true);
        Click(content);
        Assert.False(view.IsPaneOpen);
        Assert.Equal(false, toggle.IsChecked);
        Assert.Contains("Pane closed", Texts(page));
    }

    [AvaloniaFact]
    public void Errors_page_throw_shows_the_fallback_retry_recovers_and_the_outside_counter_keeps_its_state()
    {
        var (host, window) = Open();
        var page = Page(host, "Errors");

        Click(Labelled<Button>(page, "Click")); // the counter outside the boundary
        Click(Labelled<Button>(page, "Click the panel"));
        Assert.Contains("The panel works: clicked 1 times", Texts(page));

        Click(Labelled<Button>(page, "Throw"));
        Assert.Contains("The panel failed: " + ErrorsPage.Message, Texts(page));
        Assert.DoesNotContain(Texts(page), t => t.StartsWith("The panel works"));
        Assert.Contains("Clicked 1 times", Texts(page));
        Save(window, "sample-errors.png");

        Click(Labelled<Button>(page, "Retry"));
        Assert.Contains("The panel works: clicked 0 times", Texts(page)); // a new mount: the failed panel's state went with it
        Assert.Contains("The panel has failed once", Texts(page));
        Assert.Contains("Clicked 1 times", Texts(page));
    }
}
