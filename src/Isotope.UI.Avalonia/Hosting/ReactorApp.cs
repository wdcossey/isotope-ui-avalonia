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
    public static Window OpenWindow(string title, Func<RenderContext, Element> rootRender, double? width = null, double? height = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rootRender);
        var host = new ReactorHostControl(rootRender);
        var window = new Window { Title = title, Content = host };
        if (width is { } w) window.Width = w;
        if (height is { } h) window.Height = h;
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
        double? width = null, double? height = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        args ??= [];
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnMainWindowClose };
        builder.SetupWithLifetime(lifetime);
        // As upstream's ReactorApp: the UI thread's queue is captured at startup.
        UIDispatcher = DispatcherQueue.ForUIThread;
        lifetime.MainWindow = OpenWindow(title, rootRender, width, height);
        return lifetime.Start(args);
    }
}
