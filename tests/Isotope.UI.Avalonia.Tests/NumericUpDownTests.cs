using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvSeparator = Avalonia.Controls.Separator;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// NumericUpDown (Reactor's NumberBox), and the Separator (#26): the controlled value, whose engine writes don't echo
/// while the user's changes reach the state; bounds, their coercion echo and stranded tokens; NaN as an empty box; the
/// double-to-decimal conversion.
/// </summary>
public class NumericUpDownTests
{
    [AvaloniaFact]
    public void NumericUpDown_user_change_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<double>();
        Action<double>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (n, setN) = ctx.UseState(2.5);
            set = setN;
            return VStack(TextBlock("n " + n), NumericUpDown(n, v => { reported.Add(v); setN(v); }));
        }));
        var box = host.All<NumericUpDown>()[0];
        Assert.Equal(2.5m, box.Value);

        box.Value = 4; // as typing does
        host.Flush();
        Assert.Equal([4.0], reported);
        Assert.Equal("n 4", host.Texts()[0]);

        set!(7.25); // from the state: written, not reported
        host.Flush();
        Assert.Equal(7.25m, box.Value);
        Assert.Equal([4.0], reported);

        box.Value = 1; // the next user change still arrives
        Assert.Equal([4.0, 1.0], reported);
        Assert.Same(box, host.All<NumericUpDown>()[0]);
    }

    [AvaloniaFact]
    public void NumericUpDown_value_that_does_not_round_trip_through_decimal_leaves_no_token_behind()
    {
        // 0.1 + 0.2 is 0.30000000000000004 as a double and 0.3 as the decimal the control holds: not drift, so no write
        // (whose token would swallow the next user change, since writing the same decimal raises no event).
        var reported = new List<double>();
        var tick = 0;
        var host = new TestHost(() => NumericUpDown(0.1 + 0.2, v => reported.Add(v)) with { PlaceholderText = "t" + tick });
        var box = host.All<NumericUpDown>()[0];

        tick = 1;
        host.Render(); // updates the box (its placeholder changes) with the same value
        box.Value = 5;
        Assert.Equal([5.0], reported);
    }

    [AvaloniaFact]
    public void NumericUpDown_writes_its_bounds_step_and_value_and_NaN_is_an_empty_box()
    {
        double value = 150;
        var host = new TestHost(() => NumericUpDown(value) with { Minimum = 100, Maximum = 200, Increment = 5 });
        var box = host.All<NumericUpDown>()[0];
        Assert.Equal((100m, 200m, 150m, 5m), (box.Minimum, box.Maximum, box.Value, box.Increment));

        value = double.NaN;
        host.Render();
        Assert.Null(box.Value);
    }

    [AvaloniaFact]
    public void NumericUpDown_bound_moved_past_the_value_leaves_no_token_behind()
    {
        // Avalonia's NumericUpDown doesn't clip its value to a new bound (ClipValueToMinMax is off by default), so the
        // bound's write raises no change event and must not expect one.
        var reported = new List<double>();
        double min = 0;
        var host = new TestHost(() => NumericUpDown(5, v => reported.Add(v)) with { Minimum = min });
        var box = host.All<NumericUpDown>()[0];

        min = 10;
        host.Render();
        Assert.Equal(10m, box.Minimum);
        box.Value = 12; // the user's next change
        Assert.Equal([12.0], reported);
    }

    [AvaloniaFact]
    public void NumericUpDown_bound_that_clips_the_value_drops_the_coercion_echo()
    {
        // With ClipValueToMinMax on (here from a setter), a bound moved past the value clips it and raises ValueChanged:
        // the engine's, not the user's.
        var reported = new List<double>();
        double min = 0;
        var host = new TestHost(() => NumericUpDown(onValueChanged: v => reported.Add(v)) with
        {
            Minimum = min, Setters = [c => c.ClipValueToMinMax = true],
        });
        var box = host.All<NumericUpDown>()[0];
        box.Value = 5;
        Assert.Equal([5.0], reported);

        min = 10;
        host.Render();
        Assert.Equal(10m, box.Value);
        Assert.Equal([5.0], reported);
        box.Value = 12; // the next user change still arrives
        Assert.Equal([5.0, 12.0], reported);
    }

    [AvaloniaFact]
    public void NumericUpDown_default_bounds_are_decimal_range()
    {
        var host = new TestHost(() => NumericUpDown(1));
        var box = host.All<NumericUpDown>()[0];
        Assert.Equal((decimal.MinValue, decimal.MaxValue), (box.Minimum, box.Maximum));
    }

    [AvaloniaFact]
    public void NumericUpDown_without_a_value_leaves_it_to_the_control_and_writes_its_other_members()
    {
        var reported = new List<double>();
        string? format = "F2";
        var host = new TestHost(() => NumericUpDown(onValueChanged: v => reported.Add(v)) with
        {
            PlaceholderText = "Amount", FormatString = format, ShowButtonSpinner = false,
        });
        var box = host.All<NumericUpDown>()[0];
        Assert.Null(box.Value);
        Assert.Equal(("Amount", "F2", false), (box.PlaceholderText, box.FormatString, box.ShowButtonSpinner));

        box.Value = 3;
        Assert.Equal([3.0], reported);

        format = null;
        host.Render();
        Assert.Equal(3m, box.Value);
        Assert.Equal(new NumericUpDown().FormatString, box.FormatString);
    }

    [AvaloniaFact]
    public void Separator_mounts_an_avalonia_separator()
    {
        var host = new TestHost(() => VStack(TextBlock("a"), Separator(), TextBlock("b")));
        var stack = host.All<StackPanel>()[0];
        Assert.IsType<AvSeparator>(stack.Children[1]);
    }
}
