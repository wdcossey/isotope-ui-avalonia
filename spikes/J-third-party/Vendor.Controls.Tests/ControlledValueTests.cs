using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI;
using Isotope.UI.Core;
using Vendor.Controls.Isotope;
using Xunit;
using static Isotope.UI.Factories;
using static Vendor.Controls.Isotope.VendorFactories;
// The factories named after their controls hide the control types in member access (CS0119), as Isotope's Button does.
using RatingBarControl = Vendor.Controls.RatingBar;
using CalloutCardControl = Vendor.Controls.CalloutCard;
using AvaloniaButton = Avalonia.Controls.Button;
using AvaloniaSlider = Avalonia.Controls.Slider;

namespace Vendor.Controls.Tests;

/// <summary>
/// Isotope's rule (FINDINGS, decisions after Spike F, 3): an element equal to the last render's is skipped, so a value the
/// handler rejects stays in the control until the element changes. Built-in records get that from an arm in
/// <c>Element.ShallowEquals</c>; a third party's record from the <c>ShallowEqualsCore</c> and <c>HasCallbacks</c> the
/// bundled generator writes for it (#77, gap 1 closed).
/// </summary>
public sealed class ControlledValueTests
{
    private sealed record Setup(Mounted Mounted, Action Bump, List<double> SliderReports, List<int> RatingReports);

    // A slider and a rating whose handlers reject every change (they never set the state), and a counter that renders
    // the component again with the same controlled values.
    private static Setup MountRejecting()
    {
        var sliderReports = new List<double>();
        var ratingReports = new List<int>();
        Action? bump = null;
        var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = () => setN(n + 1);
            return VStack(
                TextBlock("render " + n),
                Slider(10, 0, 100, v => sliderReports.Add(v)),
                RatingBar(2, onValueChanged: v => ratingReports.Add(v)));
        });
        return new Setup(m, () => bump!(), sliderReports, ratingReports);
    }

    [AvaloniaFact]
    public void Builtin_a_rejected_edit_stays_across_a_render_with_the_same_value()
    {
        var s = MountRejecting();
        using var _ = s.Mounted;
        var slider = s.Mounted.One<AvaloniaSlider>();
        slider.Value = 30;
        Mounted.Pump();
        s.Bump();
        Mounted.Pump();
        Assert.Contains(s.Mounted.All<TextBlock>(), t => t.Text == "render 1");
        Assert.Equal([30.0], s.SliderReports);
        Assert.Equal(30, slider.Value); // skipped: not written back
    }

    // Gap 1, closed by #77 (docs/spikes/J-third-party-controls.md): the vendor's element is skipped as the built-in slider
    // is, so its controlled entry doesn't write the element's value back over the rejected edit.
    [AvaloniaFact]
    public void Vendor_a_rejected_edit_stays_across_a_render_with_the_same_value()
    {
        var s = MountRejecting();
        using var _ = s.Mounted;
        var bar = s.Mounted.One<RatingBarControl>();
        bar.Value = 4;
        Mounted.Pump();
        s.Bump();
        Mounted.Pump();
        Assert.Contains(s.Mounted.All<TextBlock>(), t => t.Text == "render 1");
        Assert.Equal([4], s.RatingReports);
        Assert.Equal(4, bar.Value); // skipped: not written back, as the built-in slider above
    }

    [AvaloniaFact]
    public void Vendor_a_new_controlled_value_is_written_when_the_element_changes()
    {
        Action<int>? set = null;
        using var m = new Mounted(ctx =>
        {
            var (v, setV) = ctx.UseState(2);
            set = setV;
            return RatingBar(v, onValueChanged: _ => { });
        });
        var bar = m.One<RatingBarControl>();
        bar.Value = 4; // rejected
        Mounted.Pump();
        Assert.Equal(4, bar.Value);
        set!(3);
        Mounted.Pump();
        Assert.Equal(3, bar.Value);
    }

    [AvaloniaFact]
    public void Vendor_an_unset_value_leaves_the_control_its_own()
    {
        Action? bump = null;
        using var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = () => setN(n + 1);
            return VStack(TextBlock("render " + n), RatingBar(maximum: 5));
        });
        var bar = m.One<RatingBarControl>();
        bar.Value = 4;
        bump!();
        Mounted.Pump();
        Assert.Equal(4, bar.Value);
    }

    // Gap 1, the cause, closed by #77: an unchanged vendor element is skipped, as a built-in one equal to the last render's.
    [AvaloniaFact]
    public void Vendor_an_unchanged_element_is_skipped()
    {
        // How often an unchanged element reaches its control: the escape hatch (.Set) runs after every mount and update.
        // One escape hatch made once, so each render's element is a new instance equal to the last one (the same Setters
        // array), which the generated ShallowEqualsCore compares by reference, as the built-in arms do.
        int vendor = 0;
        Action? bump = null;
        var unchanged = RatingBar(2).Set(_ => vendor++);
        using var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = () => setN(n + 1);
            return VStack(TextBlock("render " + n), unchanged with { });
        });
        for (int i = 0; i < 10; i++) { bump!(); Mounted.Pump(); }
        Assert.Equal(11, m.Renders);
        Assert.Equal(1, vendor); // the mount only: the 10 renders after it skip the element (11 before #77)
    }
}
