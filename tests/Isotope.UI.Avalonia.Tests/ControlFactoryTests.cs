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
        var reported = new List<bool>();
        var cb = CheckBox(true, reported.Add, "Agree");
        Assert.Equal((Optional<bool?>.Of(true), "Agree", false), (cb.IsChecked, cb.Content, cb.IsThreeState));
        // The record's callback is three-state (Avalonia's bool? IsChecked); the factory's two-state one hears only
        // checked and unchecked.
        cb.OnIsCheckedChanged!(false);
        cb.OnIsCheckedChanged(null);
        cb.OnIsCheckedChanged(true);
        Assert.Equal([false, true], reported);
        Assert.False(CheckBox().IsChecked.HasValue);
        Assert.Null(CheckBox().OnIsCheckedChanged);

        Action<bool?> states = _ => { };
        var three = ThreeStateCheckBox(null, states, "Some");
        Assert.Equal((true, Optional<bool?>.Of(null), "Some"), (three.IsThreeState, three.IsChecked, three.Content));
        Assert.Same(states, three.OnIsCheckedChanged);

        var sw = ToggleSwitch(true, changed, "On", "Off", "Wi-Fi");
        Assert.Equal((Optional<bool>.Of(true), "On", "Off", "Wi-Fi"), (sw.IsChecked, sw.OnContent, sw.OffContent, sw.Content));
        Assert.Same(changed, sw.OnIsCheckedChanged);

        var rb = RadioButton("A", true, changed, "g");
        Assert.Equal(("A", Optional<bool>.Of(true), "g"), (rb.Content, rb.IsChecked, rb.GroupName));
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
        Assert.Equal(Optional<DateTimeOffset>.Of(day), DatePicker(day, dateChanged).SelectedDate);
        Assert.Same(dateChanged, DatePicker(day, dateChanged).OnSelectedDateChanged);
        Action<TimeSpan> timeChanged = _ => { };
        Assert.Equal(Optional<TimeSpan>.Of(TimeSpan.FromHours(1)), TimePicker(TimeSpan.FromHours(1), timeChanged).SelectedTime);
        Assert.Same(timeChanged, TimePicker(TimeSpan.FromHours(1), timeChanged).OnSelectedTimeChanged);
    }

    [Fact]
    public void Container_progress_and_image_factories_build_their_records()
    {
        Element child = TextBlock("x");
        Assert.Same(child, Border(child).Child);
        Assert.Null(Border(null).Child);
        Assert.Same(child, ScrollViewer(child).Content);
        Action<bool> expanded = _ => { };
        var ex = Expander("More", child, true, expanded);
        Assert.Equal(("More", Optional<bool>.Of(true)), (ex.Header, ex.IsExpanded));
        Assert.Same(child, ex.Content);
        Assert.Same(expanded, ex.OnIsExpandedChanged);
        Assert.Equal([child], WrapPanel(child, null).Children);

        var grid = Grid([GridSize.Star()], [GridSize.Auto], child, null);
        Assert.Equal([GridSize.Star()], grid.ColumnDefinitions);
        Assert.Equal([GridSize.Auto], grid.RowDefinitions);
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
            Array.ConvertAll(["auto", "*", "2.5*", " 120 "], GridSize.Parse));
        Assert.Equal(["Auto", "*", "2.5*", "120"], new[] { GridSize.Auto, GridSize.Star(), GridSize.Star(2.5), GridSize.Px(120) }.Select(s => s.ToString()));
        Assert.Equal((10.0, 20.0), (GridSize.Star().MinSize(10).MaxSize(20).Min, GridSize.Star().MinSize(10).MaxSize(20).Max));
        Assert.Equal(new GridLength(3, GridUnitType.Star), (GridLength)GridSize.Star(3));
        foreach (var bad in new[] { "", "x", "*x", "-1", "0*", "NaN" })
            Assert.Throws<FormatException>(() => GridSize.Parse(bad));
        // Upstream's GridSize (vendored since #22): an infinite size parses, and its constructor rejects it.
        foreach (var infinite in new[] { "Infinity", "Infinity*" })
            Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Parse(infinite));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Px(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Star(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Auto.MinSize(-1));
        // Upstream doesn't check an Auto track's value; Avalonia's GridLength rejects a bad one when it is converted.
        Assert.ThrowsAny<ArgumentException>(() => (GridLength)new GridSize(double.NaN, GridUnitType.Auto));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimePicker() with { ClockIdentifier = 13 });
    }

    [AvaloniaFact]
    public void Unchanged_elements_of_the_new_controls_are_skipped_as_upstream_skips_them()
    {
        // Each record upstream compares in ShallowEquals: re-rendering an equal element skips its update.
        string[] items = ["a"];
        GridSize[] star = [GridSize.Star()];
        Element[] none = [];
        Element child = TextBlock("c");
        Func<Element>[] equal =
        [
            () => CheckBox(true, content: "a"),
            () => ToggleSwitch(true, content: "h"),
            () => RadioButton("a", true, groupName: "g"),
            () => ComboBox(items, 0) with { PlaceholderText = "p" },
            () => Progress(5),
            () => Image("none.png"),
            () => Border(child) with { CornerRadius = 2 },
            () => new GridElement(star, star, none) { RowSpacing = 1 },
            () => Grid([GridSize.Star()], [GridSize.Auto]), // new, equal tracks each render
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
