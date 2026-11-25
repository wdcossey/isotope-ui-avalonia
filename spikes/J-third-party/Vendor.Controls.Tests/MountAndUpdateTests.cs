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

/// <summary>The vendor's elements mount, update in place and call back like built-in ones.</summary>
public sealed class MountAndUpdateTests
{
    [AvaloniaFact]
    public void The_bundled_generator_wrote_and_registered_both_descriptors()
    {
        // Each Descriptor exists only if the generator ran in the vendor's build, on this framework's assembly.
        Assert.NotNull(RatingBarElement.Descriptor);
        Assert.NotNull(CalloutCardElement.Descriptor);
        using var m = new Mounted(_ => VStack(RatingBar(2), CalloutCard("h", TextBlock("c"))));
        Assert.Single(m.All<RatingBarControl>());
        Assert.Single(m.All<CalloutCardControl>());
    }

    [AvaloniaFact]
    public void A_plain_control_from_another_assembly_mounts_with_the_elements_values_and_modifiers()
    {
        using var m = new Mounted(_ => RatingBar(3, maximum: 7).Fill(Brushes.Red).Margin(4).Width(140)
            .ToolTip("rate it").Set(c => c.Tag = "set"));
        var bar = m.One<RatingBarControl>();
        Assert.Equal(3, bar.Value);
        Assert.Equal(7, bar.Maximum);
        Assert.Same(Brushes.Red, bar.Fill);
        Assert.Equal(new Thickness(4), bar.Margin);
        Assert.Equal(140, bar.Width);
        Assert.Equal("rate it", ToolTip.GetTip(bar));
        Assert.Equal("set", bar.Tag);
    }

    [AvaloniaFact]
    public void A_templated_control_mounts_its_header_content_and_template()
    {
        using var m = new Mounted(_ => CalloutCard("Tip", TextBlock("body")).Padding(6).FontSize(18));
        var card = m.One<CalloutCardControl>();
        Assert.Equal("Tip", card.Header);
        var body = Assert.IsType<TextBlock>(card.Content);
        Assert.Equal("body", body.Text);
        // The template is applied: its header part shows the header, and its presenter holds the content.
        Assert.Contains(m.All<TextBlock>(), t => t.Name == "PART_Header" && t.Text == "Tip");
        Assert.Contains(m.All<Avalonia.Controls.Presenters.ContentPresenter>(), p => p.Name == "PART_ContentPresenter" && ReferenceEquals(p.Child, body));
        Assert.Equal(new Thickness(6), card.Padding);
        Assert.Equal(18, card.FontSize);
    }

    [AvaloniaFact]
    public void A_state_change_updates_both_controls_in_place()
    {
        Action<int>? set = null;
        using var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(1);
            set = setN;
            return VStack(RatingBar(n, maximum: 5 + n), CalloutCard("header " + n, TextBlock("body " + n)));
        });
        var bar = m.One<RatingBarControl>();
        var card = m.One<CalloutCardControl>();
        var body = Assert.IsType<TextBlock>(card.Content);

        set!(3);
        Mounted.Pump();

        Assert.Same(bar, m.One<RatingBarControl>());
        Assert.Same(card, m.One<CalloutCardControl>());
        Assert.Same(body, card.Content);
        Assert.Equal(3, bar.Value);
        Assert.Equal(8, bar.Maximum);
        Assert.Equal("header 3", card.Header);
        Assert.Equal("body 3", body.Text);
        Assert.Equal(2, m.Renders);
    }

    [AvaloniaFact]
    public void A_member_set_back_to_null_clears_the_controls_value()
    {
        Action<bool>? setShown = null;
        using var m = new Mounted(ctx =>
        {
            var (shown, set) = ctx.UseState(true);
            setShown = set;
            return CalloutCard(shown ? "Tip" : null, TextBlock("c")).Accent(shown ? Brushes.Red : null);
        });
        var card = m.One<CalloutCardControl>();
        Assert.Equal("Tip", card.Header);
        Assert.Same(Brushes.Red, card.Accent);

        setShown!(false);
        Mounted.Pump();

        Assert.Null(card.Header);
        Assert.Null(card.Accent);
    }

    [AvaloniaFact]
    public void A_user_change_calls_back_and_a_write_from_state_does_not_echo()
    {
        var reported = new List<int>();
        Action<int>? set = null;
        using var m = new Mounted(ctx =>
        {
            var (value, setValue) = ctx.UseState(1);
            set = setValue;
            return VStack(TextBlock("state " + value), RatingBar(value, onValueChanged: v => { reported.Add(v); setValue(v); }));
        });
        var bar = m.One<RatingBarControl>();

        bar.Value = 4; // as a click does
        Mounted.Pump();
        Assert.Equal([4], reported);
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "state 4");

        set!(2); // the engine writes 2: the control raises ValueChanged, which is its own write's echo
        Mounted.Pump();
        Assert.Equal(2, bar.Value);
        Assert.Equal([4], reported);

        bar.Value = 5;
        Assert.Equal([4, 5], reported);
    }

    [AvaloniaFact]
    public void A_routed_event_calls_the_latest_renders_callback()
    {
        var calls = new List<string>();
        Action<int>? set = null;
        using var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            set = setN;
            return CalloutCard("h", TextBlock("c"), onDismissed: () => calls.Add("render " + n));
        });
        var card = m.One<CalloutCardControl>();
        card.Dismiss();
        set!(1);
        Mounted.Pump();
        card.Dismiss();
        Assert.Equal(["render 0", "render 1"], calls);
        // The template's close button raises it too.
        TestClick(m.All<AvaloniaButton>().Single(b => b.Name == "PART_Dismiss"));
        Assert.Equal(["render 0", "render 1", "render 1"], calls);
    }

    [AvaloniaFact]
    public void Unmounting_detaches_the_callbacks()
    {
        var calls = 0;
        Action<bool>? show = null;
        using var m = new Mounted(ctx =>
        {
            var (visible, setVisible) = ctx.UseState(true);
            show = setVisible;
            return VStack(visible ? CalloutCard("h", TextBlock("c"), onDismissed: () => calls++) : TextBlock("gone"),
                visible ? RatingBar(1, onValueChanged: _ => calls++) : TextBlock("gone"));
        });
        var card = m.One<CalloutCardControl>();
        var bar = m.One<RatingBarControl>();
        show!(false);
        Mounted.Pump();
        Assert.Empty(m.All<CalloutCardControl>());
        card.Dismiss();
        bar.Value = 3;
        Assert.Equal(0, calls);
    }

    internal static void TestClick(AvaloniaButton button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(AvaloniaButton.ClickEvent));
}
