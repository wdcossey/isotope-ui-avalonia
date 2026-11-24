using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>The factories of the #19 controls build the records they promise; GridSize parses and checks its tracks.</summary>
public class ControlFactoryTests
{
    [Fact]
    public void Toggle_factories_build_their_records()
    {
        Action<bool> changed = _ => { };
        var cb = CheckBox(true, changed, "Agree");
        Assert.Equal((Optional<bool?>.Of(true), "Agree", false), (cb.IsChecked, cb.Label, cb.IsThreeState));
        Assert.Same(changed, cb.OnIsCheckedChanged);
        Assert.False(CheckBox().IsChecked.HasValue);

        Action<bool?> states = _ => { };
        var three = ThreeStateCheckBox(null, states, "Some");
        Assert.Equal((true, Optional<bool?>.Of(null), (bool?)null, "Some"), (three.IsThreeState, three.IsChecked, three.CheckedState, three.Label));
        Assert.Same(states, three.OnCheckedStateChanged);

        var sw = ToggleSwitch(true, changed, "On", "Off", "Wi-Fi");
        Assert.Equal((Optional<bool>.Of(true), "On", "Off", "Wi-Fi"), (sw.IsOn, sw.OnContent, sw.OffContent, sw.Header));
        Assert.Same(changed, sw.OnIsOnChanged);

        var rb = RadioButton("A", true, changed, "g");
        Assert.Equal(("A", Optional<bool>.Of(true), "g"), (rb.Label, rb.IsChecked, rb.GroupName));
        Assert.Same(changed, rb.OnIsCheckedChanged);
    }

    [Fact]
    public void Selection_factories_build_their_records()
    {
        Action<int> changed = _ => { };
        string[] items = ["a", "b"];
        var combo = ComboBox(items, 1, changed);
        Assert.Same(items, combo.Items);
        Assert.Equal(Optional<int>.Of(1), combo.SelectedIndex);
        Assert.Same(changed, combo.OnSelectedIndexChanged);

        var list = ListBox(items, 0, changed);
        Assert.Same(items, list.Items);
        Assert.Equal(Optional<int>.Of(0), list.SelectedIndex);
        Assert.Same(changed, list.OnSelectedIndexChanged);
        Assert.False(ListBox(items).SelectedIndex.HasValue);
        Assert.Throws<ArgumentNullException>(() => ComboBox(null!));
        Assert.Throws<ArgumentNullException>(() => ListBox(null!));

        var day = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        Action<DateTimeOffset> dateChanged = _ => { };
        Assert.Equal(Optional<DateTimeOffset>.Of(day), DatePicker(day, dateChanged).Date);
        Assert.Same(dateChanged, DatePicker(day, dateChanged).OnDateChanged);
        Action<TimeSpan> timeChanged = _ => { };
        Assert.Equal(Optional<TimeSpan>.Of(TimeSpan.FromHours(1)), TimePicker(TimeSpan.FromHours(1), timeChanged).Time);
        Assert.Same(timeChanged, TimePicker(TimeSpan.FromHours(1), timeChanged).OnTimeChanged);
    }

    [Fact]
    public void Container_progress_and_image_factories_build_their_records()
    {
        Element child = TextBlock("x");
        Assert.Same(child, Border(child).Child);
        Assert.Null(Border(null).Child);
        Assert.Same(child, ScrollViewer(child).Child);
        Action<bool> expanded = _ => { };
        var ex = Expander("More", child, true, expanded);
        Assert.Equal(("More", Optional<bool>.Of(true)), (ex.Header, ex.IsExpanded));
        Assert.Same(child, ex.Content);
        Assert.Same(expanded, ex.OnIsExpandedChanged);
        Assert.Equal([child], WrapPanel(child, null).Children);

        var grid = Grid([GridSize.Star()], [GridSize.Auto], child, null);
        Assert.Equal([GridSize.Star()], grid.Definition.Columns);
        Assert.Equal([GridSize.Auto], grid.Definition.Rows);
        Assert.Equal([child], grid.Children);
        Assert.Throws<ArgumentNullException>(() => Grid(null!, []));
        Assert.Throws<ArgumentNullException>(() => Grid([], null!));
        Assert.Equal(new GridAttached(1, 2, 3, 4), child.Grid(1, 2, 3, 4).Attached![typeof(GridAttached)]);

        Assert.Equal((40.0, false), (Progress(40).Value, Progress(40).IsIndeterminate));
        Assert.Equal(((double?)null, true), (ProgressIndeterminate().Value, ProgressIndeterminate().IsIndeterminate));
        Assert.Equal("a.png", Image("a.png").Source);
    }

    [Fact]
    public void GridSize_parses_and_prints_tracks_and_rejects_bad_ones()
    {
        Assert.Equal([GridSize.Auto, GridSize.Star(), GridSize.Star(2.5), GridSize.Px(120)],
            new GridDefinition(["auto", "*", "2.5*", " 120 "], []).Columns);
        Assert.Equal(["Auto", "*", "2.5*", "120"], new[] { GridSize.Auto, GridSize.Star(), GridSize.Star(2.5), GridSize.Px(120) }.Select(s => s.ToString()));
        Assert.Equal((10.0, 20.0), (GridSize.Star().MinSize(10).MaxSize(20).Min, GridSize.Star().MinSize(10).MaxSize(20).Max));
        Assert.Equal(new GridLength(3, GridUnitType.Star), (GridLength)GridSize.Star(3));
        foreach (var bad in new[] { "", "x", "*x", "-1", "0*", "Infinity", "Infinity*", "NaN" })
            Assert.Throws<FormatException>(() => GridSize.Parse(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Px(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Star(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Auto.MinSize(-1));
    }

    [AvaloniaFact]
    public void Unchanged_elements_of_the_new_controls_are_skipped_as_upstream_skips_them()
    {
        // Each record upstream compares in ShallowEquals: re-rendering an equal element skips its update.
        string[] items = ["a"];
        var definition = new GridDefinition(["*"], ["*"]);
        Element[] none = [];
        Element child = TextBlock("c");
        Func<Element>[] equal =
        [
            () => CheckBox(true, label: "a"),
            () => ToggleSwitch(true, header: "h"),
            () => RadioButton("a", true, groupName: "g"),
            () => ComboBox(items, 0) with { PlaceholderText = "p" },
            () => Progress(5),
            () => Image("none.png"),
            () => Border(child) with { CornerRadius = 2 },
            () => new GridElement(definition, none) { RowSpacing = 1 },
            () => Grid([GridSize.Star()], [GridSize.Auto]), // a new, equal definition each render
            () => ScrollViewer(child),
        ];
        foreach (var make in equal)
        {
            var host = new TestHost(make);
            var before = host.Reconciler.DebugElementsSkipped;
            host.Render();
            Assert.True(host.Reconciler.DebugElementsSkipped > before, make().GetType().Name + " was not skipped");
        }
    }
}
