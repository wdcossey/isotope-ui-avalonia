using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI;
using Isotope.UI.Avalonia.TestRecords;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A library's own element records (#77), in an assembly of their own with the wrappers generator, as a third party
/// writes them: an element equal to the last render's is skipped as a built-in one is (FINDINGS, decisions after
/// Spike F, 3), through <c>Element.ShallowEqualsCore</c> and <c>HasCallbacks</c>, which the generator writes, or the
/// record itself.
/// </summary>
public class LibraryRecordSkipTests
{
    // Each render's element is a new instance equal to the last one: the same escape hatch array, made once, counts how
    // often the element reaches its control (the descriptor runs it after every mount and update).
    [AvaloniaFact]
    public void A_library_record_equal_to_the_last_renders_is_skipped()
    {
        int updates = 0;
        Action<Slider>[] count = [_ => updates++];
        int n = 0;
        var host = new TestHost(() => VStack(TextBlock("render " + n), new LibrarySliderElement(10) { Setters = count }));
        for (int i = 0; i < 10; i++) { n++; host.Render(); }

        Assert.Equal(11, host.RootRenders);
        Assert.Equal(["render 10"], host.Texts());
        Assert.Equal(1, updates); // the mount only
    }

    [AvaloniaFact]
    public void A_library_record_whose_member_changes_is_updated()
    {
        double max = 100;
        var host = new TestHost(() => VStack(new LibrarySliderElement(10) { Maximum = max }));
        var slider = host.All<Slider>()[0];
        max = 50;
        host.Render();
        Assert.Equal(50, slider.Maximum);
    }

    // As the built-in slider beside it: a value the handler rejects stays in the control across a render that gives the
    // same controlled value, since the element is skipped.
    [AvaloniaFact]
    public void A_rejected_edit_stays_as_on_the_built_in_slider()
    {
        var builtinReports = new List<double>();
        var libraryReports = new List<double>();
        int n = 0;
        var host = new TestHost(() => VStack(
            TextBlock("render " + n),
            new SliderElement(10, OnValueChanged: v => builtinReports.Add(v)),
            new LibrarySliderElement(10, OnValueChanged: v => libraryReports.Add(v))));
        var sliders = host.All<Slider>();
        sliders[0].Value = 30;
        sliders[1].Value = 30;
        n++;
        host.Render();

        Assert.Equal(["render 1"], host.Texts());
        Assert.Equal([30.0], builtinReports);
        Assert.Equal([30.0], libraryReports);
        Assert.Equal(30, sliders[0].Value);
        Assert.Equal(30, sliders[1].Value);
    }

    // As for a built-in: a callback that appears declines the skip, so the control is updated and subscribed.
    [AvaloniaFact]
    public void A_callback_that_appears_declines_the_skip()
    {
        int updates = 0;
        Action<Slider>[] count = [_ => updates++];
        var reports = new List<double>();
        Action<double>? callback = null;
        var host = new TestHost(() => VStack(new LibrarySliderElement(10, callback) { Setters = count }));
        callback = v => reports.Add(v);
        host.Render();
        Assert.Equal(2, updates);

        host.All<Slider>()[0].Value = 30;
        Assert.Equal([30.0], reports);
    }

    // A skipped element with callbacks still refreshes the control's element, so the user's change reaches the latest
    // render's closure, not the first.
    [AvaloniaFact]
    public void A_skipped_element_calls_the_latest_renders_callback()
    {
        int updates = 0;
        Action<Slider>[] count = [_ => updates++];
        var reports = new List<int>();
        int n = 0;
        var host = new TestHost(() =>
        {
            int render = n;
            return VStack(new LibrarySliderElement(10, _ => reports.Add(render)) { Setters = count });
        });
        for (int i = 0; i < 3; i++) { n++; host.Render(); }
        Assert.Equal(1, updates); // skipped

        host.All<Slider>()[0].Value = 30;
        Assert.Equal([3], reports);
    }

    // The record's own ShallowEqualsCore decides, and the generator writes none beside it: it ignores Maximum, so a
    // change to Maximum alone is skipped, and a change to Value isn't. The skipped Maximum stays lost: the update diffs
    // against the last render's element, which already had 50 (why an override must compare every member).
    [AvaloniaFact]
    public void A_hand_written_ShallowEqualsCore_decides()
    {
        double max = 100, value = 10;
        var host = new TestHost(() => VStack(new HandSliderElement(value) { Maximum = max }));
        var slider = host.All<Slider>()[0];
        max = 50;
        host.Render();
        Assert.Equal(100, slider.Maximum);

        value = 20;
        host.Render();
        Assert.Equal(20, slider.Value);
        Assert.Equal(100, slider.Maximum);
    }

    // The library's own records keep their arms in Element.ShallowEquals: the generator writes no skip members for them,
    // so those with no arm (ListBox, Expander, the pickers, …) are still updated on every render, as upstream's.
    [Fact]
    public void No_record_in_the_library_overrides_ShallowEqualsCore()
    {
        var overriding = typeof(Element).Assembly.GetTypes()
            .Where(t => typeof(Element).IsAssignableFrom(t) && t != typeof(Element))
            .Where(t => t.GetMethod("ShallowEqualsCore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not null)
            .Select(t => t.Name)
            .ToList();
        Assert.Empty(overriding);
    }

    // A built-in record with no arm is still updated on every render: an unchanged list box isn't skipped.
    [AvaloniaFact]
    public void A_built_in_record_with_no_arm_is_still_updated()
    {
        string[] items = ["a", "b"];
        int n = 0;
        var host = new TestHost(() => VStack(TextBlock("render " + n), new ListBoxElement(items)));
        var skipped = host.Reconciler.DebugElementsSkipped;
        for (int i = 0; i < 3; i++) { n++; host.Render(); }
        Assert.Equal(skipped, host.Reconciler.DebugElementsSkipped);
    }
}
