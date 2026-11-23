using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Isotope.UI.Layout;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The library's FlexPanel (Spike D's port, #2) in an Avalonia layout pass, in a headless window. Expected values are the CSS Flexbox results,
/// the same way upstream's FlexPanelCssBehaviorFixtures derive them (those run in a WinUI app and can't be linked).
/// </summary>
public sealed class FlexPanelTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Host(Control content, double width, double height)
    {
        var w = new Window { Width = width, Height = height, Content = content, SizeToContent = SizeToContent.Manual };
        _windows.Add(w);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static Border Box(double w = double.NaN, double h = double.NaN, double grow = 0)
    {
        var b = new Border { Width = w, Height = h, Background = Brushes.SteelBlue };
        FlexPanel.SetGrow(b, grow);
        return b;
    }

    [AvaloniaFact]
    public void Row_GrowChild_TakesRemainingWidth()
    {
        Border a = Box(100, 40), b = Box(h: 40, grow: 1), c = Box(50, 40);
        var panel = new FlexPanel { Children = { a, b, c } };
        Host(panel, 400, 300);
        Assert.Equal(0, a.Bounds.X);
        Assert.Equal(100, b.Bounds.X);
        Assert.Equal(250, b.Bounds.Width);
        Assert.Equal(350, c.Bounds.X);
    }

    [AvaloniaFact]
    public void Column_HeaderBodyFooter_FillsWindowHeight()
    {
        Border header = Box(h: 50), body = Box(grow: 1), footer = Box(h: 30);
        var panel = new FlexPanel { Direction = FlexDirection.Column, Children = { header, body, footer } };
        Host(panel, 300, 500);
        // Measure itself must ask for the whole slot (VerticalAlignment.Stretch + definite offer), not just
        // the content height; Arrange alone would hide a wrong DesiredSize here.
        Assert.Equal(500, panel.DesiredSize.Height);
        Assert.Equal(50, body.Bounds.Y);
        Assert.Equal(420, body.Bounds.Height);
        Assert.Equal(470, footer.Bounds.Y);
        Assert.Equal(300, body.Bounds.Width); // align-items: stretch
    }

    [AvaloniaFact]
    public void Wrap_WithGaps_BreaksLines()
    {
        var boxes = Enumerable.Range(0, 5).Select(_ => Box(100, 40)).ToArray();
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, ColumnGap = 10, RowGap = 10, HorizontalAlignment = HorizontalAlignment.Left, Width = 250 };
        foreach (var b in boxes) panel.Children.Add(b);
        Host(panel, 400, 400);
        Assert.Equal(new Point(110, 0), boxes[1].Bounds.Position);
        Assert.Equal(new Point(0, 50), boxes[2].Bounds.Position);
        Assert.Equal(new Point(0, 100), boxes[4].Bounds.Position);
    }

    [AvaloniaFact]
    public void InvisibleChild_TakesNoSpaceAndNoGap()
    {
        Border a = Box(100, 40), b = Box(100, 40), c = Box(100, 40);
        b.IsVisible = false; // CSS display:none
        var panel = new FlexPanel { ColumnGap = 10, Children = { a, b, c } };
        Host(panel, 400, 300);
        Assert.Equal(110, c.Bounds.X);
    }

    [AvaloniaFact]
    public void ChangingAttachedGrow_RelaysOut()
    {
        // b has a flex basis, not an explicit Width: an explicit Width wins at Arrange (Avalonia and WinUI
        // alike), so Yoga's grown slot would not show in Bounds.
        Border a = Box(100, 40), b = Box(h: 40);
        FlexPanel.SetBasis(b, 100);
        var panel = new FlexPanel { Children = { a, b } };
        Host(panel, 400, 300);
        Assert.Equal(100, b.Bounds.Width);

        FlexPanel.SetGrow(b, 1); // must drop the panel's attached-property cache and invalidate measure
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(300, b.Bounds.Width);
    }

    [AvaloniaFact]
    public void ChildMargin_IsHonoured()
    {
        // Asymmetric margin and Left/Top alignment, so a wrong arrange rect can't be hidden by centring.
        Border a = Box(100, 40), b = Box(100, 40);
        a.Margin = new Thickness(10, 5, 20, 0);
        a.HorizontalAlignment = HorizontalAlignment.Left;
        a.VerticalAlignment = VerticalAlignment.Top;
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexStart, Children = { a, b } };
        Host(panel, 400, 300);
        Assert.Equal(new Rect(10, 5, 100, 40), a.Bounds);
        Assert.Equal(130, b.Bounds.X);
    }

    [AvaloniaFact]
    public void JustifySpaceBetween_DistributesFreeSpace()
    {
        Border a = Box(50, 40), b = Box(50, 40), c = Box(50, 40);
        var panel = new FlexPanel { JustifyContent = FlexJustify.SpaceBetween, Children = { a, b, c } };
        Host(panel, 350, 300);
        Assert.Equal(0, a.Bounds.X);
        Assert.Equal(150, b.Bounds.X);
        Assert.Equal(300, c.Bounds.X);
    }

    [AvaloniaFact]
    public void Column_OfText_MatchesStackPanel()
    {
        // Upstream's CssStack_* fixtures: where CSS and StackPanel agree, FlexPanel must match StackPanel.
        static TextBlock T(string s) => new() { Text = s, TextWrapping = TextWrapping.Wrap };
        var flexKids = new[] { T("Short"), T("A longer line of text that wraps at this width more than once, probably twice."), T("End") };
        var stackKids = new[] { T("Short"), T("A longer line of text that wraps at this width more than once, probably twice."), T("End") };
        var flex = new FlexPanel { Direction = FlexDirection.Column, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        foreach (var k in flexKids) flex.Children.Add(k);
        var stack = new StackPanel { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        foreach (var k in stackKids) stack.Children.Add(k);
        Host(new StackPanel { Orientation = Orientation.Horizontal, Children = { flex, stack } }, 500, 400);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(stackKids[i].Bounds.Y, flexKids[i].Bounds.Y, 1.5);
            Assert.Equal(stackKids[i].Bounds.Height, flexKids[i].Bounds.Height, 1.5);
        }
        Assert.True(flexKids[1].Bounds.Height > flexKids[0].Bounds.Height * 1.5, "the long line wraps");
    }

    [AvaloniaFact]
    public void AsItemsPanel_AttachedChange_RelaysOut()
    {
        // The panel's children are item containers whose logical parent is the ItemsControl.
        var items = new ItemsControl
        {
            ItemsPanel = new global::Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new FlexPanel()),
            ItemsSource = new[] { "a", "b" },
        };
        Host(items, 400, 300);
        var panel = Assert.IsType<FlexPanel>(items.ItemsPanelRoot);
        var second = panel.Children[1];
        FlexPanel.SetBasis(second, 50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, second.Bounds.Width, 0.5);

        FlexPanel.SetGrow(second, 1);
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.Bounds.Width > 300, $"grown width {second.Bounds.Width}");
    }

    [AvaloniaFact]
    public void ChangingVerticalAlignment_ReMeasures()
    {
        Border header = Box(h: 50), body = Box(grow: 1);
        var panel = new FlexPanel { Direction = FlexDirection.Column, VerticalAlignment = VerticalAlignment.Top, Children = { header, body } };
        Host(panel, 300, 500);
        Assert.Equal(50, panel.DesiredSize.Height); // fit-content

        // Avalonia 12 re-measures on an alignment change by itself (review pass 1 assumed it only re-arranges;
        // checked: this test passes without an AffectsMeasure registration). Kept as a regression guard.
        panel.VerticalAlignment = VerticalAlignment.Stretch; // now fills the slot
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(500, panel.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void NegativeMargins_LargerThanSlot_DoNotThrow()
    {
        var a = Box(h: 40);
        FlexPanel.SetBasis(a, 10);
        a.Margin = new Thickness(-20, 0, -20, 0);
        var panel = new FlexPanel { Children = { a } };
        var ex = Record.Exception(() => Host(panel, 400, 300));
        Assert.Null(ex);
    }

    [AvaloniaFact]
    public void NestedPanel_ChildHeightChange_PropagatesUp()
    {
        // The leaf is re-measured at its old exact slot when its Height changes, so its DesiredSize alone would not
        // tell the panels; FlexPanel registers Width/Height/Margin/IsVisible to re-measure itself.
        var leaf = Box(h: 40);
        var inner = new FlexPanel { Direction = FlexDirection.Column, Children = { leaf } };
        var filler = Box(h: 30);
        FlexPanel.SetGrow(filler, 1);
        var outer = new FlexPanel { Direction = FlexDirection.Column, Children = { inner, filler } };
        Host(outer, 300, 500);
        Assert.Equal(40, inner.Bounds.Height);

        leaf.Height = 60;              // invalidates the inner panel's measure first
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(60, inner.Bounds.Height);
        Assert.Equal(60 + (440 - 30) / 2.0, filler.Bounds.Y); // filler has Height 30: centred in its grown slot
    }

    [AvaloniaFact]
    public void Reattached_ToLargerHost_ArrangesChildren()
    {
        Border a = Box(h: 40), b = Box(h: 40);
        FlexPanel.SetGrow(a, 1);
        FlexPanel.SetGrow(b, 1);
        var panel = new FlexPanel { Children = { a, b } };
        var first = new Border { Child = panel };
        Host(first, 300, 200);
        Assert.Equal(150, a.Bounds.Width);

        first.Child = null;            // detach (clears the Yoga node cache)
        Dispatcher.UIThread.RunJobs();
        Host(new Border { Child = panel }, 500, 200);
        Assert.Equal(250, a.Bounds.Width);
        Assert.Equal(250, b.Bounds.X);
    }

    // #15: CSS's automatic minimum size (§4.5). Upstream reads min-content by measuring at 0, which Avalonia clamps to 0.
    // Natural widths come from twins hosted beside the panel (in a horizontal StackPanel, so unconstrained), since
    // the theme sets the font and a button's label is in its template.
    private Control[] HostWithTwins(FlexPanel panel, params Control[] twins)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in twins) row.Children.Add(t);
        Host(new StackPanel { Children = { panel, row } }, 800, 300);
        return twins;
    }

    [AvaloniaFact]
    public void Row_NonWrappingTexts_KeepMinContentWidthAndOverflow()
    {
        const string s = "Quarterly revenue";
        TextBlock a = new() { Text = s }, b = new() { Text = s };
        var panel = new FlexPanel { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Children = { a, b } };
        double natural = HostWithTwins(panel, new TextBlock { Text = s })[0].Bounds.Width;
        Assert.True(natural is > 50 and < 200, $"natural width {natural}");
        Assert.Equal(natural, a.Bounds.Width, 0.5);
        Assert.Equal(natural, b.Bounds.Width, 0.5);
        Assert.Equal(natural, b.Bounds.X, 0.5); // overflows the 100-wide row
    }

    [AvaloniaFact]
    public void Row_ButtonLabel_IsMinContent()
    {
        const string s = "A button with a long label";
        var button = new Button { Content = s };
        var rest = Box(h: 20, grow: 1);
        FlexPanel.SetBasis(rest, 400);
        var panel = new FlexPanel { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Children = { button, rest } };
        double natural = HostWithTwins(panel, new Button { Content = s })[0].Bounds.Width;
        Assert.True(natural > 100, $"natural width {natural}");
        Assert.Equal(natural, button.Bounds.Width, 0.5);
    }

    [AvaloniaFact]
    public void Row_WrappingText_ShrinksToItsLongestWord()
    {
        const string word = "Incomprehensibilities", s = "An " + word + " too";
        var text = new TextBlock { Text = s, TextWrapping = TextWrapping.WrapWithOverflow };
        var panel = new FlexPanel { Width = 20, HorizontalAlignment = HorizontalAlignment.Left, Children = { text } };
        var twins = HostWithTwins(panel, new TextBlock { Text = word }, new TextBlock { Text = s });
        double longest = twins[0].Bounds.Width, whole = twins[1].Bounds.Width;
        Assert.True(longest > 60, $"longest word {longest}");
        Assert.True(text.Bounds.Width >= longest - 0.5, $"width {text.Bounds.Width}, longest word {longest}");
        Assert.True(text.Bounds.Width < whole - 10, $"width {text.Bounds.Width}, whole line {whole}: still wraps");
    }

    [AvaloniaFact]
    public void Demo_RendersToPng()
    {
        var tiles = new FlexPanel { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8, FlexPadding = new Thickness(12) };
        var colors = new[] { Colors.SteelBlue, Colors.SeaGreen, Colors.Goldenrod, Colors.IndianRed, Colors.SlateBlue, Colors.Teal, Colors.DarkOrange };
        for (int i = 0; i < 7; i++)
        {
            var tile = new Border { Width = 90 + 20 * (i % 3), Height = 60, Background = new SolidColorBrush(colors[i]), CornerRadius = new CornerRadius(6),
                Child = new TextBlock { Text = "item " + (i + 1), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            if (i == 6) FlexPanel.SetGrow(tile, 1);
            tiles.Children.Add(tile);
        }
        var header = new Border { Height = 48, Background = Brushes.DimGray, Child = new TextBlock { Text = "FlexPanel on Avalonia 12 (upstream Yoga, unmodified)", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) } };
        var body = new Border { Background = Brushes.WhiteSmoke, Child = tiles };
        FlexPanel.SetGrow(body, 1);
        var footer = new Border { Height = 32, Background = Brushes.Gainsboro, Child = new TextBlock { Text = "header (48) / body (grow 1, wrapping row, gap 8) / footer (32)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) } };
        var root = new FlexPanel { Direction = FlexDirection.Column, Children = { header, body, footer } };
        var w = Host(root, 480, 320);
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var path = Path.Combine(AppContext.BaseDirectory, "flex-demo.png");
        #pragma warning disable CS0618 // Save(string) is obsolete in 12.x but still the plain PNG path
        frame!.Save(path);
#pragma warning restore CS0618
        Assert.True(new FileInfo(path).Length > 1000);
        Assert.Equal(320 - 48 - 32, body.Bounds.Height);
    }
}