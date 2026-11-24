using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Styling;
using Isotope.UI.Core;
using Isotope.UI.Core.Diagnostics;

namespace Isotope.UI.Hosting;

// The plumbing of the platform hooks (RenderContext.PlatformHooks.cs): what a hook sees of its host's platform, the
// subscriptions that follow the host's top level, and the reads each hook makes. Tier 3, written for Avalonia.

/// <summary>
/// What a component sees of its platform: the host control that renders it (null for content rendered by a bare
/// reconciler), the host's top level (null while the host isn't in one; for a host in a popup, the top level the
/// popup belongs to) and, through them, the window and the app.
/// </summary>
internal readonly record struct PlatformView(ReactorHostControl? Host, TopLevel? TopLevel)
{
    /// <summary>The view of <paramref name="host"/> right now.</summary>
    public static PlatformView Of(ReactorHostControl? host) => new(host, OwningTopLevel(host));

    /// <summary>
    /// The top level <paramref name="host"/> is in; for a popup (flyout, tooltip, menu), which is a top level of its
    /// own, the top level that opened it.
    /// </summary>
    public static TopLevel? OwningTopLevel(Visual? host)
    {
        var topLevel = host is null ? null : TopLevel.GetTopLevel(host);
        while (topLevel is PopupRoot popup && popup.ParentTopLevel is { } parent && !ReferenceEquals(parent, popup))
            topLevel = parent;
        return topLevel;
    }

    /// <summary>Whether app-wide sources may be subscribed: with no host, or a host in a top level.</summary>
    /// <remarks>
    /// A detached host subscribes to nothing app-wide (it subscribes again when it joins a top level), so a host
    /// dropped without being disposed isn't kept alive by the application's events.
    /// </remarks>
    public bool MayWatchApp => Host is null || TopLevel is not null;

    /// <summary>The top level, when it is a window.</summary>
    public Window? Window => TopLevel as Window;

    /// <summary>The application, when there is one and this thread may read it.</summary>
    public static Application? App => Application.Current is { } app && app.CheckAccess() ? app : null;
}

/// <summary>A test's stand-in for the platform's colour values (headless can't change them).</summary>
internal interface IColorValuesSource
{
    PlatformColorValues GetColorValues();
    event EventHandler<PlatformColorValues>? ColorValuesChanged;
}

/// <summary>A test's stand-in for the platform's displays (headless has one, which never changes).</summary>
internal interface IDisplaySource
{
    IReadOnlyList<DisplayInfo> Displays { get; }
    event EventHandler? Changed;
}

/// <summary>Live platform subscriptions on this thread (the UI thread), so tests can see each is removed on unmount.</summary>
internal static class PlatformHookDiagnostics
{
    [ThreadStatic] private static int t_liveSubscriptions;

    public static int LiveSubscriptions => t_liveSubscriptions;

    internal static void Added() => t_liveSubscriptions++;

    internal static void Removed() => t_liveSubscriptions--;
}

/// <summary>Event subscriptions as disposables, counted by <see cref="PlatformHookDiagnostics"/>.</summary>
internal static class Subscriptions
{
    public static IDisposable Event(Action add, Action remove)
    {
        add();
        PlatformHookDiagnostics.Added();
        return new Unsubscriber(remove);
    }

    /// <summary>Calls <paramref name="changed"/> when <paramref name="property"/> of <paramref name="target"/> changes.</summary>
    public static IDisposable Property(AvaloniaObject target, AvaloniaProperty property, Action changed)
    {
        void Handler(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == property) changed();
        }
        return Event(() => target.PropertyChanged += Handler, () => target.PropertyChanged -= Handler);
    }

    /// <summary>One disposable for several (null parts are skipped); null when there are none.</summary>
    public static IDisposable? All(params IDisposable?[] parts)
    {
        var live = parts.Where(p => p is not null).ToArray();
        return live.Length == 0 ? null : new Unsubscriber(() => { foreach (var p in live) p!.Dispose(); }, counted: false);
    }

    private sealed class Unsubscriber(Action remove, bool counted = true) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose()
        {
            if (_remove is not { } remove) return;
            _remove = null;
            remove();
            if (counted) PlatformHookDiagnostics.Removed();
        }
    }
}

/// <summary>
/// A hook's subscription, made by its effect: <c>attach</c> subscribes to the platform as seen from the host, again
/// whenever the host joins or leaves a top level (or its top level closes, which counts as leaving), and each
/// platform signal (and each such move, and once after subscribing) calls <c>changed</c> with the current view.
/// Disposed by the effect's cleanup, on unmount.
/// </summary>
internal sealed class HostLink : IDisposable
{
    private readonly ReactorHostControl? _host;
    private readonly Func<PlatformView, Action, IDisposable?> _attach;
    private readonly Action<PlatformView>? _changed;
    private readonly Action _signal;
    private IDisposable? _inner;
    private IDisposable? _closed;
    private PlatformView _view;
    private bool _disposed;

    public HostLink(ReactorHostControl? host, Func<PlatformView, Action, IDisposable?> attach, Action<PlatformView>? changed)
    {
        _host = host;
        _attach = attach;
        _changed = changed;
        _signal = Signal;
        if (host is not null)
        {
            host.AttachedToVisualTree += OnAttached;
            host.DetachedFromVisualTree += OnDetached;
            PlatformHookDiagnostics.Added();
        }
        Attach(PlatformView.Of(host));
        // The platform can change between the render's read and this subscription.
        Signal();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Move(PlatformView.Of(_host));

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Move(new PlatformView(_host, null));

    private void Move(PlatformView view)
    {
        if (_disposed) return;
        Attach(view);
        Signal();
    }

    // A closed top level is left for good: drop its subscriptions without asking for a render (the window is gone).
    // (Avalonia detaches a closing window's content first, which already moved the link; this covers a top level
    // that closes without detaching its content.)
    private void OnTopLevelClosed(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _inner?.Dispose();
        _inner = null;
        _closed?.Dispose();
        _closed = null;
        _view = new PlatformView(_host, null);
    }

    private void Attach(PlatformView view)
    {
        _inner?.Dispose();
        _inner = null;
        _closed?.Dispose();
        _closed = null;
        _view = view;
        if (_host is not null && view.TopLevel is { } topLevel)
            _closed = Subscriptions.Event(() => topLevel.Closed += OnTopLevelClosed, () => topLevel.Closed -= OnTopLevelClosed);
        _inner = _attach(view, _signal);
    }

    private void Signal()
    {
        if (!_disposed) _changed?.Invoke(_view);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_host is not null)
        {
            _host.AttachedToVisualTree -= OnAttached;
            _host.DetachedFromVisualTree -= OnDetached;
            PlatformHookDiagnostics.Removed();
        }
        _closed?.Dispose();
        _closed = null;
        _inner?.Dispose();
        _inner = null;
    }
}

/// <summary>
/// A value hook's state: the value the last render read and returned, and the read, subscription and render request
/// of the last render. A platform signal reads again and asks for a render only when the value changed.
/// </summary>
internal sealed class PlatformCell<T>(IEqualityComparer<T> comparer)
{
    public T Rendered = default!;
    public Func<PlatformView, T> Read = null!;
    public Func<PlatformView, Action, IDisposable?> Subscribe = null!;
    public Action? Rerender;

    public IDisposable? Attach(PlatformView view, Action signal) => Subscribe(view, signal);

    public void Check(PlatformView view)
    {
        var now = Read(view);
        if (comparer.Equals(now, Rendered)) return;
        Rendered = now;
        Rerender?.Invoke();
    }
}

/// <summary>A closing guard's state: the predicate of the last render, asked when the window starts to close.</summary>
internal sealed class ClosingGuardCell
{
    public Func<bool> CanClose = null!;

    public IDisposable Attach(Window window) =>
        Subscriptions.Event(() => window.Closing += OnClosing, () => window.Closing -= OnClosing);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // As upstream: once a guard has cancelled, later ones aren't asked.
        if (e.Cancel) return;
        try
        {
            if (!CanClose()) e.Cancel = true;
        }
        catch (Exception ex)
        {
            // As upstream: a guard that throws cancels the close rather than crashing it.
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "UseClosingGuard", ex);
            e.Cancel = true;
        }
    }
}

/// <summary>What each platform hook reads, and what it subscribes to.</summary>
internal static class PlatformReads
{
    // ── Theme ──

    public static ColorScheme ColorScheme(PlatformView v)
    {
        // The host's theme (it inherits its window's and the app's); the app's while the host isn't in a top level.
        var variant = v is { Host: { } host, TopLevel: not null } ? host.ActualThemeVariant : PlatformView.App?.ActualThemeVariant;
        for (var x = variant; x is not null; x = x.InheritVariant)
        {
            if (x == ThemeVariant.Dark) return Core.ColorScheme.Dark;
            if (x == ThemeVariant.Light) return Core.ColorScheme.Light;
        }
        return Core.ColorScheme.Light;
    }

    public static IDisposable? SubscribeTheme(PlatformView v, Action changed)
    {
        void Handler(object? sender, EventArgs e) => changed();
        // A host in a top level inherits the window's and the app's theme, so its own event covers both; a detached
        // host watches nothing (it re-reads when it joins a top level); with no host, the app's.
        if (v.Host is { } host)
            return v.TopLevel is null ? null : Subscriptions.Event(() => host.ActualThemeVariantChanged += Handler, () => host.ActualThemeVariantChanged -= Handler);
        return PlatformView.App is { } app
            ? Subscriptions.Event(() => app.ActualThemeVariantChanged += Handler, () => app.ActualThemeVariantChanged -= Handler)
            : null;
    }

    public static bool HighContrast(PlatformView v) =>
        ColorValues(v)?.ContrastPreference == ColorContrastPreference.High;

    private static PlatformColorValues? ColorValues(PlatformView v) =>
        v.Host?.ColorValuesOverride is { } fake ? fake.GetColorValues() : PlatformView.App?.PlatformSettings?.GetColorValues();

    public static IDisposable? SubscribeColorValues(PlatformView v, Action changed)
    {
        void Handler(object? sender, PlatformColorValues e) => changed();
        // App-wide (the fake stands in for the app's settings): not watched by a detached host.
        if (!v.MayWatchApp) return null;
        if (v.Host?.ColorValuesOverride is { } fake)
            return Subscriptions.Event(() => fake.ColorValuesChanged += Handler, () => fake.ColorValuesChanged -= Handler);
        if (PlatformView.App?.PlatformSettings is { } settings)
            return Subscriptions.Event(() => settings.ColorValuesChanged += Handler, () => settings.ColorValuesChanged -= Handler);
        return null;
    }

    // ── Window ──

    public static (double Width, double Height) Size(TopLevel? topLevel) =>
        topLevel is null ? (0, 0) : (topLevel.ClientSize.Width, topLevel.ClientSize.Height);

    public static IDisposable? SubscribeSize(TopLevel? topLevel, Action changed) =>
        topLevel is null ? null : Subscriptions.Property(topLevel, TopLevel.ClientSizeProperty, changed);

    public static uint Dpi(PlatformView v) =>
        v.TopLevel is { } t ? (uint)Math.Round(96 * t.RenderScaling) : 96u;

    public static IDisposable? SubscribeScaling(TopLevel? topLevel, Action changed)
    {
        if (topLevel is null) return null;
        void Handler(object? sender, EventArgs e) => changed();
        return Subscriptions.Event(() => topLevel.ScalingChanged += Handler, () => topLevel.ScalingChanged -= Handler);
    }

    public static WindowState WindowState(PlatformView v) => v.Window?.WindowState ?? Avalonia.Controls.WindowState.Normal;

    public static IDisposable? SubscribeWindowState(PlatformView v, Action changed) =>
        v.Window is { } w ? Subscriptions.Property(w, Window.WindowStateProperty, changed) : null;

    public static bool IsActive(PlatformView v) => v.Window?.IsActive ?? true;

    public static IDisposable? SubscribeActivation(PlatformView v, Action changed)
    {
        if (v.Window is not { } w) return null;
        void Handler(object? sender, EventArgs e) => changed();
        return Subscriptions.All(
            Subscriptions.Event(() => w.Activated += Handler, () => w.Activated -= Handler),
            Subscriptions.Event(() => w.Deactivated += Handler, () => w.Deactivated -= Handler));
    }

    public static (double X, double Y) Position(PlatformView v)
    {
        if (v.Window is not { } w) return (0, 0);
        var scale = w.DesktopScaling;
        return (w.Position.X / scale, w.Position.Y / scale);
    }

    public static IDisposable? SubscribePosition(PlatformView v, Action changed)
    {
        if (v.Window is not { } w) return null;
        void Handler(object? sender, PixelPointEventArgs e) => changed();
        return Subscriptions.All(
            Subscriptions.Event(() => w.PositionChanged += Handler, () => w.PositionChanged -= Handler),
            SubscribeScaling(w, changed));
    }

    // ── Displays ──

    public static IReadOnlyList<DisplayInfo> Displays(PlatformView v)
    {
        if (v.Host?.DisplaysOverride is { } fake) return fake.Displays;
        if (v.TopLevel?.Screens is not { } screens) return [];
        return screens.All.Select((s, i) => new DisplayInfo
        {
            // The platform handle is unique per display; a display name isn't (two monitors of one model share it).
            Id = s.TryGetPlatformHandle()?.Handle.ToString()
                ?? (string.IsNullOrEmpty(s.DisplayName) ? i.ToString() : s.DisplayName + "#" + i),
            IsPrimary = s.IsPrimary,
            BoundsDip = s.Bounds.ToRect(s.Scaling),
            WorkAreaDip = s.WorkingArea.ToRect(s.Scaling),
            Dpi = (uint)Math.Round(96 * s.Scaling),
        }).ToArray();
    }

    public static IDisposable? SubscribeDisplays(PlatformView v, Action changed)
    {
        void Handler(object? sender, EventArgs e) => changed();
        if (v.Host?.DisplaysOverride is { } fake)
            return Subscriptions.Event(() => fake.Changed += Handler, () => fake.Changed -= Handler);
        if (v.TopLevel?.Screens is { } screens)
            return Subscriptions.Event(() => screens.Changed += Handler, () => screens.Changed -= Handler);
        return null;
    }

    /// <summary>Two display lists are the same when their snapshots are equal, display by display.</summary>
    public sealed class DisplayListComparer : IEqualityComparer<IReadOnlyList<DisplayInfo>>
    {
        public static readonly DisplayListComparer Instance = new();

        public bool Equals(IReadOnlyList<DisplayInfo>? x, IReadOnlyList<DisplayInfo>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

        public int GetHashCode(IReadOnlyList<DisplayInfo> obj) => obj.Count;
    }
}
