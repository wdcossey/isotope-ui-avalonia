using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Isotope.UI.Core;
using Isotope.UI.Hosting;

namespace Isotope.UI;

/// <summary>
/// The app entry point and the application-wide engine state. Tier 3: written for Avalonia after upstream's
/// <c>ReactorApp</c>, with one window and no Windows chrome (title bar, backdrop).
/// </summary>
/// <remarks>
/// These overloads take the app's own <see cref="AppBuilder"/>. The package <c>Isotope.UI.Avalonia.Desktop</c> adds
/// upstream's one-line forms, <c>ReactorApp.Run&lt;TRoot&gt;(title, width, height)</c> and
/// <c>ReactorApp.Run(title, rootRender, width, height)</c>, over a default builder.
/// </remarks>
public static class ReactorApp
{
    private static RenderErrorHandler? s_defaultRenderErrorHandler;

    /// <summary>
    /// The UI thread's queue, captured at startup (<see cref="Run"/>) or by the first <see cref="ReactorHostControl"/>;
    /// <c>null</c> until then (headless and unit-test contexts), when an off-thread state setter throws rather than
    /// racing the render.
    /// </summary>
    internal static DispatcherQueue? UIDispatcher { get; set; }

    /// <summary>The render-error handler of every host that has none of its own (<see cref="ReactorHostControl.RenderErrorHandler"/>).</summary>
    public static RenderErrorHandler? DefaultRenderErrorHandler
    {
        get => Volatile.Read(ref s_defaultRenderErrorHandler);
        set => Volatile.Write(ref s_defaultRenderErrorHandler, value);
    }

    /// <summary>
    /// Opens a window titled <paramref name="title"/> whose content is a <see cref="ReactorHostControl"/> rendering
    /// <paramref name="rootRender"/>. Closing the window disposes the host (its effects are cleaned up).
    /// </summary>
    /// <param name="title">The window's title.</param>
    /// <param name="rootRender">The root component.</param>
    /// <param name="width">The window's initial width; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="height">The window's initial height; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="fullScreen">Opens the window full screen (<see cref="WindowState.FullScreen"/>).</param>
    /// <param name="icon">The window's icon; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="configure">Called with the host before the root mounts (for example to set its
    /// <see cref="ReactorHostControl.RenderErrorHandler"/>), as upstream's <c>configure</c>.</param>
    public static Window OpenWindow(string title, Func<RenderContext, Element> rootRender, double? width = null, double? height = null,
        bool fullScreen = false, WindowIcon? icon = null, Action<ReactorHostControl>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rootRender);
        var host = new ReactorHostControl();
        configure?.Invoke(host);
        host.Mount(rootRender);
        var window = new Window { Title = title, Content = host };
        if (width is { } w) window.Width = w;
        if (height is { } h) window.Height = h;
        if (fullScreen) window.WindowState = WindowState.FullScreen;
        if (icon is not null) window.Icon = icon;
        window.Closed += (_, _) => host.Dispose();
        window.Show();
        return window;
    }

    /// <summary>
    /// Runs an app with one window: sets up <paramref name="builder"/> (the app's <see cref="Application"/>, platform
    /// and theme) with a classic desktop lifetime, opens the window (<see cref="OpenWindow"/>) and runs until it
    /// closes. Returns the exit code.
    /// </summary>
    /// <example><c>ReactorApp.Run(AppBuilder.Configure&lt;App&gt;().UsePlatformDetect(), "Counter", Counter, args)</c></example>
    public static int Run(AppBuilder builder, string title, Func<RenderContext, Element> rootRender, string[]? args = null,
        double? width = null, double? height = null, bool fullScreen = false, WindowIcon? icon = null,
        Action<ReactorHostControl>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rootRender);
        return Start(builder, args ?? [], () => OpenWindow(title, rootRender, width, height, fullScreen, icon, configure));
    }

    /// <summary>
    /// Runs an app with one window around a <see cref="Component"/> class, as
    /// <see cref="Run(AppBuilder, string, Func{RenderContext, Element}, string[], double?, double?, bool, WindowIcon, Action{ReactorHostControl})"/>.
    /// Returns the exit code.
    /// </summary>
    /// <example><c>ReactorApp.Run&lt;CounterApp&gt;(AppBuilder.Configure&lt;App&gt;().UsePlatformDetect(), "Counter", args)</c></example>
    public static int Run<TRoot>(AppBuilder builder, string title, string[]? args = null, double? width = null,
        double? height = null, bool fullScreen = false, WindowIcon? icon = null, Action<ReactorHostControl>? configure = null)
        where TRoot : Component, new()
        => Run(builder, title, static _ => Factories.Component<TRoot>(), args, width, height, fullScreen, icon, configure);

    /// <summary>
    /// Tests only, as upstream's <c>StartApplicationForTest</c>: when set, <see cref="Start"/> hands it the builder, the
    /// arguments and the main window's factory instead of setting up the platform and running the lifetime (a test
    /// process has its own platform, and a real window is out of bounds).
    /// </summary>
    internal static Func<AppBuilder, string[], Func<Window>, int>? StartForTest { get; set; }

    // The shared tail of every Run: set the platform up, capture the UI thread's queue, open the main window, run.
    private static int Start(AppBuilder builder, string[] args, Func<Window> openMainWindow)
    {
        if (StartForTest is { } start) return start(builder, args, openMainWindow);
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnMainWindowClose };
        builder.SetupWithLifetime(lifetime);
        // As upstream's ReactorApp: the UI thread's queue is captured at startup.
        UIDispatcher = DispatcherQueue.ForUIThread;
        lifetime.MainWindow = openMainWindow();
        return lifetime.Start(args);
    }
}
