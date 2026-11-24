using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Isotope.UI.Core;

// Isotope's built-in theme tokens (tier 3, #41): the values behind upstream's Theme tokens, which WinUI supplies from
// its own theme resources and Avalonia's Fluent theme doesn't define. Used when the resources a control (or the app)
// sees don't define the key, so an app overrides any token by defining its key in its resources.

/// <summary>
/// Resolves a theme resource key to a brush on Avalonia: from the resources a host (a control or the application) sees
/// for a theme variant, else from the built-in table of WinUI's Fluent token values, for light, dark and high contrast.
/// </summary>
internal static class ThemeTokens
{
    /// <summary>The brush for <paramref name="key"/>, or null when neither the resources nor the table have it.</summary>
    /// <param name="host">Where resources are looked up (with its ancestors, the application and its styles); null: none.</param>
    /// <param name="key">A WinUI resource key: a brush (<c>TextFillColorPrimaryBrush</c>) or a colour (<c>SystemAccentColor</c>).</param>
    /// <param name="variant">The theme variant to resolve for.</param>
    /// <param name="highContrast">Whether the platform asks for high contrast: the table gives its high-contrast colours.</param>
    public static IBrush? Resolve(IResourceHost? host, string key, ThemeVariant variant, bool highContrast)
    {
        if (host is not null && host.TryFindResource(key, variant, out var value) && ToBrush(value) is { } found)
            return found;
        return Builtin(host, key, variant, highContrast);
    }

    /// <summary>Whether <paramref name="variant"/> is dark: itself, or the variant it inherits from.</summary>
    public static bool IsDark(ThemeVariant? variant)
    {
        for (var v = variant; v is not null; v = v.InheritVariant)
        {
            if (v == ThemeVariant.Dark) return true;
            if (v == ThemeVariant.Light) return false;
        }
        return false;
    }

    /// <summary>Whether <paramref name="values"/> ask for high contrast.</summary>
    public static bool IsHighContrast(PlatformColorValues? values) => values?.ContrastPreference == ColorContrastPreference.High;

    // ── The table ──

    // A colour as WinUI's Fluent theme defines it: a fixed ARGB value, or a shade of the accent colour with an opacity.
    private readonly record struct Spec(uint Argb, Shade Shade = Shade.None, double Opacity = 1);

    private enum Shade { None, Accent, Light1, Light2, Light3, Dark1, Dark2, Dark3 }

    // The system colour a token takes in high contrast, as WinUI's high-contrast theme maps them; Keep keeps the
    // light or dark value (transparent fills, the smoke overlay).
    private enum Hc { Keep, Window, WindowText, Highlight, HighlightText, GrayText, ButtonFace, ButtonText }

    private readonly record struct Token(Spec Light, Spec Dark, Hc HighContrast);

    private static Spec C(uint argb) => new(argb);

    private static Spec A(Shade shade, double opacity = 1) => new(0, shade, opacity);

    // WinUI's Fluent values (Microsoft.UI.Xaml's theme resources, MIT), by key without its Brush suffix. Upstream's 36
    // Theme tokens, the other keys upstream's samples name through Theme.Ref, and their siblings.
    private static readonly Dictionary<string, Token> s_tokens = new(StringComparer.Ordinal)
    {
        // Text
        ["TextFillColorPrimary"] = new(C(0xE4000000), C(0xFFFFFFFF), Hc.WindowText),
        ["TextFillColorSecondary"] = new(C(0x9E000000), C(0xC5FFFFFF), Hc.WindowText),
        ["TextFillColorTertiary"] = new(C(0x72000000), C(0x87FFFFFF), Hc.WindowText),
        ["TextFillColorDisabled"] = new(C(0x5C000000), C(0x5DFFFFFF), Hc.GrayText),
        ["TextFillColorInverse"] = new(C(0xFFFFFFFF), C(0xE4000000), Hc.Window),
        ["AccentTextFillColorPrimary"] = new(A(Shade.Dark2), A(Shade.Light3), Hc.Highlight),
        ["AccentTextFillColorSecondary"] = new(A(Shade.Dark3), A(Shade.Light3), Hc.Highlight),
        ["AccentTextFillColorTertiary"] = new(A(Shade.Dark1), A(Shade.Light2), Hc.Highlight),
        ["AccentTextFillColorDisabled"] = new(C(0x5C000000), C(0x5DFFFFFF), Hc.GrayText),
        ["TextOnAccentFillColorPrimary"] = new(C(0xFFFFFFFF), C(0xFF000000), Hc.HighlightText),
        ["TextOnAccentFillColorSecondary"] = new(C(0xB3FFFFFF), C(0x80000000), Hc.HighlightText),
        ["TextOnAccentFillColorDisabled"] = new(C(0xFFFFFFFF), C(0x87FFFFFF), Hc.GrayText),
        ["TextOnAccentFillColorSelectedText"] = new(C(0xFFFFFFFF), C(0xFFFFFFFF), Hc.HighlightText),

        // Accent fills
        ["AccentFillColorDefault"] = new(A(Shade.Dark1), A(Shade.Light2), Hc.Highlight),
        ["AccentFillColorSecondary"] = new(A(Shade.Dark1, 0.9), A(Shade.Light2, 0.9), Hc.Highlight),
        ["AccentFillColorTertiary"] = new(A(Shade.Dark1, 0.8), A(Shade.Light2, 0.8), Hc.Highlight),
        ["AccentFillColorDisabled"] = new(C(0x37000000), C(0x28FFFFFF), Hc.GrayText),
        ["AccentFillColorSelectedTextBackground"] = new(A(Shade.Accent), A(Shade.Accent), Hc.Highlight),

        // Control fills
        ["ControlFillColorDefault"] = new(C(0xB3FFFFFF), C(0x0FFFFFFF), Hc.ButtonFace),
        ["ControlFillColorSecondary"] = new(C(0x80F9F9F9), C(0x15FFFFFF), Hc.ButtonFace),
        ["ControlFillColorTertiary"] = new(C(0x4DF9F9F9), C(0x08FFFFFF), Hc.ButtonFace),
        ["ControlFillColorDisabled"] = new(C(0x4DF9F9F9), C(0x0BFFFFFF), Hc.ButtonFace),
        ["ControlFillColorTransparent"] = new(C(0x00FFFFFF), C(0x00FFFFFF), Hc.Keep),
        ["ControlFillColorInputActive"] = new(C(0xFFFFFFFF), C(0xB31E1E1E), Hc.Window),
        ["ControlStrongFillColorDefault"] = new(C(0x72000000), C(0x8BFFFFFF), Hc.ButtonText),
        ["ControlStrongFillColorDisabled"] = new(C(0x51000000), C(0x3FFFFFFF), Hc.GrayText),
        ["ControlSolidFillColorDefault"] = new(C(0xFFFFFFFF), C(0xFF454545), Hc.ButtonFace),
        ["ControlOnImageFillColorDefault"] = new(C(0xC9FFFFFF), C(0xB31C1C1C), Hc.Window),

        // Subtle fills
        ["SubtleFillColorTransparent"] = new(C(0x00FFFFFF), C(0x00FFFFFF), Hc.Keep),
        ["SubtleFillColorSecondary"] = new(C(0x09000000), C(0x0FFFFFFF), Hc.Window),
        ["SubtleFillColorTertiary"] = new(C(0x06000000), C(0x0AFFFFFF), Hc.Window),
        ["SubtleFillColorDisabled"] = new(C(0x00FFFFFF), C(0x00FFFFFF), Hc.Keep),

        // Strokes
        ["ControlStrokeColorDefault"] = new(C(0x0F000000), C(0x12FFFFFF), Hc.ButtonText),
        ["ControlStrokeColorSecondary"] = new(C(0x29000000), C(0x18FFFFFF), Hc.ButtonText),
        ["ControlStrongStrokeColorDefault"] = new(C(0x72000000), C(0x8BFFFFFF), Hc.ButtonText),
        ["ControlStrongStrokeColorDisabled"] = new(C(0x37000000), C(0x28FFFFFF), Hc.GrayText),
        ["CardStrokeColorDefault"] = new(C(0x0F000000), C(0x19000000), Hc.WindowText),
        ["CardStrokeColorDefaultSolid"] = new(C(0xFFEBEBEB), C(0xFF1C1C1C), Hc.WindowText),
        ["SurfaceStrokeColorDefault"] = new(C(0x66757575), C(0x66757575), Hc.WindowText),
        ["SurfaceStrokeColorFlyout"] = new(C(0x0F000000), C(0x33000000), Hc.WindowText),
        ["DividerStrokeColorDefault"] = new(C(0x0F000000), C(0x15FFFFFF), Hc.WindowText),
        ["FocusStrokeColorOuter"] = new(C(0xE4000000), C(0xFFFFFFFF), Hc.WindowText),
        ["FocusStrokeColorInner"] = new(C(0xB3FFFFFF), C(0xB3000000), Hc.Window),

        // Surfaces
        ["CardBackgroundFillColorDefault"] = new(C(0xB3FFFFFF), C(0x0DFFFFFF), Hc.Window),
        ["CardBackgroundFillColorSecondary"] = new(C(0x80F6F6F6), C(0x08FFFFFF), Hc.Window),
        ["SmokeFillColorDefault"] = new(C(0x4D000000), C(0x4D000000), Hc.Keep),
        ["LayerFillColorDefault"] = new(C(0x80FFFFFF), C(0x4C3A3A3A), Hc.Window),
        ["LayerFillColorAlt"] = new(C(0xFFFFFFFF), C(0x0DFFFFFF), Hc.Window),
        ["SolidBackgroundFillColorBase"] = new(C(0xFFF3F3F3), C(0xFF202020), Hc.Window),
        ["SolidBackgroundFillColorSecondary"] = new(C(0xFFEEEEEE), C(0xFF1C1C1C), Hc.Window),
        ["SolidBackgroundFillColorTertiary"] = new(C(0xFFF9F9F9), C(0xFF282828), Hc.Window),
        ["SolidBackgroundFillColorQuarternary"] = new(C(0xFFFFFFFF), C(0xFF2C2C2C), Hc.Window),

        // Signal
        ["SystemFillColorAttention"] = new(A(Shade.Dark1), A(Shade.Light2), Hc.Highlight),
        ["SystemFillColorSuccess"] = new(C(0xFF0F7B0F), C(0xFF6CCB5F), Hc.WindowText),
        ["SystemFillColorCaution"] = new(C(0xFF9D5D00), C(0xFFFCE100), Hc.WindowText),
        ["SystemFillColorCritical"] = new(C(0xFFC42B1C), C(0xFFFF99A4), Hc.WindowText),
        ["SystemFillColorNeutral"] = new(C(0x72000000), C(0x8BFFFFFF), Hc.WindowText),
        ["SystemFillColorSolidNeutral"] = new(C(0xFF8A8A8A), C(0xFF9D9D9D), Hc.WindowText),
        ["SystemFillColorAttentionBackground"] = new(C(0x80F6F6F6), C(0x08FFFFFF), Hc.Window),
        ["SystemFillColorSuccessBackground"] = new(C(0xFFDFF6DD), C(0xFF393D1B), Hc.Window),
        ["SystemFillColorCautionBackground"] = new(C(0xFFFFF4CE), C(0xFF433519), Hc.Window),
        ["SystemFillColorCriticalBackground"] = new(C(0xFFFDE7E9), C(0xFF442726), Hc.Window),
        ["SystemFillColorNeutralBackground"] = new(C(0x06000000), C(0x08FFFFFF), Hc.Window),
        ["SystemFillColorSolidAttentionBackground"] = new(C(0xFFF7F7F7), C(0xFF2E2E2E), Hc.Window),
        ["SystemFillColorSolidNeutralBackground"] = new(C(0xFFF3F3F3), C(0xFF2E2E2E), Hc.Window),

        // The accent colour and its shades, as keys of their own.
        ["SystemAccentColor"] = new(A(Shade.Accent), A(Shade.Accent), Hc.Highlight),
        ["SystemAccentColorLight1"] = new(A(Shade.Light1), A(Shade.Light1), Hc.Highlight),
        ["SystemAccentColorLight2"] = new(A(Shade.Light2), A(Shade.Light2), Hc.Highlight),
        ["SystemAccentColorLight3"] = new(A(Shade.Light3), A(Shade.Light3), Hc.Highlight),
        ["SystemAccentColorDark1"] = new(A(Shade.Dark1), A(Shade.Dark1), Hc.Highlight),
        ["SystemAccentColorDark2"] = new(A(Shade.Dark2), A(Shade.Dark2), Hc.Highlight),
        ["SystemAccentColorDark3"] = new(A(Shade.Dark3), A(Shade.Dark3), Hc.Highlight),
    };

    /// <summary>The keys the table holds (without the Brush suffix), for tests.</summary>
    internal static IReadOnlyCollection<string> BuiltinKeys => s_tokens.Keys;

    // Windows' High Contrast Black (on a dark variant) and High Contrast White (on a light one) system colours: Avalonia
    // exposes the contrast preference but not the user's high-contrast palette.
    private static Color HighContrastColor(Hc role, bool dark) => Color.FromUInt32(role switch
    {
        Hc.Window or Hc.ButtonFace => dark ? 0xFF000000 : 0xFFFFFFFF,
        Hc.WindowText or Hc.ButtonText => dark ? 0xFFFFFFFF : 0xFF000000,
        Hc.Highlight => dark ? 0xFF1AEBFF : 0xFF37006E,
        Hc.HighlightText => dark ? 0xFF000000 : 0xFFFFFFFF,
        Hc.GrayText => dark ? 0xFF3FF23F : 0xFF600000,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    });

    private static IBrush? Builtin(IResourceHost? host, string key, ThemeVariant variant, bool highContrast)
    {
        var name = key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^"Brush".Length] : key;
        if (!s_tokens.TryGetValue(name, out var token)) return null;
        bool dark = IsDark(variant);
        if (highContrast && token.HighContrast != Hc.Keep)
            return SolidBrush(HighContrastColor(token.HighContrast, dark), 1);
        var spec = dark ? token.Dark : token.Light;
        var color = spec.Shade == Shade.None ? Color.FromUInt32(spec.Argb) : AccentShade(host, variant, spec.Shade);
        return SolidBrush(color, spec.Opacity);
    }

    // ── The accent colour ──

    /// <summary>The accent colour WinUI's tokens and Avalonia's Fluent theme both start from (Windows' default blue).</summary>
    internal static readonly Color DefaultAccent = Color.FromUInt32(0xFF0078D4);

    // A shade of the accent: the Fluent theme's resource for it (SystemAccentColorDark1…), which follows the platform's
    // accent; with no such resource, derived from the accent colour (the resource, the platform's, or the default).
    private static Color AccentShade(IResourceHost? host, ThemeVariant variant, Shade shade)
    {
        var suffix = shade == Shade.Accent ? "" : shade.ToString();
        if (host is not null && host.TryFindResource("SystemAccentColor" + suffix, variant, out var value) && value is Color c)
            return c;
        var accent = host is not null && host.TryFindResource("SystemAccentColor", variant, out var a) && a is Color ac
            ? ac
            : PlatformAccent() ?? DefaultAccent;
        return Derive(accent, shade);
    }

    private static Color? PlatformAccent()
    {
        var app = Application.Current;
        if (app is null || !app.CheckAccess()) return null;
        return app.PlatformSettings?.GetColorValues().AccentColor1 is { A: > 0 } accent ? accent : null;
    }

    // Lighter shades move the lightness toward white and darker ones toward black, by about the steps of Windows'
    // default accent palette.
    private static Color Derive(Color accent, Shade shade)
    {
        if (shade == Shade.Accent) return accent;
        var hsl = accent.ToHsl();
        double l = shade switch
        {
            Shade.Light1 => hsl.L + (1 - hsl.L) * 0.15,
            Shade.Light2 => hsl.L + (1 - hsl.L) * 0.45,
            Shade.Light3 => hsl.L + (1 - hsl.L) * 0.65,
            Shade.Dark1 => hsl.L * 0.87,
            Shade.Dark2 => hsl.L * 0.70,
            Shade.Dark3 => hsl.L * 0.50,
            _ => hsl.L,
        };
        return new HslColor(hsl.A, hsl.H, hsl.S, l).ToRgb();
    }

    // ── Brushes ──

    private static readonly ConcurrentDictionary<(Color Color, double Opacity), IBrush> s_brushes = new();

    // One immutable brush per colour and opacity, so a value resolved again is the same brush.
    private static IBrush SolidBrush(Color color, double opacity) =>
        s_brushes.GetOrAdd((color, opacity), static k => new ImmutableSolidColorBrush(k.Color, k.Opacity));

    private static IBrush? ToBrush(object? value) => value switch
    {
        IBrush brush => brush,
        Color color => SolidBrush(color, 1),
        _ => null,
    };
}
