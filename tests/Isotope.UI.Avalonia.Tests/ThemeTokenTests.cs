using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Theme tokens (#41), headless: upstream's Theme tokens resolve per theme (light, dark, high contrast, the Fluent
/// theme's accent, an app's override), bound tokens follow a theme switch without a re-render, and a component that
/// resolves a token in its render re-renders on a switch. Hosts set the process-wide UI dispatcher, so these run alone.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class ThemeTokenTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly List<Window> _windows = new();
    private readonly List<ReactorHostControl> _hosts = new();

    public void Dispose()
    {
        foreach (var h in _hosts) h.Dispose();
        foreach (var w in _windows) w.Close();
        Pump();
        ReactorApp.UIDispatcher = _previousDispatcher;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class Mounted
    {
        public required Window Window;
        public required ReactorHostControl Host;
        public int Renders;

        public List<T> All<T>() where T : Control
        {
            var found = new List<T>();
            void Walk(Control? c)
            {
                if (c is T t) found.Add(t);
                switch (c)
                {
                    case Panel p: foreach (var child in p.Children) Walk(child); break;
                    case Decorator d: Walk(d.Child); break;
                    case ContentControl { Content: Control content }: Walk(content); break;
                }
            }
            Walk(Host.Child);
            return found;
        }

        public T First<T>() where T : Control => All<T>()[0];
    }

    private Mounted Mount(Func<RenderContext, Element> render, ThemeVariant? theme = null, Action<ReactorHostControl>? setUp = null)
    {
        var window = new Window { Width = 400, Height = 300 };
        _windows.Add(window);
        if (theme is not null) window.RequestedThemeVariant = theme;
        Mounted? m = null;
        var host = new ReactorHostControl(ctx =>
        {
            m!.Renders++;
            return render(ctx);
        });
        _hosts.Add(host);
        setUp?.Invoke(host);
        m = new Mounted { Window = window, Host = host };
        window.Content = host;
        window.Show();
        Pump();
        return m;
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double OpacityOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Opacity;

    private static Color Hex(uint argb) => Color.FromUInt32(argb);

    // The colour a resource of the Fluent theme has for a variant.
    private static Color FluentColor(Window window, string key, ThemeVariant variant)
    {
        Assert.True(window.TryFindResource(key, variant, out var value), key + " is a Fluent theme resource");
        return Assert.IsType<Color>(value);
    }

    // ── Token values per theme ──

    [AvaloniaTheory]
    [InlineData("Light", 0xE4000000u, 0x9E000000u, 0xFFF3F3F3u)]
    [InlineData("Dark", 0xFFFFFFFFu, 0xC5FFFFFFu, 0xFF202020u)]
    public void Tokens_take_WinUIs_Fluent_values_for_the_controls_theme(string theme, uint primary, uint secondary, uint background)
    {
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var m = Mount(_ => VStack(
            TextBlock("a").Foreground(Theme.PrimaryText),
            TextBlock("b").Foreground(Theme.SecondaryText)).Background(Theme.SolidBackground), variant);
        var texts = m.All<TextBlock>();
        Assert.Equal(Hex(primary), ColorOf(texts[0].Foreground));
        Assert.Equal(Hex(secondary), ColorOf(texts[1].Foreground));
        Assert.Equal(Hex(background), ColorOf(m.First<StackPanel>().Background));
    }

    [AvaloniaFact]
    public void Accent_tokens_take_the_Fluent_themes_accent_shades()
    {
        var light = Mount(_ => TextBlock("a").Background(Theme.Accent).Foreground(Theme.AccentText), ThemeVariant.Light);
        var dark = Mount(_ => TextBlock("a").Background(Theme.AccentSecondary).Foreground(Theme.AccentText), ThemeVariant.Dark);
        var l = light.First<TextBlock>();
        var d = dark.First<TextBlock>();
        Assert.Equal(FluentColor(light.Window, "SystemAccentColorDark1", ThemeVariant.Light), ColorOf(l.Background));
        Assert.Equal(FluentColor(light.Window, "SystemAccentColorDark2", ThemeVariant.Light), ColorOf(l.Foreground));
        // AccentSecondary: the default accent fill at 90 %.
        Assert.Equal(FluentColor(dark.Window, "SystemAccentColorLight2", ThemeVariant.Dark), ColorOf(d.Background));
        Assert.Equal(0.9, OpacityOf(d.Background), 3);
        Assert.Equal(FluentColor(dark.Window, "SystemAccentColorLight3", ThemeVariant.Dark), ColorOf(d.Foreground));
    }

    [AvaloniaFact]
    public void Every_Theme_token_has_a_value_in_both_themes()
    {
        var tokens = typeof(Theme).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ThemeRef))
            .Select(p => (ThemeRef)p.GetValue(null)!)
            .ToList();
        Assert.Equal(36, tokens.Count);
        foreach (var token in tokens)
        {
            Assert.NotNull(ThemeTokens.Resolve(null, token.ResourceKey, ThemeVariant.Light, highContrast: false));
            Assert.NotNull(ThemeTokens.Resolve(null, token.ResourceKey, ThemeVariant.Dark, highContrast: false));
        }
        Assert.Null(ThemeTokens.Resolve(null, "NoSuchKeyBrush", ThemeVariant.Light, highContrast: false));
    }

    [AvaloniaFact]
    public void Without_theme_resources_an_accent_token_derives_from_the_accent_colour()
    {
        // No resource host: the default accent, and a darker shade of it for the light theme's accent fill.
        var accent = ColorOf(ThemeTokens.Resolve(null, "SystemAccentColor", ThemeVariant.Light, highContrast: false));
        // The same hue (within the rounding of 8-bit channels), darker for light and lighter for dark.
        Assert.InRange(ColorOf(ThemeTokens.Resolve(null, "AccentFillColorDefaultBrush", ThemeVariant.Light, false)).ToHsl().H - accent.ToHsl().H, -2, 2);
        Assert.True(ColorOf(ThemeTokens.Resolve(null, "AccentFillColorDefaultBrush", ThemeVariant.Light, false)).ToHsl().L < accent.ToHsl().L);
        Assert.True(ColorOf(ThemeTokens.Resolve(null, "AccentFillColorDefaultBrush", ThemeVariant.Dark, false)).ToHsl().L > accent.ToHsl().L);
    }

    [AvaloniaFact]
    public void An_apps_resource_overrides_a_token_and_its_removal_restores_it()
    {
        var m = Mount(_ => TextBlock("a").Foreground(Theme.SecondaryText), ThemeVariant.Light);
        var text = m.First<TextBlock>();
        Assert.Equal(Hex(0x9E000000), ColorOf(text.Foreground));

        m.Window.Resources["TextFillColorSecondaryBrush"] = Brushes.Red;
        Pump();
        Assert.Equal(Colors.Red, ColorOf(text.Foreground));

        m.Window.Resources.Remove("TextFillColorSecondaryBrush");
        Pump();
        Assert.Equal(Hex(0x9E000000), ColorOf(text.Foreground));
        Assert.Equal(1, m.Renders);
    }

    private sealed class FakeColorValues : IColorValuesSource
    {
        public PlatformColorValues Values = new() { ThemeVariant = PlatformThemeVariant.Light };
        public int Handlers;
        private EventHandler<PlatformColorValues>? _changed;

        public PlatformColorValues GetColorValues() => Values;

        public event EventHandler<PlatformColorValues>? ColorValuesChanged
        {
            add { _changed += value; Handlers++; }
            remove { _changed -= value; Handlers--; }
        }

        public void Set(ColorContrastPreference contrast)
        {
            Values = Values with { ContrastPreference = contrast };
            _changed?.Invoke(this, Values);
        }
    }

    [AvaloniaFact]
    public void High_contrast_gives_the_high_contrast_colours_and_follows_the_preference()
    {
        var fake = new FakeColorValues();
        fake.Set(ColorContrastPreference.High);
        var m = Mount(_ => VStack(TextBlock("a").Foreground(Theme.SecondaryText)).Background(Theme.CardBackground),
            ThemeVariant.Dark, h => h.ColorValuesOverride = fake);
        var text = m.First<TextBlock>();
        var panel = m.First<StackPanel>();
        // High Contrast Black: white text on black.
        Assert.Equal(Colors.White, ColorOf(text.Foreground));
        Assert.Equal(Colors.Black, ColorOf(panel.Background));
        Assert.Equal(2, fake.Handlers);

        fake.Set(ColorContrastPreference.NoPreference);
        Pump();
        Assert.Equal(Hex(0xC5FFFFFF), ColorOf(text.Foreground));
        Assert.Equal(Hex(0x0DFFFFFF), ColorOf(panel.Background));

        m.Window.RequestedThemeVariant = ThemeVariant.Light;
        fake.Set(ColorContrastPreference.High);
        Pump();
        // High Contrast White: black text on white.
        Assert.Equal(Colors.Black, ColorOf(text.Foreground));
        Assert.Equal(Colors.White, ColorOf(panel.Background));
        Assert.Equal(1, m.Renders);

        // Out of the window, the controls stop watching the platform.
        m.Window.Content = null;
        Pump();
        Assert.Equal(0, fake.Handlers);
    }

    // ── A theme switch ──

    [AvaloniaFact]
    public void A_theme_switch_updates_bound_tokens_without_a_rerender()
    {
        var m = Mount(ThemeTokenExamples.Card, ThemeVariant.Light);
        var border = m.First<Border>();
        var texts = m.All<TextBlock>();
        Assert.Equal(Hex(0xB3FFFFFF), ColorOf(border.Background));
        Assert.Equal(Hex(0x0F000000), ColorOf(border.BorderBrush));
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(Hex(0xE4000000), ColorOf(texts[0].Foreground));

        m.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Pump();
        Assert.Equal(Hex(0x0DFFFFFF), ColorOf(border.Background));
        Assert.Equal(Hex(0x19000000), ColorOf(border.BorderBrush));
        Assert.Equal(Hex(0xFFFFFFFF), ColorOf(texts[0].Foreground));
        Assert.Equal(Hex(0xC5FFFFFF), ColorOf(texts[1].Foreground));
        Assert.Equal(1, m.Renders);

        // A re-render that changes the count keeps the bindings (and the same controls).
        TestHost.Click(m.First<Button>());
        Pump();
        Assert.Equal(2, m.Renders);
        Assert.Same(border, m.First<Border>());
        Assert.Equal(Hex(0xC5FFFFFF), ColorOf(m.All<TextBlock>()[1].Foreground));
    }

    [AvaloniaFact]
    public void A_theme_switch_rerenders_a_component_that_resolves_a_token_in_its_render()
    {
        var m = Mount(ThemeTokenExamples.Swatch, ThemeVariant.Light);
        var light = m.First<TextBlock>();
        Assert.Equal("Accent on light", light.Text);
        Assert.Equal(FluentColor(m.Window, "SystemAccentColorDark1", ThemeVariant.Light), ColorOf(light.Background));

        m.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Pump();
        var dark = m.First<TextBlock>();
        Assert.Equal(2, m.Renders);
        Assert.Equal("Accent on dark", dark.Text);
        Assert.Equal(FluentColor(m.Window, "SystemAccentColorLight2", ThemeVariant.Dark), ColorOf(dark.Background));
    }

    // ── Resolve in a render ──

    [AvaloniaFact]
    public void Resolve_reads_the_apps_resources_per_theme_and_sees_a_resource_added_later()
    {
        Assert.Equal(Hex(0xE4000000), ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: false)));
        Assert.Equal(Hex(0xFFFFFFFF), ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: true)));
        Assert.Null(ThemeRef.Resolve("IsotopeTestNoSuchBrush", isDark: false));

        var app = Application.Current!;
        try
        {
            // A key the table lacks, added to the app's resources after a miss; and a token the app overrides after
            // it was resolved (and cached).
            app.Resources["IsotopeTestNoSuchBrush"] = Brushes.Green;
            app.Resources["TextFillColorPrimaryBrush"] = Brushes.Blue;
            Assert.Equal(Colors.Green, ColorOf(ThemeRef.Resolve("IsotopeTestNoSuchBrush", isDark: false)));
            Assert.Equal(Colors.Blue, ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: false)));
        }
        finally
        {
            app.Resources.Remove("IsotopeTestNoSuchBrush");
            app.Resources.Remove("TextFillColorPrimaryBrush");
        }
        Assert.Equal(Hex(0xE4000000), ColorOf(ThemeRef.Resolve(Theme.PrimaryText.ResourceKey, isDark: false)));
    }

    [AvaloniaFact]
    public void NotifyResourcesChanged_rerenders_every_live_host()
    {
        var a = Mount(_ => TextBlock("a"));
        var b = Mount(_ => TextBlock("b"));
        Theme.NotifyResourcesChanged();
        Pump();
        Assert.Equal(2, a.Renders);
        Assert.Equal(2, b.Renders);

        // A disposed host is no longer notified.
        a.Host.Dispose();
        Theme.NotifyResourcesChanged();
        Pump();
        Assert.Equal(2, a.Renders);
        Assert.Equal(3, b.Renders);
    }

    [AvaloniaFact]
    public void ThemeResource_reads_the_Fluent_themes_corner_radii_and_brushes_with_the_tokens_behind_them()
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("ControlCornerRadius", app.ActualThemeVariant, out var control));
        Assert.Equal(Assert.IsType<CornerRadius>(control), ThemeResource.CornerRadius("ControlCornerRadius"));
        Assert.True(app.TryGetResource("OverlayCornerRadius", app.ActualThemeVariant, out var overlay));
        Assert.Equal(Assert.IsType<CornerRadius>(overlay), ThemeResource.CornerRadius("OverlayCornerRadius"));
        Assert.Throws<KeyNotFoundException>(() => ThemeResource.CornerRadius("IsotopeTestNoSuchRadius"));

        // A key the Fluent theme lacks: Isotope's token, for the app's theme.
        var expected = ThemeTokens.IsDark(app.ActualThemeVariant) ? 0xFF6CCB5Fu : 0xFF0F7B0Fu;
        Assert.Equal(Hex(expected), ColorOf(ThemeResource.Brush("SystemFillColorSuccessBrush")));
        Assert.Throws<KeyNotFoundException>(() => ThemeResource.Brush("IsotopeTestNoSuchBrush"));
        Assert.Equal(7.5, ThemeResource.Get("IsotopeTestNoSuchDouble", 7.5));
    }

    [AvaloniaFact]
    public void NotifyResourcesChanged_resolves_again_a_token_a_memoized_component_reads()
    {
        var m = Mount(_ => VStack(Memo(_ =>
            TextBlock("memo").Background(ThemeRef.Resolve("IsotopeTestBrandBrush", isDark: false)), 1)));
        Assert.Null(m.First<TextBlock>().Background);

        var app = Application.Current!;
        try
        {
            app.Resources["IsotopeTestBrandBrush"] = Brushes.Orange;
            Theme.NotifyResourcesChanged();
            Pump();
            Assert.Equal(Colors.Orange, ColorOf(m.First<TextBlock>().Background));
        }
        finally
        {
            app.Resources.Remove("IsotopeTestBrandBrush");
        }
    }

    [AvaloniaFact]
    public void NotifyResourcesChanged_resolves_bound_tokens_again_with_no_event_from_the_platform()
    {
        var fake = new FakeColorValues();
        var m = Mount(_ => VStack(Memo(_ => TextBlock("a").Foreground(Theme.SecondaryText), 1)), ThemeVariant.Dark,
            h => h.ColorValuesOverride = fake);
        var text = m.First<TextBlock>();
        Assert.Equal(Hex(0xC5FFFFFF), ColorOf(text.Foreground));

        // The preference changes with no event: only the refresh sees it.
        fake.Values = fake.Values with { ContrastPreference = ColorContrastPreference.High };
        Pump();
        Assert.Equal(Hex(0xC5FFFFFF), ColorOf(text.Foreground));
        Theme.NotifyResourcesChanged();
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.Equal(Colors.White, ColorOf(text.Foreground));

        // Back to no preference, in light (the theme switch raises its own event).
        fake.Values = fake.Values with { ContrastPreference = ColorContrastPreference.NoPreference };
        m.Window.RequestedThemeVariant = ThemeVariant.Light;
        Pump();
        Assert.Equal(Hex(0x9E000000), ColorOf(text.Foreground));
    }

    [AvaloniaFact]
    public void WithBorder_on_a_wrapped_border_element_gives_the_border_its_width()
    {
        var wrapped = new ModifiedElement(Border(TextBlock("in")), new ElementModifiers { Margin = new Thickness(2) })
            .WithBorder(Theme.CardStroke, thickness: 3);
        var m = Mount(_ => wrapped, ThemeVariant.Light);
        var border = m.First<Border>();
        Assert.Equal(new Thickness(3), border.BorderThickness);
        Assert.Equal(Hex(0x0F000000), ColorOf(border.BorderBrush));
    }

    [AvaloniaFact]
    public void A_memo_skips_an_ordinary_root_pass_but_not_a_resource_refresh()
    {
        int memoRenders = 0;
        Element Tree(RenderContext ctx)
        {
            var (n, setN) = ctx.UseState(0);
            return VStack(
                Button($"root {n}", () => setN(n + 1)),
                Memo(_ => { memoRenders++; return TextBlock("memo"); }, 1));
        }
        var m = Mount(Tree);
        Assert.Equal(1, memoRenders);

        TestHost.Click(m.First<Button>());
        Pump();
        Assert.Equal(2, m.Renders);
        Assert.Equal(1, memoRenders);

        Theme.NotifyResourcesChanged();
        Pump();
        Assert.Equal(3, m.Renders);
        Assert.Equal(2, memoRenders);

        TestHost.Click(m.First<Button>());
        Pump();
        Assert.Equal(2, memoRenders);
    }

    [AvaloniaFact]
    public void Tokens_on_a_ModifiedElement_wrapper_reach_its_control_inner_ones_winning()
    {
        var inner = TextBlock("a").Foreground(Theme.SecondaryText);
        var wrapped = new ModifiedElement(inner, new ElementModifiers { Margin = new Thickness(4) })
            .Foreground(Theme.PrimaryText).Background(Theme.CardBackground);
        var m = Mount(_ => wrapped, ThemeVariant.Light);
        var text = m.First<TextBlock>();
        Assert.Equal(Hex(0x9E000000), ColorOf(text.Foreground));
        Assert.Equal(Hex(0xB3FFFFFF), ColorOf(text.Background));
        Assert.Equal(new Thickness(4), text.Margin);
    }

    [AvaloniaFact]
    public void WithBorder_on_a_component_gives_its_wrapper_border_a_width()
    {
        var m = Mount(_ => Component(_ => TextBlock("inside")).WithBorder(Theme.CardStroke, thickness: 2), ThemeVariant.Light);
        var wrapper = Assert.IsType<Border>(m.Host.Child);
        Assert.Equal(new Thickness(2), wrapper.BorderThickness);
        Assert.Equal(Hex(0x0F000000), ColorOf(wrapper.BorderBrush));
    }

    [AvaloniaFact]
    public void A_binding_to_a_property_that_isnt_a_brush_is_skipped()
    {
        var el = Button("b") with
        {
            ThemeBindings = new Dictionary<string, ThemeRef> { ["Content"] = Theme.Accent, ["Tag"] = Theme.Accent, ["Background"] = Theme.Accent },
        };
        var m = Mount(_ => el, ThemeVariant.Light);
        var button = m.First<Button>();
        Assert.Equal("b", button.Content);
        Assert.Null(button.Tag);
        Assert.Equal(["Background"], Reconciler.BoundThemeProperties(button));
    }

    // ── Precedence and changes ──

    [AvaloniaFact]
    public void A_brush_the_element_sets_wins_and_its_removal_shows_the_token()
    {
        Action<bool>? setOwn = null;
        var m = Mount(ctx =>
        {
            var (own, set) = ctx.UseState(true);
            setOwn = set;
            var el = TextBlock("a").Background(Theme.CardBackground);
            return own ? el.Background(Brushes.Red) : el;
        }, ThemeVariant.Light);
        var text = m.First<TextBlock>();
        Assert.Equal(Colors.Red, ColorOf(text.Background));

        setOwn!(false);
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.Equal(Hex(0xB3FFFFFF), ColorOf(text.Background));
    }

    [AvaloniaFact]
    public void A_changed_or_removed_token_rebinds_or_clears_the_property()
    {
        Action<int>? setStep = null;
        var m = Mount(ctx =>
        {
            var (step, set) = ctx.UseState(0);
            setStep = set;
            return step switch
            {
                0 => TextBlock("a").Foreground(Theme.PrimaryText),
                1 => TextBlock("a").Foreground(Theme.SystemCritical),
                _ => TextBlock("a"),
            };
        }, ThemeVariant.Light);
        var text = m.First<TextBlock>();
        Assert.Equal(Hex(0xE4000000), ColorOf(text.Foreground));

        setStep!(1);
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.Equal(Hex(0xFFC42B1C), ColorOf(text.Foreground));
        Assert.Equal(["Foreground"], Reconciler.BoundThemeProperties(text));

        setStep!(2);
        Pump();
        Assert.Empty(Reconciler.BoundThemeProperties(text));
        // The binding's value is gone: the text block's foreground is its theme's again.
        Assert.NotEqual(Hex(0xFFC42B1C), (text.Foreground as ISolidColorBrush)?.Color);
        Assert.NotEqual(Hex(0xE4000000), (text.Foreground as ISolidColorBrush)?.Color);
    }

    [AvaloniaFact]
    public void WithBorder_on_a_templated_control_sets_its_border_width_and_a_property_it_lacks_is_skipped()
    {
        var m = Mount(_ => VStack(
            Button("b").WithBorder(Theme.ControlStroke, thickness: 3),
            TextBlock("t").BorderBrush(Theme.CardStroke)), ThemeVariant.Light);
        var button = m.First<Button>();
        Assert.Equal(new Thickness(3), button.BorderThickness);
        Assert.Equal(Hex(0x0F000000), ColorOf(button.BorderBrush));
        // A text block has no border brush.
        Assert.Empty(Reconciler.BoundThemeProperties(m.First<TextBlock>()));
    }

    [AvaloniaFact]
    public void PaneBackground_binds_a_split_views_pane_brush()
    {
        var m = Mount(_ => SplitView(TextBlock("pane"), TextBlock("content")).PaneBackground(Theme.LayerFill), ThemeVariant.Dark);
        Assert.Equal(Hex(0x4C3A3A3A), ColorOf(m.First<SplitView>().PaneBackground));
    }

    [AvaloniaFact]
    public void Equal_theme_bindings_let_an_unchanged_element_be_skipped()
    {
        var a = TextBlock("a").Foreground(Theme.PrimaryText);
        var b = TextBlock("a").Foreground(Theme.PrimaryText);
        var c = TextBlock("a").Foreground(Theme.SecondaryText);
        Assert.True(Element.ShallowEquals(a, b));
        Assert.False(Element.ShallowEquals(a, c));
        Assert.False(Element.ShallowEquals(a, TextBlock("a")));
    }
}
