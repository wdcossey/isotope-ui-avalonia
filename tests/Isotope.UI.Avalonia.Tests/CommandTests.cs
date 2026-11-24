using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// Upstream's Command on the button family and menu items (#29), mapped to Avalonia's ICommand, in a headless window:
/// a click runs the current render's command, the button is disabled while the command can't run, an explicit callback
/// wins, and the command's shortcut and description are written and cleared.
/// </summary>
public sealed class CommandTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(TestHost host)
    {
        if (TopLevel.GetTopLevel(host.Root) is Window shown) return shown;
        var window = new Window { Width = 400, Height = 300, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // A real headless click at the control's centre, then the root passes the state changes asked for.
    private void Click(TestHost host, Control control)
    {
        Show(host);
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("the control isn't shown");
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)
            ?? throw new InvalidOperationException("the control isn't shown");
        top.MouseDown(centre, MouseButton.Left);
        top.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Dispatcher.UIThread.RunJobs();
    }

    private void Press(TestHost host, PhysicalKey key, RawInputModifiers modifiers)
    {
        Show(host).KeyPressQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
        host.Flush();
    }

    [AvaloniaFact]
    public void Button_with_a_command_shows_its_label_and_a_click_runs_it()
    {
        var log = new List<string>();
        var host = new TestHost(() => Button(new Command { Label = "Save", Execute = () => log.Add("save") }));
        var button = host.All<Button>()[0];
        Assert.Equal("Save", button.Content);
        Assert.NotNull(button.Command);

        Click(host, button);
        Assert.Equal(["save"], log);
    }

    [AvaloniaFact]
    public void A_click_runs_the_current_renders_command()
    {
        // Two commands that differ only in their action render the same, so the button isn't updated: the click still
        // runs the new action.
        var log = new List<string>();
        var n = 0;
        var host = new TestHost(() =>
        {
            var current = n;
            return Button(new Command { Label = "Go", Execute = () => log.Add("go " + current) });
        });
        n = 1;
        host.Render();
        Click(host, host.All<Button>()[0]);
        Assert.Equal(["go 1"], log);
    }

    [AvaloniaFact]
    public void An_async_command_runs()
    {
        var log = new List<string>();
        var host = new TestHost(() => Button(new Command { Label = "Load", ExecuteAsync = () => { log.Add("load"); return Task.CompletedTask; } }));
        Click(host, host.All<Button>()[0]);
        Assert.Equal(["load"], log);
    }

    [AvaloniaFact]
    public void The_button_is_disabled_while_the_command_cannot_run()
    {
        var log = new List<string>();
        Action<bool>? setCan = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (can, set) = ctx.UseState(false);
            setCan = set;
            return Button(new Command { Label = "Send", CanExecute = can, Execute = () => log.Add("send") });
        }));
        Show(host);
        var button = host.All<Button>()[0];
        Assert.False(button.IsEffectivelyEnabled);
        Click(host, button);
        Assert.Empty(log);

        setCan!(true);
        host.Flush();
        Assert.True(button.IsEffectivelyEnabled);
        Click(host, button);
        Assert.Equal(["send"], log);
    }

    [AvaloniaFact]
    public void UseCommand_disables_the_button_while_its_async_action_runs()
    {
        var done = new TaskCompletionSource();
        var host = new TestHost(() => Component(ctx =>
            Button(ctx.UseCommand(new Command { Label = "Sync", ExecuteAsync = () => done.Task }))));
        var button = host.All<Button>()[0];
        Show(host);
        Assert.True(button.IsEffectivelyEnabled);

        Click(host, button);
        Assert.False(button.IsEffectivelyEnabled);

        done.SetResult(); // the hook's continuation may run off this thread: wait for the render it asks for
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!button.IsEffectivelyEnabled && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            host.Flush();
            Thread.Sleep(10);
        }
        Assert.True(button.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void OnClick_wins_over_the_command()
    {
        var log = new List<string>();
        var host = new TestHost(() => new ButtonElement("Both", () => log.Add("click"))
        {
            Command = new Command { Label = "Both", Execute = () => log.Add("command") },
        });
        Click(host, host.All<Button>()[0]);
        Assert.Equal(["click"], log);
    }

    [AvaloniaFact]
    public void A_callback_that_comes_later_takes_the_click_from_the_command()
    {
        // With a command, the element has callbacks before and after: the new callback alone must decline the skip, or it
        // is never subscribed and the click runs nothing.
        var log = new List<string>();
        var cmd = new Command { Label = "Go", Execute = () => log.Add("command") };
        var withCallback = false;
        var host = new TestHost(() => VStack(
            new ButtonElement("Go", withCallback ? () => log.Add("click") : null) { Command = cmd },
            ToggleButton("Bold", false, withCallback ? v => log.Add("toggled " + v) : null) with { Command = cmd },
            RepeatButton("More", withCallback ? () => log.Add("more") : null) with { Command = cmd },
            HyperlinkButton("Help", null, withCallback ? () => log.Add("help") : null) with { Command = cmd }));
        withCallback = true;
        host.Render();
        Click(host, host.All<Button>()[0]);
        Click(host, host.All<ToggleButton>()[0]);
        Click(host, host.All<RepeatButton>()[0]);
        Click(host, host.All<HyperlinkButton>()[0]);
        Assert.Equal(["click", "toggled True", "more", "help"], log);
    }

    [AvaloniaFact]
    public void Command_modifier_binds_a_content_button_and_clears_its_callback()
    {
        var log = new List<string>();
        var cmd = new Command { Label = "Run", Execute = () => log.Add("run") };
        var el = Button(TextBlock("Run now"), () => log.Add("click")).Command(cmd);
        Assert.Null(el.OnClick);
        Assert.Same(cmd, el.Command);
        var host = new TestHost(() => el);
        Click(host, host.All<Button>()[0]);
        Assert.Equal(["run"], log);
    }

    [AvaloniaFact]
    public void Removing_the_command_clears_it_from_the_button()
    {
        var on = true;
        var host = new TestHost(() => on
            ? Button(new Command { Label = "Stop", CanExecute = false, Description = "Stops it", Accelerator = new(Key.S, KeyModifiers.Control) })
            : Button("Stop"));
        var button = host.All<Button>()[0];
        Show(host);
        Assert.False(button.IsEffectivelyEnabled);

        on = false;
        host.Render();
        Assert.Same(button, host.All<Button>()[0]);
        Assert.Null(button.Command);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Null(AvToolTip.GetTip(button));
        Assert.Null(AutomationProperties.GetHelpText(button));
        Assert.Null(button.HotKey);
    }

    [AvaloniaFact]
    public void The_command_shortcut_runs_it_and_its_description_is_the_tooltip()
    {
        var log = new List<string>();
        var host = new TestHost(() => Button(new Command
        {
            Label = "Save", Execute = () => log.Add("save"), Description = "Save the file", Accelerator = new(Key.S, KeyModifiers.Control),
        }));
        var button = host.All<Button>()[0];
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), button.HotKey);
        Assert.Equal("Save the file", AvToolTip.GetTip(button));
        Assert.Equal("Save the file", AutomationProperties.GetHelpText(button));

        Press(host, PhysicalKey.S, RawInputModifiers.Control);
        Assert.Equal(["save"], log);
    }

    [AvaloniaFact]
    public void A_tooltip_modifier_wins_over_the_command_description()
    {
        var description = "one";
        var host = new TestHost(() => Button(new Command { Label = "Save", Description = description }).ToolTip("Save (Ctrl+S)"));
        var button = host.All<Button>()[0];
        Assert.Equal("Save (Ctrl+S)", AvToolTip.GetTip(button));
        description = "two"; // the command's description changes; the modifier doesn't
        host.Render();
        Assert.Equal("Save (Ctrl+S)", AvToolTip.GetTip(button));
        Assert.Equal("two", AutomationProperties.GetHelpText(button));
    }

    [AvaloniaFact]
    public void Removing_a_tooltip_modifier_gives_the_tip_back_to_the_command_description()
    {
        var cmd = new Command { Label = "Save", Description = "Save the file" };
        var modifier = 0; // 0: none, 1: text, 2: element
        var host = new TestHost(() => modifier switch
        {
            1 => Button(cmd).ToolTip("Custom"),
            2 => Button(cmd).WithToolTip(TextBlock("Rich")),
            _ => Button(cmd),
        });
        var button = host.All<Button>()[0];
        Assert.Equal("Save the file", AvToolTip.GetTip(button));
        foreach (var next in new[] { 1, 0, 2, 0 })
        {
            modifier = next;
            host.Render();
            if (next == 0) Assert.Equal("Save the file", AvToolTip.GetTip(button));
        }
    }

    [AvaloniaFact]
    public void Removing_the_command_keeps_a_tooltip_modifier_that_equals_its_description()
    {
        var on = true;
        var host = new TestHost(() => (on ? Button(new Command { Label = "Save", Description = "Save" }) : Button("Save")).ToolTip("Save"));
        var button = host.All<Button>()[0];
        on = false;
        host.Render();
        Assert.Equal("Save", AvToolTip.GetTip(button));
    }

    [AvaloniaFact]
    public void A_wrapper_tooltip_wins_over_the_command_description()
    {
        var description = "A";
        var host = new TestHost(() => new ModifiedElement(Button(new Command { Label = "Go", Description = description }),
            new ElementModifiers { ToolTip = "Mine" }));
        var button = host.All<Button>()[0];
        description = "B";
        host.Render();
        Assert.Equal("Mine", AvToolTip.GetTip(button));
        Assert.Equal("B", AutomationProperties.GetHelpText(button));
    }

    [AvaloniaFact]
    public void ToggleButton_with_a_command_runs_it_on_each_toggle()
    {
        var log = new List<string>();
        var host = new TestHost(() => ToggleButton(new Command { Label = "Bold", Execute = () => log.Add("bold") }));
        var toggle = host.All<ToggleButton>()[0];
        Assert.Equal("Bold", toggle.Content);
        Click(host, toggle);
        Click(host, toggle);
        Assert.Equal(["bold", "bold"], log);
    }

    [AvaloniaFact]
    public void A_toggle_callback_wins_over_the_command_and_the_modifier_clears_it()
    {
        var log = new List<string>();
        var cmd = new Command { Label = "Bold", Execute = () => log.Add("command") };
        var host = new TestHost(() => ToggleButton("Bold", false, v => log.Add("toggled " + v)) with { Command = cmd });
        Click(host, host.All<ToggleButton>()[0]);
        Assert.Equal(["toggled True"], log);

        var bound = ToggleButton("Bold", false, v => log.Add("toggled " + v)).Command(cmd);
        Assert.Null(bound.OnIsCheckedChanged);
        Assert.Null(bound.OnIsCheckedChanged);
    }

    [AvaloniaFact]
    public void RepeatButton_and_HyperlinkButton_with_a_command_run_it()
    {
        var log = new List<string>();
        var host = new TestHost(() => VStack(
            RepeatButton(new Command { Label = "More", Execute = () => log.Add("more") }),
            HyperlinkButton(new Command { Label = "Help", Execute = () => log.Add("help") })));
        var repeat = host.All<RepeatButton>()[0];
        var link = host.All<HyperlinkButton>()[0];
        Assert.Equal(("More", "Help"), (repeat.Content, link.Content));
        Click(host, repeat);
        Click(host, link);
        Assert.Equal(["more", "help"], log);
    }

    [AvaloniaFact]
    public void Command_factories_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => Button((Command)null!));
        Assert.Throws<ArgumentNullException>(() => HyperlinkButton((Command)null!));
        Assert.Throws<ArgumentNullException>(() => RepeatButton((Command)null!));
        Assert.Throws<ArgumentNullException>(() => ToggleButton((Command)null!));
        Assert.Throws<ArgumentNullException>(() => MenuItem((Command)null!));
        Assert.Throws<ArgumentNullException>(() => MenuItem((Command<int>)null!, 1));
    }

    [AvaloniaFact]
    public void MenuItem_from_a_command_copies_its_text_state_shortcut_and_description()
    {
        var log = new List<string>();
        var item = MenuItem(new Command
        {
            Label = "Open", Execute = () => log.Add("open"), CanExecute = false, Description = "Open a file",
            Accelerator = new(Key.O, KeyModifiers.Control),
        });
        Assert.Equal("Open", item.Header);
        Assert.False(item.IsEnabled);
        Assert.Equal([new KeyboardAcceleratorData(Key.O, KeyModifiers.Control)], item.KeyboardAccelerators!);
        Assert.Equal("Open a file", item.Description);
        item.OnClick!();
        Assert.Equal(["open"], log);

        var asyncItem = MenuItem(new Command { Label = "Load", ExecuteAsync = () => { log.Add("load"); return Task.CompletedTask; } });
        asyncItem.OnClick!();
        Assert.Equal(["open", "load"], log);
        Assert.Null(MenuItem(new Command { Label = "Nothing" }).OnClick);

        var delete = MenuItem(new Command<string> { Label = "Delete", Execute = name => log.Add("delete " + name) }, "a.txt");
        delete.OnClick!();
        Assert.Equal(["open", "load", "delete a.txt"], log);
    }

    [AvaloniaFact]
    public void Menu_item_shortcut_is_shown_and_runs_the_item_and_its_description_is_the_tooltip()
    {
        var log = new List<string>();
        var shortcut = true;
        var host = new TestHost(() => MenuBar(Menu("File", MenuItem("Save", () => log.Add("save")) with
        {
            KeyboardAccelerators = shortcut ? [new(Key.S, KeyModifiers.Control)] : null,
            Description = shortcut ? "Save the file" : null,
        })));
        var save = (AvMenuItem)((AvMenuItem)host.All<AvMenu>()[0].Items[0]!).Items[0]!;
        var gesture = new KeyGesture(Key.S, KeyModifiers.Control);
        Assert.Equal((gesture, gesture), (save.InputGesture, save.HotKey));
        Assert.Equal("Save the file", AvToolTip.GetTip(save));
        Assert.Equal("Save the file", AutomationProperties.GetHelpText(save));

        Press(host, PhysicalKey.S, RawInputModifiers.Control); // the menu is closed: the shortcut still runs the item
        Assert.Equal(["save"], log);

        shortcut = false;
        host.Render();
        Assert.Same(save, ((AvMenuItem)host.All<AvMenu>()[0].Items[0]!).Items[0]);
        Assert.Equal((null, null), (save.InputGesture, save.HotKey));
        Assert.Null(AvToolTip.GetTip(save));
        Press(host, PhysicalKey.S, RawInputModifiers.Control);
        Assert.Equal(["save"], log);
    }
}
