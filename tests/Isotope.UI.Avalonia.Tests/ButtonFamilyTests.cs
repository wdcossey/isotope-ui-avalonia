using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Element content (#26): Button and the rest of the button family take any element as content, reconciled in place so
/// a child component keeps its state, and torn down when the content goes back to text or the button unmounts; Expander's
/// element header. ToggleButton, RepeatButton and HyperlinkButton mount, update in place and report clicks.
/// </summary>
public sealed class ButtonFamilyTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // A real headless click, so a toggle button toggles itself as it does for a user.
    private void ClickInWindow(TestHost host, Control control)
    {
        if (TopLevel.GetTopLevel(control) is not Window window)
        {
            window = new Window { Width = 400, Height = 300, Content = host.Root, SizeToContent = SizeToContent.Manual };
            _windows.Add(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("not in the window");
        TestHost.HoldOffRepeat(control);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // A child component with its own state and a cleanup, to see that the content is reconciled and torn down.
    private static Func<RenderContext, Element> Counter(List<string> log, Action<Action>? exposeBump = null) => ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        exposeBump?.Invoke(() => setN(n + 1));
        ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
        return TextBlock("count " + n);
    };

    [AvaloniaFact]
    public void Content_is_Avalonia_s_object_a_value_is_written_as_it_is_and_an_element_is_mounted()
    {
        // #31: one Content, as Avalonia's ContentControl.Content: a string or other value is written as it is, an element
        // is mounted as a child, and the button moves between them in place.
        object? content = 42;
        var host = new TestHost(() => new ButtonElement(content));
        var button = host.All<Button>()[0];
        Assert.Equal(42, button.Content);

        content = 43;
        host.Render();
        Assert.Same(button, host.All<Button>()[0]);
        Assert.Equal(43, button.Content);

        content = TextBlock("child");
        host.Render();
        Assert.Equal("child", Assert.IsType<TextBlock>(button.Content).Text);

        content = "text";
        host.Render();
        Assert.Equal("text", button.Content);
        Assert.Empty(host.All<TextBlock>());
    }

    [AvaloniaFact]
    public void Button_with_element_content_mounts_it_and_a_child_component_keeps_its_state()
    {
        var log = new List<string>();
        Action? bump = null;
        var clicks = 0;
        var tag = "a";
        var counter = Counter(log, b => bump = b);
        var host = new TestHost(() => Button(HStack(TextBlock(tag), Component(counter)), () => clicks++));
        var button = host.All<Button>()[0];
        Assert.IsType<StackPanel>(button.Content);
        Assert.Equal(["a", "count 0"], host.Texts());

        bump!();
        host.Flush();
        Assert.Equal(["a", "count 1"], host.Texts());
        var panel = button.Content;

        tag = "b"; // the button updates: its content is reconciled, not remounted
        host.Render();
        Assert.Same(button, host.All<Button>()[0]);
        Assert.Same(panel, button.Content);
        Assert.Equal(["b", "count 1"], host.Texts());
        Assert.Empty(log);

        TestHost.Click(button);
        Assert.Equal(1, clicks);
    }

    [AvaloniaFact]
    public void Button_content_switches_between_text_and_an_element_and_tears_the_element_down()
    {
        var log = new List<string>();
        var counter = Counter(log);
        var asElement = false;
        var host = new TestHost(() => asElement ? Button(Component(counter)) : Button("Save"));
        var button = host.All<Button>()[0];
        Assert.Equal("Save", button.Content);

        asElement = true;
        host.Render();
        Assert.Same(button, host.All<Button>()[0]);
        Assert.IsAssignableFrom<Control>(button.Content);
        Assert.Equal(["count 0"], host.Texts());

        asElement = false;
        host.Render();
        Assert.Equal("Save", button.Content);
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void Button_element_content_is_torn_down_with_the_button()
    {
        var log = new List<string>();
        var counter = Counter(log);
        var host = new TestHost(() => VStack(Button(Component(counter))));

        host.Unmount();
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void Expander_element_header_mounts_keeps_its_state_and_gives_way_to_the_string_header()
    {
        var log = new List<string>();
        Action? bump = null;
        var counter = Counter(log, b => bump = b);
        var custom = true;
        var body = "body";
        var host = new TestHost(() =>
        {
            var e = Expander("Plain", TextBlock(body));
            return custom ? e.Header(Component(counter)) : e;
        });
        var expander = host.All<Expander>()[0];
        var header = Assert.IsAssignableFrom<Control>(expander.Header);
        Assert.Equal("count 0", header.GetSelfAndLogicalDescendants().OfType<TextBlock>().Single().Text);

        bump!();
        host.Flush();
        body = "body 2";
        host.Render();
        Assert.Same(header, expander.Header);
        Assert.Equal("count 1", header.GetSelfAndLogicalDescendants().OfType<TextBlock>().Single().Text);

        custom = false;
        host.Render();
        Assert.Equal("Plain", expander.Header);
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void Expander_element_header_is_torn_down_with_the_expander()
    {
        var log = new List<string>();
        var counter = Counter(log);
        var host = new TestHost(() => Expander("Plain", TextBlock("body")).Header(Component(counter)));

        host.Unmount();
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void ToggleButton_click_toggles_and_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (on, setOn) = ctx.UseState(false);
            set = setOn;
            return VStack(TextBlock("state " + on), ToggleButton("Bold", on, v => { reported.Add(v); setOn(v); }));
        }));
        var toggle = host.All<ToggleButton>()[0];
        Assert.Equal(("Bold", false), (toggle.Content, toggle.IsChecked));

        ClickInWindow(host, toggle);
        host.Flush();
        Assert.Equal([true], reported);
        Assert.Equal(["state True"], host.Texts());
        Assert.True(toggle.IsChecked);

        set!(false); // from the state: written, not reported
        host.Flush();
        Assert.False(toggle.IsChecked);
        Assert.Equal([true], reported);
        Assert.Same(toggle, host.All<ToggleButton>()[0]);
    }

    [AvaloniaFact]
    public void ThreeStateToggleButton_writes_its_checked_state_and_reports_null()
    {
        bool? state = null;
        var reported = new List<bool?>();
        var host = new TestHost(() => ThreeStateToggleButton("Mixed", state, v => reported.Add(v)));
        var toggle = host.All<ToggleButton>()[0];
        Assert.True(toggle.IsThreeState);
        Assert.Null(toggle.IsChecked);

        state = false; // only the three-state value changes (IsChecked stays false): the update isn't skipped
        host.Render();
        Assert.False(toggle.IsChecked);

        state = true;
        host.Render();
        Assert.True(toggle.IsChecked);

        ClickInWindow(host, toggle); // checked -> indeterminate, as Avalonia cycles a three-state toggle
        Assert.Equal([null], reported);
    }

    [AvaloniaFact]
    public void ToggleButton_takes_element_content()
    {
        var host = new TestHost(() => ToggleButton(HStack(TextBlock("B"), TextBlock("old")), true));
        var toggle = host.All<ToggleButton>()[0];
        Assert.IsType<StackPanel>(toggle.Content);
        Assert.Equal(["B", "old"], host.Texts());
        Assert.True(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void RepeatButton_mounts_its_timing_and_content_and_reports_clicks()
    {
        var clicks = 0;
        var delay = 400;
        var host = new TestHost(() => RepeatButton("More", () => clicks++) with { Delay = delay, Interval = 30 });
        var button = host.All<RepeatButton>()[0];
        Assert.Equal(("More", 400, 30), (button.Content, button.Delay, button.Interval));

        delay = 100;
        host.Render();
        Assert.Same(button, host.All<RepeatButton>()[0]);
        Assert.Equal(100, button.Delay);

        TestHost.Click(button);
        Assert.Equal(1, clicks);
    }

    [AvaloniaFact]
    public void RepeatButton_takes_element_content()
    {
        var host = new TestHost(() => RepeatButton(TextBlock("+")));
        Assert.IsType<TextBlock>(host.All<RepeatButton>()[0].Content);
    }

    [AvaloniaFact]
    public void HyperlinkButton_writes_and_clears_its_uri_and_reports_clicks()
    {
        Uri? uri = new("https://example.com/");
        var clicks = 0;
        var host = new TestHost(() => HyperlinkButton("Docs", uri, () => clicks++));
        var link = host.All<HyperlinkButton>()[0];
        Assert.Equal(("Docs", uri), (link.Content, link.NavigateUri));

        TestHost.Click(link);
        Assert.Equal(1, clicks);

        uri = null;
        host.Render();
        Assert.Same(link, host.All<HyperlinkButton>()[0]);
        Assert.Null(link.NavigateUri);
    }

    [AvaloniaFact]
    public void HyperlinkButton_takes_element_content()
    {
        var host = new TestHost(() => HyperlinkButton(HStack(TextBlock("Read"), TextBlock("more"))));
        Assert.IsType<StackPanel>(host.All<HyperlinkButton>()[0].Content);
        Assert.Equal(["Read", "more"], host.Texts());
    }
}
