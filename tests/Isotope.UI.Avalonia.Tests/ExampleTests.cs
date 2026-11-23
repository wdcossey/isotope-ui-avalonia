using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The examples end to end (#2): mounted in a headless window, driven with headless mouse and keyboard input, rendered
/// with Skia and captured to PNG (no real window). Renders are scheduled on the dispatcher, as an app host would.
/// </summary>
public sealed class ExampleTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(DispatcherHost host, double width, double height)
    {
        var w = new Window
        {
            Width = width, Height = height, SizeToContent = SizeToContent.Manual,
            Background = Brushes.White, Content = host.Root,
        };
        _windows.Add(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("not in the window");
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs(); // the posted root pass and the layout that follows it
    }

    private static void Type(Window window, TextBox box, string text)
    {
        box.Focus();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonLabelled(DispatcherHost host, string label, int index = 0)
        => All<Button>(host).Where(b => Equals(b.Content, label)).ElementAt(index);

    private static List<T> All<T>(DispatcherHost host) where T : Control
        => host.Root.GetSelfAndVisualDescendantsOfType<T>();

    private static string Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        var path = Path.Combine(AppContext.BaseDirectory, name);
#pragma warning disable CS0618 // Save(string) is obsolete in 12.x but still the plain PNG path (as in Spike D)
        frame.Save(path);
#pragma warning restore CS0618
        return path;
    }

    [AvaloniaFact]
    public void Counter_counts_real_clicks_and_renders_to_png()
    {
        var host = new DispatcherHost(() => new FuncElement(Examples.Counter));
        var window = Show(host, 240, 120);
        var text = All<TextBlock>(host).First(t => t.Text!.StartsWith("Clicked"));

        for (int i = 0; i < 3; i++) Click(window, ButtonLabelled(host, "Click"));

        Assert.Equal("Clicked 3 times", text.Text);
        Assert.Same(text, All<TextBlock>(host).First(t => t.Text!.StartsWith("Clicked")));
        Assert.Equal(4, host.Renders); // the mount and one root pass per click
        Assert.True(new FileInfo(Save(window, "example-counter.png")).Length > 1000);
    }

    [AvaloniaFact]
    public void TodoApp_adds_toggles_reverses_and_removes_with_real_input_and_renders_to_png()
    {
        var host = new DispatcherHost(() => new FuncElement(Examples.TodoApp).Provide(Examples.Accent, Brushes.LightSteelBlue));
        var window = Show(host, 420, 320);
        var box = All<TextBox>(host)[0];

        foreach (var title in new[] { "Buy milk", "Walk the dog", "Write the report" })
        {
            Type(window, box, title);
            Assert.Equal(title, box.Text);
            Click(window, ButtonLabelled(host, "Add"));
            Assert.Equal("", box.Text); // the controlled value was cleared
        }
        Assert.Same(box, All<TextBox>(host)[0]);
        Assert.Equal(["[ ] Buy milk", "[ ] Walk the dog", "[ ] Write the report"], RowTitles(host));

        Click(window, ButtonLabelled(host, "Done", 0)); // Buy milk
        Assert.Equal("[x] Buy milk", RowTitles(host)[0]);
        Assert.Contains("2 of 3 left", Texts(host));
        var milkRow = RowOf(host, "Buy milk");

        Click(window, ButtonLabelled(host, "Reverse"));
        Assert.Equal(["[ ] Write the report", "[ ] Walk the dog", "[x] Buy milk"], RowTitles(host));
        Assert.Same(milkRow, RowOf(host, "Buy milk")); // the keyed row moved from first to last, with its controls
        Assert.Equal("toggled 1", RowTexts(milkRow)[1]); // and its own state

        Click(window, ButtonLabelled(host, "Remove", 1)); // Walk the dog
        Assert.Equal(["[ ] Write the report", "[x] Buy milk"], RowTitles(host));
        Assert.Contains("1 of 2 left", Texts(host));

        Type(window, box, "Call the plumber"); // a draft, shown in the capture
        Assert.True(new FileInfo(Save(window, "example-todo.png")).Length > 1000);
    }

    private static List<string> Texts(DispatcherHost host) => All<TextBlock>(host).Select(t => t.Text ?? "").ToList();

    private static List<string> RowTitles(DispatcherHost host)
        => Texts(host).Where(t => t.StartsWith("[ ] ") || t.StartsWith("[x] ")).ToList();

    // A row's Border wrapper (the FuncElement's), found from its title.
    private static Control RowOf(DispatcherHost host, string title)
        => All<TextBlock>(host).First(t => t.Text!.EndsWith(title)).GetVisualAncestorsOfType<Border>()
            .First(b => b.Child is Layout.FlexPanel);

    private static List<string> RowTexts(Control row) => row.GetSelfAndVisualDescendantsOfType<TextBlock>()
        .Where(t => t.Text is not null).Select(t => t.Text!).ToList();
}

internal static class VisualTreeExtensions
{
    public static List<T> GetSelfAndVisualDescendantsOfType<T>(this Control root) where T : Control
        => global::Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualDescendants(root).OfType<T>().ToList();

    public static IEnumerable<T> GetVisualAncestorsOfType<T>(this Control control) where T : Control
        => global::Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(control).OfType<T>();
}
