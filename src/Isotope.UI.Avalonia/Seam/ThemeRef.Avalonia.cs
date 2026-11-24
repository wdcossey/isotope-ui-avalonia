using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Isotope.UI.Hosting;

namespace Isotope.UI.Core;

// Our part (tier 3, #41) of upstream's ThemeRef (src/Reactor/Core/Theme.cs, vendored with its WinUI resolution
// dropped): how a key resolves on Avalonia. Upstream's Theme tokens, Theme.Ref and the resolution cache are vendored.

public readonly partial record struct ThemeRef
{
    /// <summary>
    /// Resolves <paramref name="resourceKey"/> for <paramref name="element"/>: from the resources it sees (its own, its
    /// ancestors', the application's and its styles', such as the Fluent theme) for its actual theme variant, else from
    /// Isotope's built-in tokens, in high contrast when the platform asks for it. Upstream's member, on the UI thread.
    /// </summary>
    internal static IBrush? Resolve(string resourceKey, FrameworkElement element) =>
        ThemeTokens.Resolve(element, resourceKey, element.ActualThemeVariant, IsHighContrast(element));

    // Resolve(string, bool) (vendored): the application's resources for that variant, else the built-in tokens, cached
    // as upstream caches. As upstream, nothing is resolved (or cached) before there is an application.
    private static IBrush? ResolveForTheme(string resourceKey, string themeName)
    {
        if (Application.Current is not { } app) return null;
        // Off the UI thread before anything subscribed, nothing would clear what is cached: resolve, don't cache.
        bool watched = WatchApplication(app);
        var cacheKey = (resourceKey, themeName);
        int generation = Volatile.Read(ref s_resolutionGeneration);
        if (TryGetCurrentResolution(cacheKey, generation, out var cached))
            return cached;
        var variant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var resolved = ThemeTokens.Resolve(app, resourceKey, variant, ThemeTokens.IsHighContrast(app.PlatformSettings?.GetColorValues()));
        // As upstream: only a found brush is cached, so a key added to the resources later is found then.
        if (resolved is not null && watched)
            s_resolutionCache[cacheKey] = (generation, resolved);
        return resolved;
    }

    // The application whose changes clear the cache: its resources (and its styles', such as the Fluent theme's
    // accent colours) and the platform's colour values (the accent, the contrast preference). Upstream's hosts clear it
    // on their theme-change events; here the application's own events do, once per application.
    private static Application? s_watched;

    // Whether the cache is cleared on app's changes. Resolve is called on the UI thread, as upstream's; a call from
    // elsewhere resolves but doesn't subscribe.
    private static bool WatchApplication(Application app)
    {
        if (ReferenceEquals(Volatile.Read(ref s_watched), app)) return true;
        if (!app.CheckAccess()) return false;
        // What was cached before (for an application no longer current) isn't covered by its events.
        InvalidateResolutionCache();
        Volatile.Write(ref s_watched, app);
        app.ResourcesChanged += static (_, _) => InvalidateResolutionCache();
        if (app.PlatformSettings is { } settings)
            settings.ColorValuesChanged += static (_, _) => InvalidateResolutionCache();
        return true;
    }

    /// <summary>
    /// Whether the platform asks <paramref name="element"/> for high contrast: the colour values of the host it is in
    /// (a test's stand-in, see <see cref="ReactorHostControl"/>), else the application's platform settings.
    /// </summary>
    internal static bool IsHighContrast(Visual element) => ThemeTokens.IsHighContrast(ColorValuesSource(element)?.GetColorValues());

    /// <summary>Where <paramref name="element"/>'s colour values come from; null with no application.</summary>
    internal static IColorValuesSource? ColorValuesSource(Visual element)
    {
        if (element.FindAncestorOfType<ReactorHostControl>(includeSelf: true)?.ColorValuesOverride is { } fake) return fake;
        return Application.Current is { } app && app.CheckAccess() && app.PlatformSettings is { } settings
            ? new PlatformColorValuesSource(settings)
            : null;
    }

    private sealed class PlatformColorValuesSource(IPlatformSettings settings) : IColorValuesSource
    {
        public PlatformColorValues GetColorValues() => settings.GetColorValues();

        public event EventHandler<PlatformColorValues>? ColorValuesChanged
        {
            add => settings.ColorValuesChanged += value;
            remove => settings.ColorValuesChanged -= value;
        }
    }
}
