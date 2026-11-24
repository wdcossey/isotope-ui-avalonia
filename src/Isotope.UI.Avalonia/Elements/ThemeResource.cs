using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Isotope.UI.Core;

namespace Isotope.UI;

// Our own version (tier 3, #41) of upstream's src/Reactor/Elements/ThemeResource.cs: the same members, over
// Avalonia's application resources (with its styles', such as the Fluent theme's) and Avalonia's value types.

/// <summary>
/// Reads theme resources by key from the application's resources and its styles (Avalonia's Fluent theme defines
/// <c>ControlCornerRadius</c> and <c>OverlayCornerRadius</c>), for the application's theme:
/// <c>ThemeResource.CornerRadius("ControlCornerRadius")</c>. Call it on the UI thread.
/// </summary>
public static class ThemeResource
{
    /// <summary>
    /// The brush for <paramref name="key"/>; a key the resources lack falls back to Isotope's built-in theme tokens
    /// (WinUI's Fluent values: <c>ThemeResource.Brush("SystemFillColorSuccessBrush")</c>), as <see cref="ThemeRef"/>s do;
    /// with no application, the light theme's.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Neither the resources nor the tokens have a brush of that key.</exception>
    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        var brush = app is null
            ? ThemeTokens.Resolve(null, key, ThemeVariant.Light, highContrast: false)
            : ThemeTokens.Resolve(app, key, app.ActualThemeVariant, ThemeTokens.IsHighContrast(app.PlatformSettings?.GetColorValues()));
        return brush
            ?? throw new KeyNotFoundException($"Theme resource '{key}' not found or is not a Brush");
    }

    /// <exception cref="KeyNotFoundException">No double of that key.</exception>
    public static double Double(string key) => Get(key, double.NaN) is var v && !double.IsNaN(v)
        ? v
        : throw new KeyNotFoundException($"Theme resource '{key}' not found or is not a double");

    /// <summary>A corner radius: <c>ControlCornerRadius</c> (controls) or <c>OverlayCornerRadius</c> (flyouts, cards).</summary>
    /// <exception cref="KeyNotFoundException">No corner radius of that key.</exception>
    public static CornerRadius CornerRadius(string key) => TryFind(key, out var value) && value is CornerRadius cr
        ? cr
        : throw new KeyNotFoundException($"Theme resource '{key}' not found or is not a CornerRadius");

    /// <exception cref="KeyNotFoundException">No thickness of that key.</exception>
    public static Thickness Thickness(string key) => TryFind(key, out var value) && value is Thickness t
        ? t
        : throw new KeyNotFoundException($"Theme resource '{key}' not found or is not a Thickness");

    /// <summary>The resource of that key and type, or <paramref name="defaultValue"/>.</summary>
    public static T Get<T>(string key, T defaultValue = default!) => TryFind(key, out var value) && value is T typed ? typed : defaultValue;

    private static bool TryFind(string key, out object? value)
    {
        value = null;
        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out value);
    }
}
