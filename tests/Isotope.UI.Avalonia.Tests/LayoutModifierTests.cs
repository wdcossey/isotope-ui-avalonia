using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using Xunit;
using static Isotope.UI.Factories;
using AvBorder = Avalonia.Controls.Border;
using AvCornerRadius = Avalonia.CornerRadius;
using AvGrid = Avalonia.Controls.Grid;
using AvTextBlock = Avalonia.Controls.TextBlock;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Layout modifiers (#49): upstream's Padding, WithBorder, Center, CornerRadius, BorderBrush, BorderThickness, size
/// limits, content alignment and IsVisible, with Avalonia's value types. Each is checked on mount, on an update that
/// changes only it, and when it goes (the control's own value comes back). A stack, grid or wrap panel, which have no
/// padding, border or corners on Avalonia, is mounted inside a border that has them (PanelChrome).
/// </summary>
public sealed class LayoutModifierTests
{
    // Mounts first(), re-renders with second(), then with plain(); reads the value from the one control each time.
    private static void AssertSetChangedAndCleared<TControl, T>(Func<Element> first, Func<Element> second, Func<Element> plain,
        Func<TControl, T> read, T v1, T v2) where TControl : Control
    {
        var step = 0;
        var host = new TestHost(() => step switch { 0 => first(), 1 => second(), _ => plain() });
        var control = host.All<TControl>()[0];
        Assert.Equal(v1, read(control));

        step = 1;
        host.Render();
        Assert.Same(control, host.All<TControl>()[0]);
        Assert.Equal(v2, read(control));

        step = 2;
        host.Render();
        var fresh = new TestHost(plain).All<TControl>()[0];
        Assert.Equal(read(fresh), read(control));
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ── The overloads ───────────────────────────────────────────────────

    [Fact]
    public void Padding_and_BorderThickness_overloads_take_horizontal_first_and_name_their_sides()
    {
        Thickness? P(Element e) => e.Modifiers?.Padding;
        Thickness? B(Element e) => e.Modifiers?.BorderThickness;
        var t = TextBlock("a");
        Assert.Equal(new Thickness(4), P(t.Padding(4)));
        Assert.Equal(new Thickness(1, 2, 1, 2), P(t.Padding(1, 2)));
        Assert.Equal(new Thickness(0, 3, 0, 0), P(t.Padding(top: 3)));
        Assert.Equal(new Thickness(1, 2, 3, 4), P(t.Padding(1, 2, 3, 4)));
        Assert.Equal(new Thickness(5, 6, 7, 8), P(t.Padding(new Thickness(5, 6, 7, 8))));
        Assert.Equal(new Thickness(4), B(t.BorderThickness(4)));
        Assert.Equal(new Thickness(1, 2, 1, 2), B(t.BorderThickness(1, 2)));
        Assert.Equal(new Thickness(0, 0, 0, 3), B(t.BorderThickness(bottom: 3)));
        Assert.Equal(new Thickness(1, 2, 3, 4), B(t.BorderThickness(1, 2, 3, 4)));
        Assert.Equal(new Thickness(5, 6, 7, 8), B(t.BorderThickness(new Thickness(5, 6, 7, 8))));
    }

    [Fact]
    public void CornerRadius_WithBorder_and_Center_overloads()
    {
        var t = TextBlock("a");
        Assert.Equal(new AvCornerRadius(4), t.CornerRadius(4).Modifiers?.CornerRadius);
        Assert.Equal(new AvCornerRadius(1, 2, 3, 4), t.CornerRadius(1, 2, 3, 4).Modifiers?.CornerRadius);
        Assert.Equal(new AvCornerRadius(5, 6, 7, 8), t.CornerRadius(new AvCornerRadius(5, 6, 7, 8)).Modifiers?.CornerRadius);

        var border = t.WithBorder("Red").Modifiers!;
        Assert.Equal((Colors.Red, new Thickness(1)), (ColorOf(border.BorderBrush), border.BorderThickness));
        var thick = t.WithBorder(Brushes.Blue, 3).Modifiers!;
        Assert.Equal((Colors.Blue, new Thickness(3)), (ColorOf(thick.BorderBrush), thick.BorderThickness));

        var centred = t.Margin(2).Center().Modifiers!;
        Assert.Equal((HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(2)),
            (centred.HorizontalAlignment, centred.VerticalAlignment, centred.Margin));
    }

    [Fact]
    public void Spacing_and_FlexPadding_set_the_records_members()
    {
        Assert.Equal(20, VStack().Spacing(20).Spacing);
        Assert.Equal(new Thickness(4), FlexRow().FlexPadding(4).FlexPadding);
        Assert.Equal(new Thickness(1, 2, 1, 2), FlexRow().FlexPadding(1, 2).FlexPadding);
        Assert.Equal(new Thickness(1, 2, 3, 4), FlexRow().FlexPadding(1, 2, 3, 4).FlexPadding);
    }

    // ── Written where Avalonia has the property ─────────────────────────

    [AvaloniaFact]
    public void Padding_on_a_button_and_a_text_block()
    {
        AssertSetChangedAndCleared<Button, Thickness>(() => Button("Go").Padding(4), () => Button("Go").Padding(1, 2), () => Button("Go"),
            c => c.Padding, new Thickness(4), new Thickness(1, 2, 1, 2));
        AssertSetChangedAndCleared<AvTextBlock, Thickness>(() => TextBlock("a").Padding(4), () => TextBlock("a").Padding(top: 2), () => TextBlock("a"),
            c => c.Padding, new Thickness(4), new Thickness(0, 2, 0, 0));
    }

    [AvaloniaFact]
    public void BorderBrush_and_BorderThickness_on_a_text_box()
    {
        AssertSetChangedAndCleared<TextBox, Color?>(() => TextBox("x").BorderBrush("Red"), () => TextBox("x").BorderBrush(Brushes.Blue),
            () => TextBox("x"), c => (c.BorderBrush as ISolidColorBrush)?.Color, Colors.Red, Colors.Blue);
        AssertSetChangedAndCleared<TextBox, Thickness>(() => TextBox("x").BorderThickness(3), () => TextBox("x").BorderThickness(1, 2),
            () => TextBox("x"), c => c.BorderThickness, new Thickness(3), new Thickness(1, 2, 1, 2));
    }

    [AvaloniaFact]
    public void WithBorder_on_a_button()
    {
        AssertSetChangedAndCleared<Button, (Color?, Thickness)>(() => Button("Go").WithBorder("Red", 2), () => Button("Go").WithBorder(Brushes.Blue),
            () => Button("Go"), c => ((c.BorderBrush as ISolidColorBrush)?.Color, c.BorderThickness),
            (Colors.Red, new Thickness(2)), (Colors.Blue, new Thickness(1)));
    }

    [AvaloniaFact]
    public void CornerRadius_on_a_button() =>
        AssertSetChangedAndCleared<Button, AvCornerRadius>(() => Button("Go").CornerRadius(6), () => Button("Go").CornerRadius(1, 2, 3, 4),
            () => Button("Go"), c => c.CornerRadius, new AvCornerRadius(6), new AvCornerRadius(1, 2, 3, 4));

    [AvaloniaFact]
    public void MinWidth_MinHeight_MaxWidth_and_MaxHeight_on_a_text_block()
    {
        AssertSetChangedAndCleared<AvTextBlock, double>(() => TextBlock("a").MinWidth(10), () => TextBlock("a").MinWidth(20), () => TextBlock("a"),
            c => c.MinWidth, 10.0, 20.0);
        AssertSetChangedAndCleared<AvTextBlock, double>(() => TextBlock("a").MinHeight(10), () => TextBlock("a").MinHeight(20), () => TextBlock("a"),
            c => c.MinHeight, 10.0, 20.0);
        AssertSetChangedAndCleared<AvTextBlock, double>(() => TextBlock("a").MaxWidth(100), () => TextBlock("a").MaxWidth(200), () => TextBlock("a"),
            c => c.MaxWidth, 100.0, 200.0);
        AssertSetChangedAndCleared<AvTextBlock, double>(() => TextBlock("a").MaxHeight(100), () => TextBlock("a").MaxHeight(200), () => TextBlock("a"),
            c => c.MaxHeight, 100.0, 200.0);
    }

    [AvaloniaFact]
    public void Center_centres_both_ways() =>
        AssertSetChangedAndCleared<AvTextBlock, (HorizontalAlignment, VerticalAlignment)>(() => TextBlock("a").Center(),
            () => TextBlock("a").Center().HAlign(HorizontalAlignment.Right), () => TextBlock("a"),
            c => (c.HorizontalAlignment, c.VerticalAlignment),
            (HorizontalAlignment.Center, VerticalAlignment.Center), (HorizontalAlignment.Right, VerticalAlignment.Center));

    [AvaloniaFact]
    public void Content_alignment_on_a_button_and_a_text_box()
    {
        AssertSetChangedAndCleared<Button, HorizontalAlignment>(() => Button("Go").HorizontalContentAlignment(HorizontalAlignment.Left),
            () => Button("Go").HorizontalContentAlignment(HorizontalAlignment.Right), () => Button("Go"),
            c => c.HorizontalContentAlignment, HorizontalAlignment.Left, HorizontalAlignment.Right);
        AssertSetChangedAndCleared<TextBox, VerticalAlignment>(() => TextBox("x").VerticalContentAlignment(VerticalAlignment.Top),
            () => TextBox("x").VerticalContentAlignment(VerticalAlignment.Bottom), () => TextBox("x"),
            c => c.VerticalContentAlignment, VerticalAlignment.Top, VerticalAlignment.Bottom);
    }

    [AvaloniaFact]
    public void Content_alignment_does_nothing_on_a_control_without_it()
    {
        var block = new TestHost(() => TextBlock("a").HorizontalContentAlignment(HorizontalAlignment.Right)
            .VerticalContentAlignment(VerticalAlignment.Bottom)).All<AvTextBlock>()[0];
        Assert.False(block.IsSet(ContentControl.HorizontalContentAlignmentProperty));
        Assert.False(block.IsSet(ContentControl.VerticalContentAlignmentProperty));
    }

    [AvaloniaFact]
    public void IsVisible_hides_and_shows() =>
        AssertSetChangedAndCleared<Button, bool>(() => Button("Go").IsVisible(false), () => Button("Go").IsVisible(true), () => Button("Go"),
            c => c.IsVisible, false, true);

    [AvaloniaFact]
    public void A_wrapper_s_layout_modifiers_merge_with_the_inner_element_s()
    {
        var inner = Button("Go").Margin(4);
        var wrapped = new ModifiedElement(inner, new ElementModifiers
        {
            Padding = new Thickness(1), BorderBrush = Brushes.Red, BorderThickness = new Thickness(2), CornerRadius = new AvCornerRadius(3),
            MinWidth = 10, MinHeight = 11, MaxWidth = 100, MaxHeight = 101,
            HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Bottom, IsVisible = false,
        });
        var button = new TestHost(() => wrapped).All<Button>()[0];
        Assert.Equal((new Thickness(1), Colors.Red, new Thickness(2), new AvCornerRadius(3)),
            (button.Padding, ColorOf(button.BorderBrush), button.BorderThickness, button.CornerRadius));
        Assert.Equal((10.0, 11.0, 100.0, 101.0), (button.MinWidth, button.MinHeight, button.MaxWidth, button.MaxHeight));
        Assert.Equal((HorizontalAlignment.Right, VerticalAlignment.Bottom, false),
            (button.HorizontalContentAlignment, button.VerticalContentAlignment, button.IsVisible));
        Assert.Equal(new Thickness(4), button.Margin);
    }

    // ── A Border element: its members and the modifiers ─────────────────

    [AvaloniaFact]
    public void The_modifiers_reach_a_Border_element()
    {
        AssertSetChangedAndCleared<AvBorder, Thickness>(() => Border(TextBlock("a")).Padding(4), () => Border(TextBlock("a")).Padding(1, 2),
            () => Border(TextBlock("a")), c => c.Padding, new Thickness(4), new Thickness(1, 2, 1, 2));
        AssertSetChangedAndCleared<AvBorder, (Color?, Thickness)>(() => Border(TextBlock("a")).WithBorder("Red", 2),
            () => Border(TextBlock("a")).WithBorder("Blue", 3), () => Border(TextBlock("a")),
            c => ((c.BorderBrush as ISolidColorBrush)?.Color, c.BorderThickness), (Colors.Red, new Thickness(2)), (Colors.Blue, new Thickness(3)));
        AssertSetChangedAndCleared<AvBorder, AvCornerRadius>(() => Border(TextBlock("a")).CornerRadius(4), () => Border(TextBlock("a")).CornerRadius(8),
            () => Border(TextBlock("a")), c => c.CornerRadius, new AvCornerRadius(4), new AvCornerRadius(8));
    }

    [AvaloniaFact]
    public void On_a_Border_element_its_members_win_over_the_modifiers_and_the_modifiers_return_when_the_members_go()
    {
        var members = true;
        var host = new TestHost(() =>
        {
            var border = members
                ? Border(TextBlock("a")) with { Padding = new Thickness(9), BorderBrush = Brushes.Green, BorderThickness = 5, CornerRadius = 7 }
                : Border(TextBlock("a"));
            return border.Padding(1).WithBorder("Red", 2).CornerRadius(3);
        });
        var b = host.All<AvBorder>()[0];
        Assert.Equal((new Thickness(9), Colors.Green, new Thickness(5), new AvCornerRadius(7)),
            (b.Padding, ColorOf(b.BorderBrush), b.BorderThickness, b.CornerRadius));

        members = false;
        host.Render();
        Assert.Same(b, host.All<AvBorder>()[0]);
        Assert.Equal((new Thickness(1), Colors.Red, new Thickness(2), new AvCornerRadius(3)),
            (b.Padding, ColorOf(b.BorderBrush), b.BorderThickness, b.CornerRadius));
    }

    [AvaloniaFact]
    public void A_Border_element_s_members_set_back_to_null_clear_the_control()
    {
        var members = true;
        var host = new TestHost(() => members
            ? Border(TextBlock("a")) with { Padding = new Thickness(9), BorderBrush = Brushes.Green, BorderThickness = 5, CornerRadius = 7 }
            : Border(TextBlock("a")));
        var b = host.All<AvBorder>()[0];
        members = false;
        host.Render();
        Assert.False(b.IsSet(Decorator.PaddingProperty));
        Assert.False(b.IsSet(AvBorder.BorderBrushProperty));
        Assert.False(b.IsSet(AvBorder.BorderThicknessProperty));
        Assert.False(b.IsSet(AvBorder.CornerRadiusProperty));
    }

    // ── Panels: inside a border that has them ───────────────────────────

    private static PanelChrome ChromeOf(Control panel) => Assert.IsType<PanelChrome>(panel.Parent);

    [AvaloniaFact]
    public void A_padded_stack_is_mounted_inside_a_border_that_holds_its_modifiers()
    {
        var host = new TestHost(() => VStack(TextBlock("a")).Padding(10).WithBorder("Red", 2).CornerRadius(6)
            .Background("Blue").Margin(3).Width(120).HAlign(HorizontalAlignment.Left).Opacity(0.5));
        var panel = host.All<StackPanel>()[0];
        var chrome = ChromeOf(panel);
        Assert.Same(chrome, host.Root.Child);
        Assert.Equal((new Thickness(10), Colors.Red, new Thickness(2), new AvCornerRadius(6), Colors.Blue),
            (chrome.Padding, ColorOf(chrome.BorderBrush), chrome.BorderThickness, chrome.CornerRadius, ColorOf(chrome.Background)));
        Assert.Equal((new Thickness(3), 120.0, HorizontalAlignment.Left, 0.5), (chrome.Margin, chrome.Width, chrome.HorizontalAlignment, chrome.Opacity));
        // The panel keeps its defaults: the border is the element's box.
        Assert.False(panel.IsSet(Layoutable.MarginProperty));
        Assert.False(panel.IsSet(Layoutable.WidthProperty));
        Assert.False(panel.IsSet(Panel.BackgroundProperty));
        Assert.False(panel.IsSet(Visual.OpacityProperty));
        Assert.Equal("a", Assert.IsType<AvTextBlock>(Assert.Single(panel.Children)).Text);
    }

    [AvaloniaFact]
    public void The_panel_is_laid_out_inside_the_padding_and_the_border()
    {
        var host = new TestHost(() => VStack(TextBlock("a").Height(20)).Padding(10).WithBorder("Red", 2));
        host.Root.Measure(new Size(200, 200));
        host.Root.Arrange(new Rect(0, 0, 200, 200));
        var panel = host.All<StackPanel>()[0];
        Assert.Equal(new Rect(12, 12, 176, 176), panel.Bounds);
    }

    [AvaloniaFact]
    public void A_grid_and_a_wrap_panel_get_one_too_and_a_stack_without_them_doesn_t()
    {
        var host = new TestHost(() => VStack(
            Grid([GridSize.Star()], [GridSize.Auto], TextBlock("g")).Padding(4),
            WrapPanel(TextBlock("w")).CornerRadius(4),
            HStack(TextBlock("h")).Background("Red").Margin(2)));
        Assert.Equal(new Thickness(4), ChromeOf(host.All<AvGrid>()[0]).Padding);
        Assert.Equal(new AvCornerRadius(4), ChromeOf(host.All<WrapPanel>()[0]).CornerRadius);
        var plain = host.All<StackPanel>()[1];
        Assert.IsType<StackPanel>(plain.Parent);
        Assert.Equal(Colors.Red, ColorOf(plain.Background));
    }

    [AvaloniaFact]
    public void A_flex_panel_ignores_them_as_upstream_s()
    {
        var host = new TestHost(() => FlexRow(TextBlock("a")).Padding(8).WithBorder("Red").CornerRadius(4));
        var flex = host.All<FlexPanel>()[0];
        Assert.Same(flex, host.Root.Child);
        Assert.Equal(default, flex.FlexPadding);
    }

    [AvaloniaFact]
    public void A_changed_padding_updates_the_same_border_and_panel_and_its_children_keep_their_state()
    {
        var padding = 4.0;
        Action<int>? setCount = null;
        var counter = Component(ctx =>
        {
            var (count, set) = ctx.UseState(0);
            setCount = set;
            return TextBlock($"count {count}");
        });
        var host = new TestHost(() => VStack(counter).Padding(padding).WithBorder("Red"));
        var panel = host.All<StackPanel>()[0];
        var chrome = ChromeOf(panel);
        setCount!(3);
        host.Flush();

        padding = 8;
        host.Render();
        Assert.Same(panel, host.All<StackPanel>()[0]);
        Assert.Same(chrome, panel.Parent);
        Assert.Equal(new Thickness(8), chrome.Padding);
        Assert.Equal(["count 3"], host.Texts());
    }

    [AvaloniaFact]
    public void Padding_that_comes_or_goes_mounts_the_panel_afresh()
    {
        var padded = false;
        var cleanups = 0;
        var child = Component(ctx =>
        {
            ctx.UseEffect(() => () => cleanups++, Array.Empty<object>());
            return TextBlock("a");
        });
        var host = new TestHost(() => padded ? VStack(child).Padding(4) : VStack(child));
        var first = host.All<StackPanel>()[0];
        Assert.Same(first, host.Root.Child);

        padded = true;
        host.Render();
        var second = host.All<StackPanel>()[0];
        Assert.NotSame(first, second);
        Assert.Same(ChromeOf(second), host.Root.Child);
        Assert.Equal(1, cleanups);

        padded = false;
        host.Render();
        var third = host.All<StackPanel>()[0];
        Assert.Same(third, host.Root.Child);
        Assert.Equal(2, cleanups);
    }

    [AvaloniaFact]
    public void Unmounting_a_padded_stack_unmounts_what_is_inside_it()
    {
        var shown = true;
        var cleanups = 0;
        var child = Component(ctx =>
        {
            ctx.UseEffect(() => () => cleanups++, Array.Empty<object>());
            return TextBlock("a");
        });
        var host = new TestHost(() => VStack(shown ? VStack(child).Padding(4) : TextBlock("gone")));
        shown = false;
        host.Render();
        Assert.Equal(1, cleanups);
        Assert.Equal(["gone"], host.Texts());
    }

    [AvaloniaFact]
    public void Keyed_padded_stacks_keep_their_controls_when_they_move()
    {
        string[] order = ["a", "b", "c"];
        var host = new TestHost(() => VStack(order.Select(k => (Element)VStack(TextBlock(k)).Padding(2).WithKey(k)).ToArray()));
        var outer = host.All<StackPanel>()[0];
        Dictionary<string, Control> Rows() => outer.Children.ToDictionary(c => ((AvTextBlock)((StackPanel)((PanelChrome)c).Child!).Children[0]).Text!, c => c);
        var before = Rows();

        order = ["c", "a", "b"];
        host.Render();
        var after = Rows();
        foreach (var k in order) Assert.Same(before[k], after[k]);
        Assert.Equal(["c", "a", "b"], host.Texts());
    }

    [AvaloniaFact]
    public void The_border_and_the_panel_both_carry_the_element_after_an_update_and_a_skip()
    {
        // The engine reads the element a slot's control represents (a keyed diff reads its key), and a handler reads its
        // panel's: both are the render's element, and a skip leaves them as they were.
        var padding = 2.0;
        StackElement? latest = null;
        Element[] children = [TextBlock("a")]; // one array, so an unchanged stack compares equal and is skipped
        var host = new TestHost(() => VStack(latest = new StackElement(Orientation.Vertical, children).Padding(padding).WithKey("k")));
        var panel = host.All<StackPanel>()[1];
        var chrome = ChromeOf(panel);
        void AssertTagged()
        {
            Assert.Same(latest, Reconciler.GetMountedElement(chrome));
            Assert.Same(latest, Reconciler.GetMountedElement(panel));
        }
        Assert.Equal("k", Reconciler.GetMountedElement(chrome)?.Key);
        AssertTagged();

        padding = 3;
        host.Render();
        AssertTagged();

        host.Render();
        Assert.Same(Reconciler.GetMountedElement(panel), Reconciler.GetMountedElement(chrome));
    }

    [AvaloniaFact]
    public void A_padded_stack_remounted_for_a_foreign_control_gets_one_border()
    {
        // The handler remounts an element paired with a control of another type (upstream's #919 guard): the mount puts
        // the panel in its border, and the update doesn't put that border in a second one.
        var reconciler = new Reconciler();
        Element Padded() => VStack(TextBlock("a")).Padding(4).WithBorder("Red", 2);
        var foreign = new PanelChrome { Child = new AvTextBlock() };
        var chrome = Assert.IsType<PanelChrome>(reconciler.Reconcile(Padded(), Padded(), foreign, () => { }));
        Assert.IsType<StackPanel>(chrome.Child);
        Assert.Equal((new Thickness(4), new Thickness(2)), (chrome.Padding, chrome.BorderThickness));
    }

    [AvaloniaFact]
    public void A_skipped_component_root_refreshes_the_element_on_the_border_and_its_panel()
    {
        Element[] children = [TextBlock("a")]; // one array, so the stack compares equal and is skipped
        Action<int>? set = null;
        StackElement? latest = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (_, s) = ctx.UseState(0);
            set = s;
            return latest = new StackElement(Orientation.Vertical, children).Padding(4);
        }));
        var panel = host.All<StackPanel>()[0];
        var chrome = ChromeOf(panel);
        var skipped = host.Reconciler.DebugElementsSkipped;

        set!(1);
        host.Flush();
        Assert.Equal(skipped + 1, host.Reconciler.DebugElementsSkipped);
        Assert.Same(latest, Reconciler.GetMountedElement(chrome));
        Assert.Same(latest, Reconciler.GetMountedElement(panel));
    }

    [AvaloniaFact]
    public void A_theme_token_border_on_a_stack_goes_on_its_border()
    {
        var host = new TestHost(() => VStack(
            VStack(TextBlock("a")).WithBorder(Theme.CardStroke, 2),
            HStack(TextBlock("b")).BorderBrush(Theme.CardStroke)));
        var withBorder = ChromeOf(host.All<StackPanel>()[1]);
        Assert.Equal(new Thickness(2), withBorder.BorderThickness);
        Assert.Equal(["BorderBrush"], Reconciler.BoundThemeProperties(withBorder));
        // A token brush alone puts the panel in a border too, as a brush modifier does.
        Assert.Equal(["BorderBrush"], Reconciler.BoundThemeProperties(ChromeOf(host.All<StackPanel>()[2])));
    }

    [AvaloniaFact]
    public void A_padded_child_s_flex_and_grid_data_go_on_its_border()
    {
        var host = new TestHost(() => VStack(
            FlexRow(VStack(TextBlock("f")).Padding(2).Flex(grow: 1)),
            Grid([GridSize.Star(), GridSize.Star()], [GridSize.Auto], VStack(TextBlock("g")).Padding(2).Grid(row: 0, column: 1))));
        var flexChild = ChromeOf(host.All<StackPanel>()[1]);
        Assert.Equal(1, FlexPanel.GetGrow(flexChild));
        var gridChild = ChromeOf(host.All<StackPanel>()[2]);
        Assert.Equal(1, AvGrid.GetColumn(gridChild));
    }

    [AvaloniaFact]
    public void An_unchanged_padded_stack_is_skipped()
    {
        // One children array and one setter array, so the stack compares equal; the setter runs on each update.
        Element[] children = [TextBlock("a")];
        var updates = 0;
        Action<StackPanel>[] setters = [_ => updates++];
        var padding = 4.0;
        var host = new TestHost(() => VStack(new StackElement(Orientation.Vertical, children) { Setters = setters }
            .Padding(padding).WithBorder("Red").CornerRadius(2)));
        var chrome = ChromeOf(host.All<StackPanel>()[1]);
        var before = updates;
        host.Render();
        Assert.Equal(before, updates);
        Assert.Same(chrome, ChromeOf(host.All<StackPanel>()[1]));

        padding = 5; // a change is an update
        host.Render();
        Assert.Equal(before + 1, updates);
    }

    [AvaloniaFact]
    public void The_guide_s_card_is_a_bordered_padded_stack()
    {
        var host = new TestHost(() => LayoutModifierExamples.Card("Title", "Body", selected: true));
        var chrome = ChromeOf(host.All<StackPanel>()[0]);
        Assert.Equal((new Thickness(12), Colors.DodgerBlue, new Thickness(2), new AvCornerRadius(8), 320.0),
            (chrome.Padding, ColorOf(chrome.BorderBrush), chrome.BorderThickness, chrome.CornerRadius, chrome.MaxWidth));
        Assert.Equal(["Title", "Body"], host.Texts());
    }
}
