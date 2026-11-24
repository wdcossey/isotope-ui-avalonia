// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Theme.cs, last changed upstream in 1265b8653d15; transforms: drop-ThemeRef-Resolve-element, drop-ThemeRef-ResolveForTheme, drop-ThemeRef-ResolveForThemeUncached, drop-ThemeRef-ResolveFromDictionaries, drop-ThemeRef-GetEffectiveThemeName, drop-ThemeRef-TryResolveFromThemeDictionaries, drop-ThemeRef-ResolveFromDictionariesForTest, theme-ref-partial, theme-brush-alias, winui-usings, namespace-isotope. Do not edit: change the tools/sync rules.

using Brush = Avalonia.Media.IBrush;

namespace Isotope.UI.Core;

/// <summary>
/// A reference to a WinUI theme resource that resolves at render time.
/// Use via <see cref="Theme"/> tokens or <see cref="Theme.Ref"/> for custom keys.
/// </summary>
// <snippet:theme-ref>
public readonly partial record struct ThemeRef(string ResourceKey)
{
    public override string ToString() => $"ThemeRef({ResourceKey})";













// </snippet:theme-ref>

    /// <summary>
    /// Resolves a theme resource using an explicit isDark flag.
    /// Useful for resolving during Render() before controls are in the tree.
    /// </summary>
    /// <remarks>
    /// The brush is the one a <c>{ThemeResource}</c> reference gets on an element in that theme,
    /// so either theme can be resolved whatever theme the app uses. WinUI does the lookup, so its
    /// rules apply, including the <c>Default</c> fallback. For a key WinUI cannot resolve for that
    /// theme (one an app dictionary defines only for some themes), Reactor reads the app's Light,
    /// Dark and Default theme dictionaries, then does a plain lookup, which uses the app's theme:
    /// a key defined only for the app's theme therefore resolves to that brush for the other
    /// theme too, and a key defined only for the other theme resolves to null for the app's
    /// theme. Returns null when the key is not found or is not a <see cref="Brush"/>. Call it on
    /// the UI thread.
    /// </remarks>
    public static Brush? Resolve(string resourceKey, bool isDark)
    {
        return ResolveForTheme(resourceKey, isDark ? "Dark" : "Light");
    }

    // Issue #660 (#86): cache the (resourceKey, themeName) -> Brush resolution.
    // Resolving costs a WinUI resource lookup on EVERY ThemeRef.Resolve —
    // i.e. per cell x per color token x per render on the data-grid workload. The
    // resolved brush for a given (key, theme) is deterministic and stable until the
    // theme/palette changes, so cache it and clear the cache on a theme change
    // (InvalidateResolutionCache, wired from the hosts' theme-change handlers).
    // The cached value mirrors the uncached return EXACTLY, including a null result
    // for an unknown key (so missing keys aren't re-walked). Brushes resolved from a
    // ThemeDictionary are shared instances today, so returning the cached reference
    // is identical to the prior behavior.
    // Each entry is stamped with the invalidation generation it was resolved under. A resolve
    // that started before InvalidateResolutionCache and publishes after it carries an older
    // generation, so it is never served: Clear() alone is not a barrier for in-flight resolves.
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<(string Key, string Theme), (int Generation, Brush? Brush)> s_resolutionCache = new();
    private static int s_resolutionGeneration;

    /// <summary>
    /// Drops every cached <see cref="ResolveForTheme"/> result. Called by the host
    /// when the effective theme or the system palette changes, and by
    /// <see cref="Theme.NotifyResourcesChanged"/>, so the next resolve re-reads the
    /// (now-updated) dictionaries. Thread-safe.
    /// </summary>
    internal static void InvalidateResolutionCache()
    {
        Interlocked.Increment(ref s_resolutionGeneration);
        s_resolutionCache.Clear();
    }

    private static bool TryGetCurrentResolution((string Key, string Theme) cacheKey, int generation, out Brush? brush)
    {
        // Also re-check the live generation: an invalidation that has incremented it but not yet
        // cleared the dictionary must not serve the entry it is about to drop.
        if (s_resolutionCache.TryGetValue(cacheKey, out var entry) && entry.Generation == generation
            && Volatile.Read(ref s_resolutionGeneration) == generation)
        {
            brush = entry.Brush;
            return true;
        }
        brush = null;
        return false;
    }

    /// <summary>Test seam: number of cached resolutions.</summary>
    internal static int ResolutionCacheCountForTest => s_resolutionCache.Count;

    /// <summary>Test seam: the current invalidation generation.</summary>
    internal static int ResolutionGenerationForTest => Volatile.Read(ref s_resolutionGeneration);

    /// <summary>
    /// Test seam: plants a cache entry, stamped with <paramref name="generation"/> (default:
    /// the current one). Headless tests cannot construct a <see cref="Brush"/>, so this is the
    /// only way to prove an invalidation empties the cache and that a late publish is ignored.
    /// </summary>
    internal static void SeedResolutionCacheForTest(string resourceKey, string themeName, int? generation = null)
        => s_resolutionCache[(resourceKey, themeName)] = (generation ?? ResolutionGenerationForTest, null);

    /// <summary>Test seam: whether a lookup would be served from the cache.</summary>
    internal static bool IsResolutionCachedForTest(string resourceKey, string themeName)
        => TryGetCurrentResolution((resourceKey, themeName), ResolutionGenerationForTest, out _);

    /// <summary>Test seam: whether a lookup that captured <paramref name="capturedGeneration"/> would be served.</summary>
    internal static bool IsResolutionCachedForTest(string resourceKey, string themeName, int capturedGeneration)
        => TryGetCurrentResolution((resourceKey, themeName), capturedGeneration, out _);






































































































































}

/// <summary>
/// Provides semantic theme tokens and custom resource references.
/// All tokens resolve from WinUI's resource system and automatically
/// adapt when the theme changes (Light ↔ Dark).
/// <para>
/// Usage: <c>Button("Go").Background(Theme.Accent)</c>
/// </para>
/// </summary>
public static class Theme
{
    // ── Accent / Fill ────────────────────────────────────────────────
    public static ThemeRef Accent            => new("AccentFillColorDefaultBrush");
    public static ThemeRef AccentSecondary   => new("AccentFillColorSecondaryBrush");
    public static ThemeRef AccentTertiary    => new("AccentFillColorTertiaryBrush");
    public static ThemeRef AccentDisabled    => new("AccentFillColorDisabledBrush");

    // ── Text ─────────────────────────────────────────────────────────
    public static ThemeRef PrimaryText       => new("TextFillColorPrimaryBrush");
    public static ThemeRef SecondaryText     => new("TextFillColorSecondaryBrush");
    public static ThemeRef TertiaryText      => new("TextFillColorTertiaryBrush");
    public static ThemeRef DisabledText      => new("TextFillColorDisabledBrush");
    public static ThemeRef AccentText        => new("AccentTextFillColorPrimaryBrush");

    // ── Surfaces / Fill ──────────────────────────────────────────────
    public static ThemeRef SolidBackground   => new("SolidBackgroundFillColorBaseBrush");
    public static ThemeRef CardBackground    => new("CardBackgroundFillColorDefaultBrush");
    public static ThemeRef SmokeFill         => new("SmokeFillColorDefaultBrush");
    public static ThemeRef SubtleFill        => new("SubtleFillColorSecondaryBrush");
    public static ThemeRef LayerFill         => new("LayerFillColorDefaultBrush");

    // ── Control Fill ─────────────────────────────────────────────────
    public static ThemeRef ControlFill              => new("ControlFillColorDefaultBrush");
    public static ThemeRef ControlFillSecondary     => new("ControlFillColorSecondaryBrush");
    public static ThemeRef ControlFillTertiary      => new("ControlFillColorTertiaryBrush");
    public static ThemeRef ControlFillDisabled      => new("ControlFillColorDisabledBrush");
    public static ThemeRef ControlFillInputActive   => new("ControlFillColorInputActiveBrush");

    // ── Stroke / Border ──────────────────────────────────────────────
    public static ThemeRef CardStroke        => new("CardStrokeColorDefaultBrush");
    public static ThemeRef SurfaceStroke     => new("SurfaceStrokeColorDefaultBrush");
    public static ThemeRef DividerStroke     => new("DividerStrokeColorDefaultBrush");
    public static ThemeRef ControlStroke     => new("ControlStrokeColorDefaultBrush");
    public static ThemeRef ControlStrokeSecondary => new("ControlStrokeColorSecondaryBrush");

    // ── Signal ───────────────────────────────────────────────────────
    public static ThemeRef SystemAttention   => new("SystemFillColorAttentionBrush");
    public static ThemeRef SystemSuccess     => new("SystemFillColorSuccessBrush");
    public static ThemeRef SystemCaution     => new("SystemFillColorCautionBrush");
    public static ThemeRef SystemCritical    => new("SystemFillColorCriticalBrush");
    public static ThemeRef SystemNeutral     => new("SystemFillColorNeutralBrush");
    public static ThemeRef SystemSolidNeutral => new("SystemFillColorSolidNeutralBrush");

    public static ThemeRef SystemAttentionBackground => new("SystemFillColorAttentionBackgroundBrush");
    public static ThemeRef SystemSuccessBackground   => new("SystemFillColorSuccessBackgroundBrush");
    public static ThemeRef SystemCautionBackground   => new("SystemFillColorCautionBackgroundBrush");
    public static ThemeRef SystemCriticalBackground  => new("SystemFillColorCriticalBackgroundBrush");
    public static ThemeRef SystemNeutralBackground   => new("SystemFillColorNeutralBackgroundBrush");
    public static ThemeRef SystemSolidAttention       => new("SystemFillColorSolidAttentionBackgroundBrush");

    // ── Custom resource reference ────────────────────────────────────
    /// <summary>
    /// Reference any WinUI theme resource by key name.
    /// The resource must exist in the WinUI resource tree
    /// (e.g., defined in XamlControlsResources or added via app resources).
    /// </summary>
    public static ThemeRef Ref(string resourceKey) => new(resourceKey);

    /// <summary>
    /// Tells Reactor that application resources were edited at runtime, so theme
    /// references resolve again and every live host re-renders with the new values.
    /// </summary>
    /// <remarks>
    /// <para>Call it after changing <c>Application.Current.Resources</c> in a way that is
    /// <em>not</em> a theme change — replacing a brush in a <c>ThemeDictionaries</c> entry,
    /// merging or removing a brand dictionary, or an inspector's live resource edit. Reactor
    /// caches each resolved <c>(resource key, theme)</c> brush and only drops that cache
    /// when the effective theme or the system palette changes, so without this call such an
    /// edit is not seen. Not needed for a Light/Dark/high-contrast switch
    /// (<c>RequestedTheme</c>) or for mutating an existing brush's <c>Color</c>; both are
    /// already picked up.</para>
    /// <para>What it does: clears the resolution cache behind <see cref="ThemeRef"/>, then
    /// asks every live <see cref="Isotope.UI.Hosting.ReactorHost"/> and
    /// <see cref="Isotope.UI.Hosting.ReactorHostControl"/> for a full re-render that
    /// bypasses component memoization and the reconciler's skip of unchanged elements, so
    /// memoized and reused subtrees are refreshed too. That re-resolves
    /// <c>.Resources(...)</c> theme overrides, re-applies <see cref="ThemeRef"/> modifiers
    /// such as <c>.Background(Theme.Accent)</c>, and re-runs any
    /// <see cref="ThemeRef.Resolve(string, bool)"/> call in a <c>Render</c> method.</para>
    /// <para>Callable from any thread; the re-renders are scheduled on each host's UI
    /// thread. Await a host's <c>WaitForIdleAsync()</c> to observe the result.</para>
    /// <para>On a host's UI thread, a host that has no content yet re-renders inline, and its
    /// render can throw. Every other host is still notified; then the exception is rethrown
    /// (an <see cref="global::System.AggregateException"/> when several hosts threw).</para>
    /// </remarks>
    public static void NotifyResourcesChanged()
    {
        ThemeRef.InvalidateResolutionCache();
        ThemeResourceListeners.NotifyAll();
    }
}
