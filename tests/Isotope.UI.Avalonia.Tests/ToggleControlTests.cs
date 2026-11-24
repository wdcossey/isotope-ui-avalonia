using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// CheckBox, ToggleSwitch and RadioButton (#19): mount, update in place, and the controlled checked state, whose
/// engine writes don't echo while the user's changes reach the state.
/// </summary>
public class ToggleControlTests
{
    [AvaloniaFact]
    public void CheckBox_mounts_its_label_and_state_and_updates_them_in_place()
    {
        var label = "Agree";
        var host = new TestHost(() => CheckBox(true, label: label));
        var box = host.All<CheckBox>()[0];
        Assert.Equal((true, "Agree"), (box.IsChecked, box.Content));
        Assert.False(box.IsThreeState);

        label = "Accept";
        host.Render();
        Assert.Same(box, host.All<CheckBox>()[0]);
        Assert.Equal("Accept", box.Content);
    }

    [AvaloniaFact]
    public void CheckBox_label_set_back_to_null_clears_the_content()
    {
        string? label = "Agree";
        var host = new TestHost(() => CheckBox(label: label));
        var box = host.All<CheckBox>()[0];

        label = null;
        host.Render();
        Assert.Null(box.Content);
    }

    [AvaloniaFact]
    public void CheckBox_user_change_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (on, setOn) = ctx.UseState(true);
            set = setOn;
            return VStack(TextBlock("state " + on), CheckBox(on, v => { reported.Add(v); setOn(v); }, "Agree"));
        }));
        var box = host.All<CheckBox>()[0];

        box.IsChecked = false; // as a click does
        host.Flush();
        Assert.Equal([false], reported);
        Assert.Equal(["state False"], host.Texts());

        set!(true); // from the state: written, not reported
        host.Flush();
        Assert.True(box.IsChecked);
        Assert.Equal([false], reported);

        box.IsChecked = false;
        Assert.Equal([false, false], reported);
        Assert.Same(box, host.All<CheckBox>()[0]);
    }

    [AvaloniaFact]
    public void CheckBox_rejected_click_stays_until_the_element_changes_as_upstream()
    {
        // Upstream's rule (as TextBox's): an element equal to the last render's is skipped, so a click the handler
        // rejects stays; when the element changes for another reason, the controlled value is written back.
        Action? bump = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = () => setN(n + 1);
            return VStack(TextBlock("render " + n), CheckBox(true, _ => { }, n < 2 ? "Agree" : "Agree!"));
        }));
        var box = host.All<CheckBox>()[0];

        box.IsChecked = false; // rejected: the state isn't set
        bump!();
        host.Flush();
        Assert.Equal(["render 1"], host.Texts());
        Assert.False(box.IsChecked);

        bump!(); // the label changes: the element is updated and the controlled value written back
        host.Flush();
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void Three_state_CheckBox_reports_the_indeterminate_state_and_a_two_state_one_does_not()
    {
        var states = new List<bool?>();
        var host = new TestHost(() => ThreeStateCheckBox(null, states.Add, "Some"));
        var box = host.All<CheckBox>()[0];
        Assert.True(box.IsThreeState);
        Assert.Null(box.IsChecked);

        box.IsChecked = true;
        box.IsChecked = null;
        Assert.Equal([true, null], states);

        var checks = new List<bool>();
        var two = new TestHost(() => CheckBox(false, checks.Add));
        var plain = two.All<CheckBox>()[0];
        plain.IsChecked = null; // indeterminate: not a bool to report
        plain.IsChecked = true;
        Assert.Equal([true], checks);
    }

    [AvaloniaFact]
    public void CheckBox_with_no_value_keeps_what_the_user_set_across_renders()
    {
        var label = "a";
        var host = new TestHost(() => CheckBox(label: label));
        var box = host.All<CheckBox>()[0];
        box.IsChecked = true;

        label = "b";
        host.Render();
        Assert.Equal("b", box.Content);
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void ToggleSwitch_mounts_its_contents_and_header_and_updates_them_in_place()
    {
        var header = "Wi-Fi";
        var host = new TestHost(() => ToggleSwitch(true, onContent: "On", offContent: "Off", header: header));
        var sw = host.All<ToggleSwitch>()[0];
        Assert.Equal((true, "On", "Off", "Wi-Fi"), (sw.IsChecked, sw.OnContent, sw.OffContent, sw.Content));

        header = "Bluetooth";
        host.Render();
        Assert.Same(sw, host.All<ToggleSwitch>()[0]);
        Assert.Equal("Bluetooth", sw.Content);
    }

    [AvaloniaFact]
    public void ToggleSwitch_user_toggle_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<bool>();
        Action<bool>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (on, setOn) = ctx.UseState(false);
            set = setOn;
            return VStack(TextBlock("state " + on), ToggleSwitch(on, v => { reported.Add(v); setOn(v); }));
        }));
        var sw = host.All<ToggleSwitch>()[0];

        sw.IsChecked = true;
        host.Flush();
        Assert.Equal([true], reported);
        Assert.Equal(["state True"], host.Texts());

        set!(false);
        host.Flush();
        Assert.False(sw.IsChecked);
        Assert.Equal([true], reported);
    }

    [AvaloniaFact]
    public void RadioButtons_in_a_group_report_the_user_choice_and_the_engine_writes_do_not_echo()
    {
        var reported = new List<string>();
        Action<int>? choose = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (chosen, setChosen) = ctx.UseState(0);
            choose = setChosen;
            return VStack(
                TextBlock("chosen " + chosen),
                RadioButton("A", chosen == 0, v => { reported.Add("A " + v); if (v) setChosen(0); }, "g"),
                RadioButton("B", chosen == 1, v => { reported.Add("B " + v); if (v) setChosen(1); }, "g"));
        }));
        var radios = host.All<RadioButton>();
        Assert.Equal(("A", "B", "g"), (radios[0].Content, radios[1].Content, radios[1].GroupName));
        Assert.Equal((true, false), (radios[0].IsChecked, radios[1].IsChecked));

        radios[1].IsChecked = true; // a click on B: the group unchecks A, which is reported too
        host.Flush();
        Assert.Equal(["B True", "A False"], reported);
        Assert.Equal(["chosen 1"], host.Texts());

        choose!(0); // from the state: both written, neither reported
        host.Flush();
        Assert.Equal((true, false), (radios[0].IsChecked, radios[1].IsChecked));
        Assert.Equal(["B True", "A False"], reported);
    }
}
