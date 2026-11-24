using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Border, ScrollViewer, Expander, Grid and WrapPanel (#19): their content or children mount, update in place and keep a
/// child component's state; Border's two background sources; Expander's controlled state; Grid's tracks and cells.
/// </summary>
public class ContainerControlTests
{
    // A child component with its own state, to see that a container's update keeps it.
    private static Element Counter(RenderContext ctx)
    {
        var (n, setN) = ctx.UseState(0);
        return Button("count " + n, () => setN(n + 1));
    }

    [AvaloniaFact]
    public void Border_mounts_its_child_and_chrome_and_updates_them_in_place()
    {
        var radius = 4.0;
        var host = new TestHost(() => Border(TextBlock("inside")) with
        {
            CornerRadius = radius, BorderThickness = 2, BorderBrush = Brushes.Red, Padding = new Thickness(3),
        });
        var border = host.All<Border>()[0];
        Assert.Equal(["inside"], host.Texts());
        Assert.Equal((new CornerRadius(4), new Thickness(2), new Thickness(3)), (border.CornerRadius, border.BorderThickness, border.Padding));
        Assert.Same(Brushes.Red, border.BorderBrush);
        var text = host.All<TextBlock>()[0];

        radius = 8;
        host.Render();
        Assert.Same(border, host.All<Border>()[0]);
        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
    }

    [AvaloniaFact]
    public void Border_brush_set_back_to_null_returns_to_the_default()
    {
        var set = true;
        var host = new TestHost(() => set ? Border(null) with { BorderBrush = Brushes.Red } : Border(null));
        var border = host.All<Border>()[0];
        Assert.Same(Brushes.Red, border.BorderBrush);

        set = false;
        host.Render();
        Assert.Null(border.BorderBrush);
    }

    [AvaloniaFact]
    public void Border_background_and_the_background_modifier_are_written_as_one_value()
    {
        // The record's background wins; removing it shows the modifier's, and removing that clears it.
        IBrush? own = Brushes.Red, modifier = Brushes.Blue;
        var host = new TestHost(() =>
        {
            var b = Border(TextBlock("x")) with { Background = own };
            return modifier is null ? b : b.Background(modifier);
        });
        var border = host.All<Border>()[0];
        Assert.Same(Brushes.Red, border.Background);

        own = null;
        host.Render();
        Assert.Same(Brushes.Blue, border.Background);

        own = Brushes.Green;
        host.Render();
        Assert.Same(Brushes.Green, border.Background);

        modifier = null; // the modifier goes: the record's stays
        host.Render();
        Assert.Same(Brushes.Green, border.Background);

        own = null;
        host.Render();
        Assert.Null(border.Background);
        Assert.False(border.IsSet(global::Avalonia.Controls.Border.BackgroundProperty));
    }

    [AvaloniaFact]
    public void Border_child_component_keeps_its_state_when_the_border_updates()
    {
        var radius = 1.0;
        var host = new TestHost(() => Border(Component(Counter)) with { CornerRadius = radius });
        TestHost.Click(host.All<Button>()[0]);
        host.Flush();
        Assert.Equal("count 1", host.All<Button>()[0].Content);

        radius = 2;
        host.Render();
        Assert.Equal("count 1", host.All<Button>()[0].Content);
    }

    [AvaloniaFact]
    public void ScrollViewer_mounts_its_child_with_scroll_bars_and_swaps_the_child_in_place()
    {
        var wide = false;
        var host = new TestHost(() => (wide ? ScrollViewer(TextBlock("wide")) : ScrollViewer(VStack(TextBlock("tall"))))
            with { VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Visible });
        var scroll = host.All<ScrollViewer>()[0];
        Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility); // upstream's default
        Assert.Equal(ScrollBarVisibility.Visible, scroll.VerticalScrollBarVisibility);
        Assert.IsType<StackPanel>(scroll.Content);

        wide = true;
        host.Render();
        Assert.Same(scroll, host.All<ScrollViewer>()[0]);
        Assert.IsType<TextBlock>(scroll.Content);
        Assert.Equal(["wide"], host.Texts());
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility);
    }

    [AvaloniaFact]
    public void Expander_mounts_its_header_content_and_state_and_keeps_a_content_component_state()
    {
        var header = "More";
        var host = new TestHost(() => Expander(header, Component(Counter), true) with { ExpandDirection = ExpandDirection.Up });
        var expander = host.All<Expander>()[0];
        Assert.Equal(("More", true, ExpandDirection.Up), (expander.Header, expander.IsExpanded, expander.ExpandDirection));
        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        header = "Less";
        host.Render();
        Assert.Same(expander, host.All<Expander>()[0]);
        Assert.Equal("Less", expander.Header);
        Assert.Equal("count 1", host.All<Button>()[0].Content);
    }

    [AvaloniaFact]
    public void Expander_user_toggle_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (open, setOpen) = ctx.UseState(false);
            set = setOpen;
            return VStack(TextBlock("state " + open), Expander("More", TextBlock("body"), open, v => { reported.Add(v); setOpen(v); }));
        }));
        var expander = host.All<Expander>()[0];

        expander.IsExpanded = true; // as a click on the header does
        host.Flush();
        Assert.Equal([true], reported);
        Assert.Equal("state True", host.Texts()[0]);

        set!(false);
        host.Flush();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // Avalonia posts Collapsed after the change
        Assert.False(expander.IsExpanded);
        Assert.Equal([true], reported);

        expander.IsExpanded = true;
        Assert.Equal([true, true], reported);
    }

    [AvaloniaFact]
    public void Expander_with_no_value_keeps_what_the_user_set_across_renders()
    {
        var header = "a";
        var host = new TestHost(() => Expander(header, TextBlock("body")));
        var expander = host.All<Expander>()[0];
        expander.IsExpanded = true;

        header = "b";
        host.Render();
        Assert.True(expander.IsExpanded);
    }

    // With Factories imported, Grid.GetRow names the factory (as upstream's ScrollViewer remarks say of its own), so the
    // attached-property calls go through an alias.
    [AvaloniaFact]
    public void Grid_builds_its_tracks_and_places_each_child_in_its_cell()
    {
        var host = new TestHost(() => Grid(
            [GridSize.Auto, GridSize.Star(2).MinSize(50), GridSize.Px(100)],
            [GridSize.Star(), GridSize.Auto.MaxSize(40)],
            TextBlock("a"),
            TextBlock("b").Grid(row: 1, column: 1, columnSpan: 2)) with { RowSpacing = 4, ColumnSpacing = 6 });
        var grid = host.All<Grid>()[0];
        Assert.Equal([GridLength.Auto, new GridLength(2, GridUnitType.Star), new GridLength(100)],
            grid.ColumnDefinitions.Select(c => c.Width).ToArray());
        Assert.Equal(50, grid.ColumnDefinitions[1].MinWidth);
        Assert.Equal([new GridLength(1, GridUnitType.Star), GridLength.Auto], grid.RowDefinitions.Select(r => r.Height).ToArray());
        Assert.Equal(40, grid.RowDefinitions[1].MaxHeight);
        Assert.Equal((4.0, 6.0), (grid.RowSpacing, grid.ColumnSpacing));

        var (a, b) = (host.All<TextBlock>()[0], host.All<TextBlock>()[1]);
        Assert.Equal((0, 0, 1, 1), (AvaloniaGrid.GetRow(a), AvaloniaGrid.GetColumn(a), AvaloniaGrid.GetRowSpan(a), AvaloniaGrid.GetColumnSpan(a)));
        Assert.Equal((1, 1, 1, 2), (AvaloniaGrid.GetRow(b), AvaloniaGrid.GetColumn(b), AvaloniaGrid.GetRowSpan(b), AvaloniaGrid.GetColumnSpan(b)));
    }

    [AvaloniaFact]
    public void Grid_moves_a_child_between_cells_in_place_and_rebuilds_tracks_only_for_a_new_definition()
    {
        var definition = new GridDefinition(["*", "*"], ["Auto"]);
        var column = 1;
        var host = new TestHost(() => new GridElement(definition, [TextBlock("a").Grid(column: column)]));
        var grid = host.All<Grid>()[0];
        var text = host.All<TextBlock>()[0];
        var firstTrack = grid.ColumnDefinitions[0];
        Assert.Equal(1, AvaloniaGrid.GetColumn(text));

        column = 0; // the same definition: tracks kept, the cell rewritten
        host.Render();
        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Equal(0, AvaloniaGrid.GetColumn(text));
        Assert.Same(firstTrack, grid.ColumnDefinitions[0]);

        definition = new GridDefinition(["100", "*", "*"], ["Auto"]);
        host.Render();
        Assert.Equal(3, grid.ColumnDefinitions.Count);
        Assert.Equal(new GridLength(100), grid.ColumnDefinitions[0].Width);
    }

    [AvaloniaFact]
    public void Grid_child_that_loses_its_cell_returns_to_the_first_one()
    {
        var placed = true;
        var host = new TestHost(() => Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star(), GridSize.Star()],
            placed ? TextBlock("a").Grid(row: 1, column: 1, rowSpan: 2) : TextBlock("a")));
        var text = host.All<TextBlock>()[0];
        Assert.Equal((1, 1, 2), (AvaloniaGrid.GetRow(text), AvaloniaGrid.GetColumn(text), AvaloniaGrid.GetRowSpan(text)));

        placed = false;
        host.Render();
        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Equal((0, 0, 1), (AvaloniaGrid.GetRow(text), AvaloniaGrid.GetColumn(text), AvaloniaGrid.GetRowSpan(text)));
    }

    [AvaloniaFact]
    public void WrapPanel_mounts_its_children_and_layout_and_updates_them_in_place()
    {
        var spacing = 4.0;
        var host = new TestHost(() => WrapPanel(TextBlock("a"), TextBlock("b")) with
        {
            Orientation = Orientation.Vertical, ItemSpacing = spacing, LineSpacing = 2, ItemWidth = 30,
        });
        var panel = host.All<WrapPanel>()[0];
        Assert.Equal(["a", "b"], host.Texts());
        Assert.Equal((Orientation.Vertical, 4.0, 2.0, 30.0), (panel.Orientation, panel.ItemSpacing, panel.LineSpacing, panel.ItemWidth));
        Assert.True(double.IsNaN(panel.ItemHeight));
        var a = host.All<TextBlock>()[0];

        spacing = 8;
        host.Render();
        Assert.Same(panel, host.All<WrapPanel>()[0]);
        Assert.Same(a, host.All<TextBlock>()[0]);
        Assert.Equal(8, panel.ItemSpacing);
    }
}
