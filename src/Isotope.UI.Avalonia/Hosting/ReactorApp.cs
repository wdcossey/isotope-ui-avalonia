using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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
    /// <paramref name="rootRender"/>. Closing the window disposes the host (its effects are cleaned up). Call it on
    /// the UI thread once the app runs.
    /// </summary>
    /// <param name="title">The window's title.</param>
    /// <param name="rootRender">The root component.</param>
    /// <param name="width">The window's initial width; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="height">The window's initial height; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="fullScreen">Opens the window full screen (<see cref="WindowState.FullScreen"/>).</param>
    /// <param name="icon">The window's icon; <c>null</c> leaves Avalonia's default.</param>
    /// <param name="configure">Called with the host, already the window's content, before the root mounts (for example
    /// to set its <see cref="ReactorHostControl.RenderErrorHandler"/>), as upstream's <c>configure</c>. If it throws,
    /// the host is disposed and the window closed, and the exception propagates; if it closes the window, the host is
    /// disposed and an <see cref="InvalidOperationException"/> is thrown.</param>
    public static Window OpenWindow(string title, Func<RenderContext, Element> rootRender, double? width = null, double? height = null,
        bool fullScreen = false, WindowIcon? icon = null, Action<ReactorHostControl>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rootRender);
        var host = new ReactorHostControl();
        var window = new Window { Title = title, Content = host };
        if (width is { } w) window.Width = w;
        if (height is { } h) window.Height = h;
        if (fullScreen) window.WindowState = WindowState.FullScreen;
        if (icon is not null) window.Icon = icon;
        bool closed = false;
        window.Closed += (_, _) =>
        {
            closed = true;
            host.Dispose();
        };
        try
        {
            // As upstream's OpenWindowCore: configure gets the host of a created window, before the root mounts.
            configure?.Invoke(host);
            if (closed) throw new InvalidOperationException("configure closed the window before its root mounted.");
            host.Mount(rootRender);
        }
        catch
        {
            // Closing disposes the host (the Closed handler); a window configure closed is closed already.
            if (!closed) window.Close();
            throw;
        }
        window.Show();
        return window;
    }

    /// <summary>
    /// Runs an app with one window: sets up <paramref name="builder"/> (the app's <see cref="Application"/>, platform
    /// and theme) with a classic desktop lifetime, opens the window (<see cref="OpenWindow"/>) and runs until it
    /// closes. Returns the exit code.
    /// </summary>
    /// <remarks>
    /// <paramref name="icon"/> is a factory because Avalonia's <see cref="WindowIcon"/> can only be built once the
    /// platform is set up; it is called on the UI thread when the window opens. As upstream's <c>Run</c>, a caller on an
    /// MTA thread (top-level statements, an async <c>Main</c>) is moved: on Windows the app runs on a new STA thread,
    /// which the clipboard and drag-and-drop need, and the call returns when it ends.
    /// </remarks>
    /// <example><c>ReactorApp.Run(AppBuilder.Configure&lt;App&gt;().UsePlatformDetect(), "Counter", Counter, args)</c></example>
    public static int Run(AppBuilder builder, string title, Func<RenderContext, Element> rootRender, string[]? args = null,
        double? width = null, double? height = null, bool fullScreen = false, Func<WindowIcon>? icon = null,
        Action<ReactorHostControl>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rootRender);
        return Start(builder, args ?? [], () => OpenWindow(title, rootRender, width, height, fullScreen, icon?.Invoke(), configure));
    }

    /// <summary>
    /// Runs an app with one window around a <see cref="Component"/> class, as
    /// <see cref="Run(AppBuilder, string, Func{RenderContext, Element}, string[], double?, double?, bool, Func{WindowIcon}, Action{ReactorHostControl})"/>.
    /// Returns the exit code.
    /// </summary>
    /// <example><c>ReactorApp.Run&lt;CounterApp&gt;(AppBuilder.Configure&lt;App&gt;().UsePlatformDetect(), "Counter", args)</c></example>
    public static int Run<TRoot>(AppBuilder builder, string title, string[]? args = null, double? width = null,
        double? height = null, bool fullScreen = false, Func<WindowIcon>? icon = null, Action<ReactorHostControl>? configure = null)
        where TRoot : Component, new()
        => Run(builder, title, static _ => Factories.Component<TRoot>(), args, width, height, fullScreen, icon, configure);

    /// <summary>
    /// Tests only, as upstream's <c>StartApplicationForTest</c>: when set, <see cref="Start"/> hands it the builder, the
    /// arguments and the main window's factory instead of setting up the platform and running the lifetime (a test
    /// process has its own platform, and a real window is out of bounds).
    /// </summary>
    internal static Func<AppBuilder, string[], Func<Window>, int>? StartForTest { get; set; }

    // The shared tail of every Run, on an STA thread: set the platform up, capture the UI thread's queue, open the main
    // window, run.
    private static int Start(AppBuilder builder, string[] args, Func<Window> openMainWindow) => RunOnSta(() =>
    {
        if (StartForTest is { } start) return start(builder, args, openMainWindow);
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnMainWindowClose };
        builder.SetupWithLifetime(lifetime);
        // As upstream's ReactorApp: the UI thread's queue is captured at startup.
        UIDispatcher = DispatcherQueue.ForUIThread;
        lifetime.MainWindow = openMainWindow();
        return lifetime.Start(args);
    });

    // Upstream's RunOnSta: on Windows, a caller on an MTA thread gets the run on a new STA thread, joined, with its
    // result or exception. Elsewhere, on an STA thread, and on a thread Avalonia's dispatcher already belongs to (the
    // app touched Avalonia before Run: moving would put the platform on another thread than its objects), it runs inline.
    internal static int RunOnSta(Func<int> run)
    {
        if (!OperatingSystem.IsWindows() || Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
            || Dispatcher.FromThread(Thread.CurrentThread) is not null)
            return run();
        int result = 0;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { result = run(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
