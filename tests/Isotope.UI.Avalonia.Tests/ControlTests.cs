using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The TextBox and Slider elements, whose descriptors upstream's wrappers generator generates for Avalonia (#12): the
/// controlled value, echo suppression, and Slider's Min and Max written by the record's Customize.
/// </summary>
public class ControlTests
{
    [AvaloniaFact]
    public void TextBox_controlled_value_is_written_and_user_edits_reach_the_state()
    {
        var reported = new List<string>();
        var host = new TestHost(() => Component(ctx =>
        {
            var (text, setText) = ctx.UseState("a");
            return VStack(Text("state " + text), new TextBoxElement(text, v => { reported.Add(v); setText(v); }));
        }));
        var box = host.All<TextBox>()[0];
        Assert.Equal("a", box.Text);
        Dispatcher.UIThread.RunJobs(); // Avalonia raises TextChanged after a write, the mount's too: no echo
        Assert.Empty(reported);

        box.Text = "ab"; // as typing does
        Dispatcher.UIThread.RunJobs(); // Avalonia raises TextChanged after the write, as WinUI does
        host.Flush();

        Assert.Equal(["ab"], reported);
        Assert.Equal(["state ab"], host.Texts());
        Assert.Equal("ab", box.Text);
        Assert.Same(box, host.All<TextBox>()[0]);
    }

    [AvaloniaFact]
    public void TextBox_write_of_a_new_value_does_not_echo_and_the_next_edit_is_reported()
    {
        var reported = new List<string>();
        Action<string>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (text, setText) = ctx.UseState("a");
            set = setText;
            return new TextBoxElement(text, v => { reported.Add(v); setText(v); });
        }));
        var box = host.All<TextBox>()[0];
        Dispatcher.UIThread.RunJobs();

        set!("reset");
        host.Flush();
        Dispatcher.UIThread.RunJobs(); // the write's TextChanged comes after it; its echo is dropped

        Assert.Equal("reset", box.Text);
        Assert.Empty(reported);

        box.Text = "reset!";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["reset!"], reported);
    }

    [AvaloniaFact]
    public void TextBox_two_writes_before_their_events_arrive_do_not_echo()
    {
        var reported = new List<string>();
        Action<string>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (text, setText) = ctx.UseState("a");
            set = setText;
            return new TextBoxElement(text, v => { reported.Add(v); setText(v); });
        }));
        var box = host.All<TextBox>()[0];
        Dispatcher.UIThread.RunJobs();

        // Two renders write before the dispatcher runs: both TextChanged events arrive afterwards, reading "c".
        set!("b");
        host.Flush();
        set!("c");
        host.Flush();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("c", box.Text);
        Assert.Empty(reported);

        box.Text = "d";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["d"], reported);
    }

    [AvaloniaFact]
    public void TextBox_nullable_members_set_back_to_null_return_to_the_defaults()
    {
        var set = true;
        var host = new TestHost(() => set
            ? new TextBoxElement(PlaceholderText: "name")
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            }
            : new TextBoxElement());
        var box = host.All<TextBox>()[0];
        Assert.Equal("name", box.PlaceholderText);
        Assert.True(box.IsReadOnly);
        Assert.True(box.AcceptsReturn);

        set = false;
        host.Render();

        var defaults = new TextBox();
        Assert.Equal(defaults.PlaceholderText, box.PlaceholderText);
        Assert.Equal(defaults.IsReadOnly, box.IsReadOnly);
        Assert.Equal(defaults.AcceptsReturn, box.AcceptsReturn);
        Assert.Equal(defaults.TextWrapping, box.TextWrapping);
    }

    private sealed record Heading(string Title) : TextBlockElement(Title);

    [AvaloniaFact]
    public void A_record_derived_from_a_control_record_mounts_and_updates_as_its_base()
    {
        var title = "one";
        var host = new TestHost(() => VStack(new Heading(title)));
        var text = host.All<TextBlock>()[0];
        Assert.Equal(["one"], host.Texts());

        title = "two";
        host.Render();

        Assert.Equal(["two"], host.Texts());
        Assert.Same(text, host.All<TextBlock>()[0]);
    }

    [AvaloniaFact]
    public void TextBox_with_no_value_keeps_what_the_user_typed_across_renders()
    {
        Action<int>? bump = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = setN;
            // The placeholder changes with each render, so the box is updated rather than skipped.
            return VStack(Text(n.ToString()), new TextBoxElement(PlaceholderText: "name " + n));
        }));
        var box = host.All<TextBox>()[0];
        Assert.Equal("name 0", box.PlaceholderText);

        box.Text = "typed";
        Dispatcher.UIThread.RunJobs();
        bump!(1);
        host.Flush();

        Assert.Equal(["1"], host.Texts());
        Assert.Equal("name 1", box.PlaceholderText);
        Assert.Equal("typed", box.Text);
    }

    [AvaloniaFact]
    public void Slider_value_pairs_with_ValueChanged_and_a_new_value_does_not_echo()
    {
        var reported = new List<double>();
        Action<double>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (value, setValue) = ctx.UseState(10.0);
            set = setValue;
            return VStack(Text("state " + value), new SliderElement(value, OnValueChanged: v => { reported.Add(v); setValue(v); }));
        }));
        var slider = host.All<Slider>()[0];
        Assert.Equal(10, slider.Value);

        slider.Value = 30; // as dragging does
        host.Flush();
        Assert.Equal([30.0], reported);
        Assert.Equal(["state 30"], host.Texts());

        set!(50);
        host.Flush();
        Assert.Equal(50, slider.Value);
        Assert.Equal([30.0], reported);

        slider.Value = 60;
        Assert.Equal([30.0, 60.0], reported);
    }

    [AvaloniaFact]
    public void Slider_Min_and_Max_are_written_first_and_a_coercing_bound_does_not_echo()
    {
        var reported = new List<double>();
        var min = 10.0;
        var host = new TestHost(() => new SliderElement(5, Min: min, Max: 20, OnValueChanged: v => reported.Add(v)));
        var slider = host.All<Slider>()[0];

        // Min and Max land before Value, so the value is clamped to the range it was given with.
        Assert.Equal(10, slider.Minimum);
        Assert.Equal(20, slider.Maximum);
        Assert.Equal(10, slider.Value);

        min = 15; // the bound now coerces the value up
        host.Render();
        Assert.Equal(15, slider.Minimum);
        Assert.Equal(15, slider.Value);
        Assert.Empty(reported);

        slider.Value = 18;
        Assert.Equal([18.0], reported);
    }
}
