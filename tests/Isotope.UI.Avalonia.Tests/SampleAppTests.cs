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
/// headless click on its item in the page list (#96) and its main interaction driven with headless mouse input, then
/// rendered to PNG. What only a
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

    // holdMs: a pause between press and release, as a slow runner makes (#55).
    private static void Click(Control control, MouseButton button = MouseButton.Left, int holdMs = 0)
    {
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("not shown");
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("not shown");
        TestHost.HoldOffRepeat(control);
        top.MouseDown(centre, button);
        if (holdMs > 0) Thread.Sleep(holdMs);
        top.MouseUp(centre, button);
        Dispatcher.UIThread.RunJobs(); // the posted root pass and the layout that follows it
    }

    private static List<T> All<T>(Control root) where T : Control => root.GetSelfAndVisualDescendantsOfType<T>();

    private static List<string> Texts(Control root) => All<TextBlock>(root).Select(t => t.Text ?? "").ToList();

    private static T Labelled<T>(Control root, string label) where T : ContentControl =>
        All<T>(root).FirstOrDefault(c => Equals(c.Content, label)) ?? throw new InvalidOperationException("nothing labelled " + label);

    // The app's split view: the first in the tree (the Split view page has its own, inside the content).
    private static AvSplitView Shell(DispatcherHost host) => All<AvSplitView>(host.Root).First();

    private static ListBox PageList(DispatcherHost host) => Assert.IsType<ListBox>(Shell(host).Pane);

    // Every page's control, in list order: all mounted, one shown.
    private static List<Control> PageControls(DispatcherHost host) =>
        Assert.IsType<Grid>(Shell(host).Content).Children.ToList();

    // The page shown, after checking that it's the only one shown and that the list has it selected.
    private static Control Shown(DispatcherHost host, string name)
    {
        var index = Array.FindIndex(SampleApp.Pages, p => p.Name == name);
        Assert.Equal(index, PageList(host).SelectedIndex);
        var pages = PageControls(host);
        Assert.Equal(SampleApp.Pages.Length, pages.Count);
        Assert.Equal([index], pages.Select((p, i) => (p, i)).Where(x => x.p.IsVisible).Select(x => x.i));
        return pages[index];
    }

    // The app's menu bar: the first in the tree (every page is in the tree too, hidden or not).
    private static Menu MenuBar(DispatcherHost host) => All<Menu>(host.Root).First();

    // The Go menu's items that have the radio mark.
    private static IEnumerable<string> GoMarked(DispatcherHost host) =>
        All<AvMenuItem>(MenuBar(host)).Single(i => Equals(i.Header, "Go")).Items.Cast<AvMenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header!);

    // Selects a page as the user does: a click on its item in the list.
    private static Control Page(DispatcherHost host, string name)
    {
        Click(Labelled<ListBoxItem>(PageList(host), name));
        return Shown(host, name);
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // Opens a menu bar's top-level menu with a click on its header, then clicks the item.
    private static void ChooseFromMenuBar(DispatcherHost host, string menu, string item)
    {
        var top = All<AvMenuItem>(MenuBar(host)).Single(i => Equals(i.Header, menu));
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
    public void The_app_lists_every_page_beside_it_and_the_Basics_counter_and_todo_work()
    {
        var (host, window) = Open();
        var shell = Shell(host);
        Assert.Equal((SplitViewDisplayMode.Inline, true), (shell.DisplayMode, shell.IsPaneOpen));
        var list = PageList(host);
        Assert.Equal(SampleApp.Pages.Select(p => p.Name), All<ListBoxItem>(list).Select(i => (string)i.Content!));
        var page = Shown(host, "Basics");
        Assert.Equal(["Basics"], GoMarked(host));

        Click(Labelled<Button>(page, "Click"));
        Assert.Contains("Clicked 1 times", Texts(page));

        var box = All<TextBox>(page).Single();
        box.Focus();
        window.KeyTextInput("Water the plants");
        Dispatcher.UIThread.RunJobs();
        Click(Labelled<Button>(page, "Add"));
        Assert.Contains("[ ] Water the plants", Texts(page));
        Assert.Contains("1 of 1 left", Texts(page));
        Save(window, "sample-basics.png");
    }

    [AvaloniaFact]
    public void The_Go_menu_and_the_list_share_the_page_and_the_View_menu_turns_tooltips_off()
    {
        var (host, window) = Open();

        ChooseFromMenuBar(host, "Go", "Numbers"); // the menu moves the list's selection
        Assert.Contains("Value: 20", Texts(Shown(host, "Numbers")));
        Assert.Equal(["Numbers"], GoMarked(host));

        var page = Page(host, "Buttons"); // a click in the list moves the Go menu's radio item
        Assert.Equal(["Buttons"], GoMarked(host));

        var bold = Labelled<ToggleButton>(page, "Bold");
        Assert.Equal("Stays pressed until clicked again", AvToolTip.GetTip(bold));
        ChooseFromMenuBar(host, "View", "Show tooltips");
        Assert.Null(AvToolTip.GetTip(bold));
        ChooseFromMenuBar(host, "View", "Show tooltips");
        Assert.Equal("Stays pressed until clicked again", AvToolTip.GetTip(bold));
    }

    [AvaloniaFact]
    public void Switching_pages_keeps_each_pages_state()
    {
        var (host, window) = Open();
        var basics = Shown(host, "Basics");
        Click(Labelled<Button>(basics, "Click"));
        Click(Labelled<Button>(basics, "Click"));
        var draft = All<TextBox>(basics).Single();
        draft.Focus();
        window.KeyTextInput("Not added yet");
        Dispatcher.UIThread.RunJobs();

        var numbers = Page(host, "Numbers");
        var box = All<NumericUpDown>(numbers).Single();
        Click(All<RepeatButton>(box).Single(b => b.Name == "PART_IncreaseButton"));
        Assert.Equal(25m, box.Value);

        var errors = Page(host, "Errors");
        Click(Labelled<Button>(errors, "Throw"));
        Assert.Contains("The panel failed: " + ErrorsPage.Message, Texts(errors));

        // Back to each: the same controls, with what was done on them.
        Assert.Same(basics, Page(host, "Basics"));
        Assert.Contains("Clicked 2 times", Texts(basics));
        Assert.Equal("Not added yet", draft.Text);
        Assert.Same(numbers, Page(host, "Numbers"));
        Assert.Equal(25m, box.Value);
        Assert.Contains("Value: 25", Texts(numbers));
        Assert.Same(errors, Page(host, "Errors"));
        Assert.Contains("The panel failed: " + ErrorsPage.Message, Texts(errors));
    }

    [AvaloniaFact]
    public void Arrow_keys_in_the_list_change_the_page_and_Tab_goes_into_the_page_shown()
    {
        var (host, window) = Open();
        var list = PageList(host);
        var basics = All<ListBoxItem>(list)[0];
        basics.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, PhysicalKey.ArrowDown);
        var buttons = Shown(host, "Buttons");
        Assert.Equal(["Buttons"], GoMarked(host));
        Press(window, PhysicalKey.ArrowDown);
        Shown(host, "Menus");
        Press(window, PhysicalKey.ArrowUp);
        Assert.Same(buttons, Shown(host, "Buttons"));

        Press(window, PhysicalKey.Tab); // out of the list and into the shown page, not a hidden one
        var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        Assert.Contains(buttons, focused.GetVisualAncestorsOfType<Control>());
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
    public void After_a_slow_click_no_tooltip_takes_the_next_click()
    {
        // #55: a headless MouseUp runs the due timers first. Held past a tooltip's ShowDelay (400 ms), as on a slow
        // runner, Bold's tooltip opened over Filter and took its click.
        var (host, _) = Open();
        var page = Page(host, "Buttons");
        Click(Labelled<ToggleButton>(page, "Bold"), holdMs: 500);
        Click(Labelled<ToggleButton>(page, "Filter"));
        Assert.Contains("Filter: on", Texts(page));
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

    // A text block's text, from its inlines when it has some (#65).
    private static string TextOf(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : block.Text ?? "";

    [AvaloniaFact]
    public void Rich_text_page_likes_from_a_button_in_the_text_and_rotates_its_keyed_runs()
    {
        var (host, window) = Open();
        var page = Page(host, "Rich text");
        var blocks = All<TextBlock>(page);
        Assert.Contains(blocks, b => TextOf(b) == "Rich text is bold, italic, underlined, struck through and coloured, in one text block.");

        Click(Labelled<Button>(page, "Like")); // a button in the flow of the text, clicked where it is laid out
        Assert.Contains(All<TextBlock>(page), b => TextOf(b).EndsWith(" liked 1 times.", StringComparison.Ordinal));

        var colours = All<TextBlock>(page).Single(b => TextOf(b) == "Red Green Blue ");
        var runs = colours.Inlines!.ToArray();
        Click(Labelled<Button>(page, "Rotate the colours"));
        Assert.Equal("Green Blue Red ", TextOf(colours));
        Assert.Equal([runs[1], runs[2], runs[0]], colours.Inlines!.ToArray());

        var selectable = All<SelectableTextBlock>(page).Single();
        Assert.StartsWith("Selectable text. Drag over", TextOf(selectable));
        Save(window, "sample-rich-text.png");
    }

    [AvaloniaFact]
    public void Every_page_and_the_list_fit_in_the_apps_window()
    {
        // Nothing is cut off on the right (#96): each page name's text gets the width it wants, and on each page, laid out
        // beside the list in the app's window, no control reaches past the page's right edge and no one-line text is
        // narrower than its text. Wrapping text may wrap. Run on each system: the fonts differ (CI run 318).
        var (host, _) = Open();
        static double Wanted(TextBlock t)
        {
            t.Measure(Size.Infinity);
            var width = t.DesiredSize.Width;
            t.InvalidateMeasure();
            return width;
        }
        static IEnumerable<string> CutOff(Control root) =>
            All<Control>(root)
                .Select(c => (c, right: c.TranslatePoint(new Point(c.Bounds.Width, 0), root)?.X ?? 0))
                .Where(x => x.right > root.Bounds.Width + 0.5)
                .Select(x => $"{x.c.GetType().Name} ends at {x.right} of {root.Bounds.Width}")
                .Concat(All<TextBlock>(root)
                    .Where(t => t.TextWrapping == TextWrapping.NoWrap && t.IsEffectivelyVisible && Wanted(t) > t.Bounds.Width + 0.5)
                    .Select(t => $"\"{t.Text}\" wants {Wanted(t)} of {t.Bounds.Width}"));

        var list = PageList(host);
        Assert.Equal(SampleApp.PaneWidth, list.Bounds.Width);
        Assert.Empty(CutOff(list));
        foreach (var (name, _) in SampleApp.Pages)
        {
            var page = Page(host, name);
            Assert.Equal(SampleApp.WindowWidth - SampleApp.PaneWidth, page.Bounds.Width);
            Assert.True(!CutOff(page).Any(), $"{name}: {string.Join("; ", CutOff(page))}");
        }
    }

    [AvaloniaFact]
    public void Icons_page_shows_each_kind_and_its_last_icon_changes_kind()
    {
        var (host, window) = Open();
        var page = Page(host, "Icons");
        var paths = All<global::Avalonia.Controls.PathIcon>(page);
        Assert.Equal(4, paths.Count); // three shapes and the icon that changes kind
        Assert.Equal([new Size(20, 20), new Size(32, 32), new Size(48, 48)], paths.Take(3).Select(p => p.Bounds.Size));
        // The ring, an avares resource of the app, loaded by both bitmap icons and the image.
        Assert.Equal(2, All<BitmapIconPresenter>(page).Count(b => b.Source is { PixelSize.Width: 24 }));
        Assert.Single(All<Image>(page), i => i.Source is { Size.Width: 24 });
        Save(window, "sample-icons.png");

        var last = paths[3];
        Click(Labelled<Button>(page, "Next kind"));
        Assert.Contains("One icon showing a glyph: another kind mounts another control.", Texts(page));
        Assert.Equal(3, All<global::Avalonia.Controls.PathIcon>(page).Count);
        Assert.Null(last.Parent);
        Click(Labelled<Button>(page, "Next kind"));
        Assert.Equal(3, All<BitmapIconPresenter>(page).Count);
        Click(Labelled<Button>(page, "Next kind"));
        Assert.Equal(2, All<Image>(page).Count);
        Click(Labelled<Button>(page, "Next kind"));
        Assert.Equal(4, All<global::Avalonia.Controls.PathIcon>(page).Count);
        Assert.Contains("One icon showing a shape: another kind mounts another control.", Texts(page));
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
