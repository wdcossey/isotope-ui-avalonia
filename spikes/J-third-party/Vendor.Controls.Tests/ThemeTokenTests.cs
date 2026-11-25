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

/// <summary>Isotope's theme tokens on the vendor's controls: the built-in modifiers, and the vendor's own brush properties.</summary>
public sealed class ThemeTokenTests
{
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void The_builtin_token_modifiers_reach_a_templated_control_and_follow_a_theme_switch()
    {
        using var m = new Mounted(_ => CalloutCard("h", TextBlock("c"))
            .Background(Theme.CardBackground).Foreground(Theme.PrimaryText).BorderBrush(Theme.CardStroke), ThemeVariant.Light);
        var card = m.One<CalloutCardControl>();
        var light = (ColorOf(card.Background), ColorOf(card.Foreground), ColorOf(card.BorderBrush));

        m.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Mounted.Pump();

        var dark = (ColorOf(card.Background), ColorOf(card.Foreground), ColorOf(card.BorderBrush));
        Assert.NotEqual(light, dark);
        Assert.Equal(ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: true)), dark.Item2);
        Assert.Equal(ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: false)), light.Item2);
        Assert.Equal(1, m.Renders); // no re-render
    }

    [AvaloniaFact]
    public void A_token_on_the_vendors_own_brush_property_follows_a_theme_switch()
    {
        using var m = new Mounted(_ => VStack(
            CalloutCard("h", TextBlock("c")).Accent(Theme.Accent),
            RatingBar(3).Fill(Theme.Accent)), ThemeVariant.Light);
        var card = m.One<CalloutCardControl>();
        var bar = m.One<RatingBarControl>();
        var light = ColorOf(card.Accent);
        Assert.Equal(light, ColorOf(bar.Fill));

        m.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Mounted.Pump();

        Assert.NotEqual(light, ColorOf(card.Accent));
        Assert.Equal(ColorOf(card.Accent), ColorOf(bar.Fill));
        Assert.Equal(1, m.Renders);
    }

    [AvaloniaFact]
    public void The_elements_own_brush_wins_over_a_token_and_the_token_returns_when_it_goes()
    {
        Action<bool>? setOwn = null;
        using var m = new Mounted(ctx =>
        {
            var (own, set) = ctx.UseState(true);
            setOwn = set;
            return CalloutCard("h", TextBlock("c")).Accent(Theme.Accent).Accent(own ? Brushes.Red : null);
        });
        var card = m.One<CalloutCardControl>();
        Assert.Same(Brushes.Red, card.Accent);

        setOwn!(false);
        Mounted.Pump();

        Assert.Equal(ColorOf(ThemeRef.Resolve(Theme.Accent.ResourceKey, isDark: false)), ColorOf(card.Accent));
    }

    [AvaloniaFact]
    public void A_brush_modifier_reaches_a_templated_control()
    {
        using var m = new Mounted(_ => CalloutCard("h", TextBlock("c")).Background(Brushes.Blue).Foreground(Brushes.Green));
        var card = m.One<CalloutCardControl>();
        Assert.Same(Brushes.Blue, card.Background);
        Assert.Same(Brushes.Green, card.Foreground);
    }

    [AvaloniaFact]
    public void A_token_for_a_property_the_control_lacks_is_skipped()
    {
        // RatingBar is a plain Control: no Background. As on Isotope's own controls, the binding is skipped.
        using var m = new Mounted(_ => RatingBar(3).Background(Theme.CardBackground));
        var bar = m.One<RatingBarControl>(); // mounted, not replaced by the host's error text
        Assert.Equal(3, bar.Value);
        Assert.Null(AvaloniaPropertyRegistry.Instance.FindRegistered(bar, "Background"));
        Assert.Same(Brushes.Goldenrod, bar.Fill); // the token didn't land on its brush property either
    }

    [AvaloniaFact]
    public void A_token_resolves_for_a_theme_from_outside_the_library()
    {
        // The public overload resolves for the app's light or dark theme; the one for a given control is internal.
        Assert.NotEqual(ColorOf(ThemeRef.Resolve(Theme.Accent.ResourceKey, isDark: false)),
            ColorOf(ThemeRef.Resolve(Theme.Accent.ResourceKey, isDark: true)));
    }
}
