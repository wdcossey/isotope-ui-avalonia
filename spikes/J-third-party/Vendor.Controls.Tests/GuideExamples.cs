#region controls-usings
using Isotope.UI;
using Isotope.UI.Core;
using Vendor.Controls.Isotope;
using static Isotope.UI.Factories;
using static Vendor.Controls.Isotope.VendorFactories;
// With the factory imported, RatingBar names the method, not the control type (CS0119): alias a type you need.
using RatingBarControl = Vendor.Controls.RatingBar;
#endregion
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CalloutCardControl = Vendor.Controls.CalloutCard;
using SpreadPanelControl = Vendor.Controls.SpreadPanel;
using AvaloniaButton = Avalonia.Controls.Button;
using Xunit;

namespace Vendor.Controls.Tests;

/// <summary>
/// What a library's users write: the C# blocks of docs/guide/writing-controls.md (DocSnippetTests), each a #region here,
/// run by <see cref="GuideExampleTests"/>. Change the guide and the region together.
/// </summary>
public static class GuideExamples
{
    #region controls-usage
    // A component that uses the library's controls beside Isotope's own.
    public static Element Review(RenderContext ctx)
    {
        var (stars, setStars) = ctx.UseState(3);
        var (open, setOpen) = ctx.UseState(true);
        if (!open) return Button("Show the card", () => setOpen(true));
        return CalloutCard("Rate this guide",
                VStack(8,
                    RatingBar(stars, onValueChanged: setStars).Fill(Theme.Accent),
                    TextBlock($"{stars} of 5")),
                onDismissed: () => setOpen(false))
            .Accent(Theme.Accent)
            .Margin(8);
    }
    #endregion

    #region controls-wrap-event
    // OnCleared comes from a plain .NET event, through [WrapEvent("Cleared", Arg = "OldValue")].
    public static Element ClearableRating(RenderContext ctx)
    {
        var (stars, setStars) = ctx.UseState(4);
        var (cleared, setCleared) = ctx.UseState<int?>(null);
        return VStack(
            RatingBar(stars, onValueChanged: setStars, onCleared: old => setCleared(old)),
            TextBlock(cleared is { } was ? $"Cleared a {was}" : $"{stars} stars"));
    }
    #endregion

    #region controls-panel-usage
    // The library's panel takes keyed children and Isotope's groups, as a StackPanel does.
    public static Element Scores(RenderContext ctx)
    {
        var (names, setNames) = ctx.UseState<string[]>(["first", "second", "third"]);
        return VStack(
            SpreadPanel(
                TextBlock("Scores"),
                Group([.. names.Select(name => (Element)CalloutCard(name, RatingBar(3)).WithKey(name))]))
                with { Spacing = 8 },
            Button("Reverse", () => setNames([.. Enumerable.Reverse(names)])));
    }
    #endregion

    #region controls-error-boundary
    // A boundary catches what a library's control throws: here, Avalonia's validation of RatingBar.Maximum.
    public static Element SafeRating(RenderContext ctx) =>
        ErrorBoundary(RatingBar(1, maximum: 0), error => TextBlock($"The rating failed: {error.Message}"));
    #endregion
}

/// <summary>The guide's examples do what the guide says they do.</summary>
public sealed class GuideExampleTests
{
    [AvaloniaFact]
    public void The_review_card_rates_and_dismisses()
    {
        using var m = new Mounted(GuideExamples.Review);
        var bar = m.One<RatingBarControl>();
        var card = m.One<CalloutCardControl>();
        Assert.Equal(3, bar.Value);
        // The tokens are bound: the control's own fill is Goldenrod, and the card has no accent of its own.
        Assert.NotNull(bar.Fill);
        Assert.NotSame(Brushes.Goldenrod, bar.Fill);
        Assert.NotNull(card.Accent);

        bar.Value = 4; // as a click does
        Mounted.Pump();
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "4 of 5");
        Assert.Same(bar, m.One<RatingBarControl>());

        card.Dismiss();
        Mounted.Pump();
        Assert.Empty(m.All<CalloutCardControl>());
        Assert.Contains(m.All<AvaloniaButton>(), b => b.Content as string == "Show the card");
    }

    [AvaloniaFact]
    public void A_wrap_event_callback_gets_the_projected_value()
    {
        using var m = new Mounted(GuideExamples.ClearableRating);
        var bar = m.One<RatingBarControl>();
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "4 stars");

        bar.Clear();
        Mounted.Pump();
        Assert.Equal(0, bar.Value);
        Assert.Contains(m.All<TextBlock>(), t => t.Text == "Cleared a 4");
    }

    [AvaloniaFact]
    public void A_wrap_manual_member_is_written_by_customize()
    {
        Action<bool>? setReadOnly = null;
        using var m = new Mounted(ctx =>
        {
            var (readOnly, set) = ctx.UseState(true);
            setReadOnly = set;
            return RatingBar(2) with { IsReadOnly = readOnly };
        });
        var bar = m.One<RatingBarControl>();
        Assert.False(bar.IsHitTestVisible);

        setReadOnly!(false);
        Mounted.Pump();
        Assert.Same(bar, m.One<RatingBarControl>());
        Assert.True(bar.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void The_panel_example_reverses_its_keyed_cards_in_place()
    {
        using var m = new Mounted(GuideExamples.Scores);
        var panel = m.One<SpreadPanelControl>();
        var before = panel.Children.ToList();
        Assert.Equal(4, before.Count); // the text and the group's three cards
        Assert.Equal(8, panel.Spacing);

        MountAndUpdateTests.TestClick(m.All<AvaloniaButton>().Single(b => b.Content as string == "Reverse"));
        Mounted.Pump();

        Assert.Equal([before[0], before[3], before[2], before[1]], panel.Children);
        Assert.Equal(["third", "second", "first"], panel.Children.OfType<CalloutCardControl>().Select(c => c.Header));
    }

    [AvaloniaFact]
    public void The_boundary_example_shows_the_controls_error()
    {
        using var m = new Mounted(GuideExamples.SafeRating);
        Assert.Empty(m.All<RatingBarControl>());
        Assert.Contains(m.All<TextBlock>(), t => t.Text?.StartsWith("The rating failed: ") == true);
    }
}
