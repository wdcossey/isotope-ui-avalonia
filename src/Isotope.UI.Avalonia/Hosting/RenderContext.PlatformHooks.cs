using Avalonia.Controls;
using Isotope.UI.Hosting;

namespace Isotope.UI.Core;

/// <summary>
/// The platform hooks: upstream's theme, window and display hooks, which tools/sync drops from the vendored
/// <c>RenderContext</c> (they are WinUI-typed), written for Avalonia with upstream's names and Avalonia's value types.
/// Tier 3. See docs/guide/platform-hooks.md for the list and for what differs from upstream.
/// </summary>
/// <remarks>
/// Each value hook reads its value during the render, so the first frame is right, and subscribes in an effect: a
/// platform change reads it again and re-renders the component only when it changed. The subscription follows the
/// host's top level (a host moved to another window re-subscribes) and is removed on unmount. A hook takes the same
/// hook slots with or without a window, so a component can move between the two.
/// </remarks>
public sealed partial class RenderContext
{
    /// <summary>The host control rendering this context, as recorded at the last render; null for a bare reconciler.</summary>
    private ReactorHostControl? PlatformHost => _owner as ReactorHostControl;

    // ── Theme and accessibility ──

    /// <summary>
    /// Returns <see cref="ColorScheme.Light"/> or <see cref="ColorScheme.Dark"/>: the theme variant of the host that
    /// renders this component (which inherits its window's and the app's <c>RequestedThemeVariant</c>), or the app's
    /// outside a host. Re-renders when it changes. Never <see cref="ColorScheme.HighContrast"/>; use
    /// <see cref="UseHighContrast"/> for that.
    /// </summary>
    public ColorScheme UseColorScheme() =>
        UsePlatformValue(PlatformReads.ColorScheme, PlatformReads.SubscribeTheme);

    /// <summary><c>true</c> when <see cref="UseColorScheme"/> is <see cref="ColorScheme.Dark"/>.</summary>
    public bool UseIsDarkTheme() => UseColorScheme() == ColorScheme.Dark;

    /// <summary>
    /// Returns <c>true</c> when the platform asks for high contrast (Avalonia's
    /// <c>PlatformColorValues.ContrastPreference</c>), and re-renders when that changes.
    /// </summary>
    public bool UseHighContrast() =>
        UsePlatformValue(PlatformReads.HighContrast, PlatformReads.SubscribeColorValues);

    // ── Window ──

    /// <summary>
    /// The window the host that renders this component is in, or <c>null</c> (outside a host, a host not in a
    /// window, or a top level that isn't a window), and re-renders when the host moves to another window or none.
    /// </summary>
    public Window? UseWindow() =>
        UsePlatformValue(static v => v.Window, static (_, _) => null);

    /// <summary>
    /// The client size, in DIPs, of the top level the host is in (a window, or the single view on mobile and in the
    /// browser), and re-renders when it changes. <c>(0, 0)</c> outside one.
    /// </summary>
    public (double Width, double Height) UseWindowSize() =>
        UsePlatformValue(static v => PlatformReads.Size(v.TopLevel), static (v, changed) => PlatformReads.SubscribeSize(v.TopLevel, changed));

    /// <summary>The client size, in DIPs, of <paramref name="topLevel"/>, and re-renders when it changes.</summary>
    public (double Width, double Height) UseWindowSize(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        return UsePlatformValue(_ => PlatformReads.Size(topLevel), (_, changed) => PlatformReads.SubscribeSize(topLevel, changed), key: topLevel);
    }

    /// <summary>
    /// <c>true</c> when the width of <see cref="UseWindowSize()"/> is at least <paramref name="minWidth"/>; re-renders
    /// only when the width crosses it. <c>false</c> outside a top level.
    /// </summary>
    public bool UseBreakpoint(double minWidth) =>
        UsePlatformValue(v => v.TopLevel is { } t && t.ClientSize.Width >= minWidth, static (v, changed) => PlatformReads.SubscribeSize(v.TopLevel, changed));

    /// <summary>
    /// <c>true</c> when the width of <paramref name="topLevel"/> is at least <paramref name="minWidth"/>; re-renders
    /// only when the width crosses it.
    /// </summary>
    public bool UseBreakpoint(TopLevel topLevel, double minWidth)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        return UsePlatformValue(_ => PlatformReads.Size(topLevel).Width >= minWidth, (_, changed) => PlatformReads.SubscribeSize(topLevel, changed), key: topLevel);
    }

    /// <summary>The window's state (Avalonia's <see cref="WindowState"/>), re-rendering on change; <c>Normal</c> outside a window.</summary>
    public WindowState UseWindowState() =>
        UsePlatformValue(PlatformReads.WindowState, PlatformReads.SubscribeWindowState);

    /// <summary>
    /// <c>true</c> while the window is the active one, re-rendering on activation and deactivation; <c>true</c> outside
    /// a window (as upstream's: content that is shown counts as active).
    /// </summary>
    public bool UseIsActive() =>
        UsePlatformValue(PlatformReads.IsActive, PlatformReads.SubscribeActivation);

    /// <summary>
    /// The window's position on the desktop, in DIPs (Avalonia's <c>Window.Position</c> over its desktop scaling),
    /// re-rendering when it moves; <c>(0, 0)</c> outside a window.
    /// </summary>
    public (double X, double Y) UseWindowPosition() =>
        UsePlatformValue(PlatformReads.Position, PlatformReads.SubscribePosition);

    /// <summary>
    /// The DPI the top level renders at (96 x its <c>RenderScaling</c>), re-rendering when it changes; 96 outside a top
    /// level.
    /// </summary>
    public uint UseDpi() =>
        UsePlatformValue(PlatformReads.Dpi, static (v, changed) => PlatformReads.SubscribeScaling(v.TopLevel, changed));

    /// <summary>
    /// Registers a synchronous "can the window close now?" check, asked from the window's <c>Closing</c> event for as
    /// long as the component is mounted. Guards stack: any that returns <c>false</c> (or throws) cancels the close,
    /// and later guards aren't asked. The latest render's <paramref name="canClose"/> is used. No-op outside a window.
    /// </summary>
    public void UseClosingGuard(Func<bool> canClose)
    {
        ArgumentNullException.ThrowIfNull(canClose);
        var host = PlatformHost;
        var cell = UseRef<ClosingGuardCell?>(null);
        var guard = cell.Current ??= new ClosingGuardCell();
        guard.CanClose = canClose;
        UseEffect(() =>
        {
            var link = new HostLink(host, (v, _) => v.Window is { } w ? guard.Attach(w) : null, changed: null);
            return link.Dispose;
        }, guard);
    }

    // ── Displays ──

    /// <summary>
    /// The displays of the top level the host is in (Avalonia's <c>TopLevel.Screens</c>), as value snapshots, and
    /// re-renders when they change. Empty outside a top level.
    /// </summary>
    public IReadOnlyList<DisplayInfo> UseDisplays() =>
        UsePlatformValue(PlatformReads.Displays, PlatformReads.SubscribeDisplays, comparer: PlatformReads.DisplayListComparer.Instance);

    // ── The value hook ──

    // Reads the value now, keeps it with this render's read and render request, and (from an effect, once per mount
    // or per key) subscribes through a HostLink that reads again on each platform signal. Two slots: a ref and an effect.
    private T UsePlatformValue<T>(Func<PlatformView, T> read, Func<PlatformView, Action, IDisposable?> subscribe,
        object? key = null, IEqualityComparer<T>? comparer = null)
    {
        var host = PlatformHost;
        var value = read(PlatformView.Of(host));
        var cell = UseRef<PlatformCell<T>?>(null);
        var state = cell.Current ??= new PlatformCell<T>(comparer ?? EqualityComparer<T>.Default);
        state.Rendered = value;
        state.Read = read;
        state.Subscribe = subscribe;
        state.Rerender = _requestRerender;
        UseEffect(() =>
        {
            var link = new HostLink(host, state.Attach, state.Check);
            return link.Dispose;
        }, state, key!);
        return value;
    }
}
