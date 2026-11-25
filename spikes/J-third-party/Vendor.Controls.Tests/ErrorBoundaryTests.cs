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

/// <summary>Isotope's error boundaries around, and inside, the vendor's controls.</summary>
public sealed class ErrorBoundaryTests
{
    private static Element Throws(string message) => Component(_ => throw new InvalidOperationException(message));

    [AvaloniaFact]
    public void A_boundary_around_a_card_catches_its_contents_throw()
    {
        using var m = new Mounted(_ => VStack(
            TextBlock("sibling"),
            ErrorBoundary(CalloutCard("h", Throws("boom")), ex => TextBlock("caught: " + ex.Message))));
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "caught: boom");
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "sibling");
        Assert.Empty(m.All<CalloutCardControl>());
    }

    [AvaloniaFact]
    public void A_boundary_inside_the_content_slot_keeps_the_card()
    {
        Action<bool>? setThrow = null;
        using var m = new Mounted(ctx =>
        {
            var (bad, set) = ctx.UseState(false);
            setThrow = set;
            return CalloutCard("h", ErrorBoundary(bad ? Throws("inside") : TextBlock("fine"), ex => TextBlock("caught: " + ex.Message)));
        });
        var card = m.One<CalloutCardControl>();
        var slot = card.Content; // the boundary's own control
        string[] Texts() => [.. ((Control)card.Content!).GetSelfAndVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

        setThrow!(true);
        Mounted.Pump();
        Assert.Same(card, m.One<CalloutCardControl>());
        Assert.Same(slot, card.Content);
        Assert.Equal(["caught: inside"], Texts());

        setThrow!(false); // the boundary's next render retries the child
        Mounted.Pump();
        Assert.Same(card, m.One<CalloutCardControl>());
        Assert.Equal(["fine"], Texts());
    }

    [AvaloniaFact]
    public void A_boundary_catches_the_vendor_controls_own_throw_on_mount()
    {
        // RatingBar's Maximum must be 1 to 50: Avalonia's validation throws inside the descriptor's write.
        using var m = new Mounted(_ => VStack(
            TextBlock("sibling"),
            ErrorBoundary(RatingBar(1, maximum: 0), ex => TextBlock("caught: " + ex.GetType().Name))));
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "caught: ArgumentException");
        Assert.Empty(m.All<RatingBarControl>());
    }

    [AvaloniaFact]
    public void A_boundary_catches_a_throw_from_the_escape_hatch()
    {
        using var m = new Mounted(_ => ErrorBoundary(
            CalloutCard("h", TextBlock("c")).Set(_ => throw new InvalidOperationException("setter")),
            ex => TextBlock("caught: " + ex.Message)));
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "caught: setter");
    }

    [AvaloniaFact]
    public void A_boundary_catches_the_vendor_controls_throw_on_update()
    {
        Action<int>? setMax = null;
        using var m = new Mounted(ctx =>
        {
            var (max, set) = ctx.UseState(5);
            setMax = set;
            return ErrorBoundary(RatingBar(1, maximum: max), ex => TextBlock("caught: " + ex.GetType().Name));
        });
        Assert.Single(m.All<RatingBarControl>());
        setMax!(0);
        Mounted.Pump();
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "caught: ArgumentException");
    }
}
