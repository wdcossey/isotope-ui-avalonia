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

/// <summary>Keyed reorders move the vendor's controls, and a component in the card's content slot keeps its state.</summary>
public sealed class KeyedAndSlotTests
{
    // A counter: a button whose text is its own state.
    private static Element Counter(string name) => Component(ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        return Button(name + " " + n, () => setN(n + 1));
    });

    [AvaloniaFact]
    public void A_keyed_reverse_moves_the_controls_without_detaching_them()
    {
        string[] ids = ["a", "b", "c", "d", "e"];
        Action<string[]>? setOrder = null;
        using var m = new Mounted(ctx =>
        {
            var (order, set) = ctx.UseState(ids);
            setOrder = set;
            return VStack([.. order.Select(id => (Element)RatingBar(id[0] - 'a' + 1).WithKey(id))]);
        });
        var before = m.All<RatingBarControl>();
        var detached = 0;
        foreach (var bar in before) bar.DetachedFromVisualTree += (_, _) => detached++;

        setOrder!([.. ids.Reverse()]);
        Mounted.Pump();

        var after = m.All<RatingBarControl>();
        Assert.Equal([.. Enumerable.Reverse(before)], after); // the same instances, in the new order
        Assert.Equal([5, 4, 3, 2, 1], after.Select(b => b.Value));
        Assert.Equal(0, detached);
    }

    [AvaloniaFact]
    public void A_keyed_reverse_of_cards_keeps_each_cards_content_state_and_focus()
    {
        string[] ids = ["a", "b", "c"];
        Action<string[]>? setOrder = null;
        using var m = new Mounted(ctx =>
        {
            var (order, set) = ctx.UseState(ids);
            setOrder = set;
            return VStack([.. order.Select(id => (Element)CalloutCard("card " + id, Counter(id)).WithKey(id))]);
        });
        // Click b's counter twice and c's once, then focus c's close button (a part of its template).
        var counters = m.All<AvaloniaButton>().Where(b => b.Name != "PART_Dismiss").ToList();
        MountAndUpdateTests.TestClick(counters[1]); Mounted.Pump();
        MountAndUpdateTests.TestClick(counters[1]); Mounted.Pump();
        MountAndUpdateTests.TestClick(counters[2]); Mounted.Pump();
        var cards = m.All<CalloutCardControl>();
        var close = cards[2].GetVisualDescendants().OfType<AvaloniaButton>().Single(b => b.Name == "PART_Dismiss");
        Assert.True(close.Focus());

        setOrder!(["c", "b", "a"]);
        Mounted.Pump();

        Assert.Equal([cards[2], cards[1], cards[0]], m.All<CalloutCardControl>());
        Assert.Equal(["c 1", "b 2", "a 0"],
            m.All<AvaloniaButton>().Where(b => b.Name != "PART_Dismiss").Select(b => b.Content as string));
        Assert.True(close.IsFocused);
    }

    [AvaloniaFact]
    public void A_component_in_the_content_slot_keeps_its_state_across_the_cards_updates()
    {
        Action<string>? setHeader = null;
        using var m = new Mounted(ctx =>
        {
            var (header, set) = ctx.UseState("one");
            setHeader = set;
            return CalloutCard(header, Counter("count")).Accent(header == "one" ? Brushes.Red : Brushes.Blue);
        });
        var card = m.One<CalloutCardControl>();
        var button = m.All<AvaloniaButton>().Single(b => b.Name != "PART_Dismiss");
        MountAndUpdateTests.TestClick(button); Mounted.Pump();
        MountAndUpdateTests.TestClick(button); Mounted.Pump();

        setHeader!("two");
        Mounted.Pump();

        Assert.Same(card, m.One<CalloutCardControl>());
        Assert.Equal("two", card.Header);
        Assert.Same(Brushes.Blue, card.Accent);
        var after = m.All<AvaloniaButton>().Single(b => b.Name != "PART_Dismiss");
        Assert.Same(button, after);
        Assert.Equal("count 2", after.Content);
    }

    [AvaloniaFact]
    public void Another_kind_of_content_replaces_the_child_and_keeps_the_card()
    {
        Action<bool>? setText = null;
        using var m = new Mounted(ctx =>
        {
            var (text, set) = ctx.UseState(false);
            setText = set;
            return CalloutCard("h", text ? TextBlock("now text") : Counter("count"));
        });
        var card = m.One<CalloutCardControl>();
        // A component mounts inside a Border of the reconciler's, as everywhere in Isotope.
        Assert.Single(card.GetVisualDescendants().OfType<AvaloniaButton>(), b => b.Name != "PART_Dismiss");

        setText!(true);
        Mounted.Pump();

        Assert.Same(card, m.One<CalloutCardControl>());
        Assert.Equal("now text", Assert.IsType<TextBlock>(card.Content).Text);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<AvaloniaButton>(), b => b.Name != "PART_Dismiss");
    }

    [AvaloniaFact]
    public void Cards_nest_and_the_inner_cards_component_keeps_its_state()
    {
        Action? bump = null;
        using var m = new Mounted(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            bump = () => setN(n + 1);
            return CalloutCard("outer " + n, VStack(RatingBar(n), CalloutCard("inner", Counter("count"))));
        });
        var button = m.All<AvaloniaButton>().Single(b => b.Name != "PART_Dismiss" && b.Content is string s && s.StartsWith("count"));
        MountAndUpdateTests.TestClick(button); Mounted.Pump();
        bump!();
        Mounted.Pump();
        Assert.Equal(2, m.All<CalloutCardControl>().Count);
        Assert.Equal("count 1", button.Content);
        Assert.Equal(1, m.One<RatingBarControl>().Value);
    }
}
