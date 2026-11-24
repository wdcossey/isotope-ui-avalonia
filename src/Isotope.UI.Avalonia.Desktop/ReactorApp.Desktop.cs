using Avalonia;
using Avalonia.Controls;
using Isotope.UI.Core;
using Isotope.UI.Hosting;

namespace Isotope.UI;

/// <summary>
/// Upstream's one-line startup on Avalonia's desktop platforms (Windows, macOS, Linux), as static members of
/// <see cref="ReactorApp"/>: <c>ReactorApp.Run&lt;App&gt;("Title", width: 800, height: 600)</c> ports from Reactor by
/// changing the namespace. Each runs the app with one window and a classic desktop lifetime, over
/// <see cref="CreateDefaultAppBuilder"/>, until the window closes, and returns the exit code (upstream's return
/// nothing, so their call statements compile as they are).
/// </summary>
/// <remarks>
/// As upstream's, a <c>Main</c> on an MTA thread (top-level statements, an async <c>Main</c>) is moved: on Windows the
/// app runs on a new STA thread, which the clipboard and drag-and-drop need, and the call returns when it ends. The
/// icon is a factory, called when the window opens, because Avalonia's <see cref="WindowIcon"/> can only be built once
/// the platform is set up: <c>icon: () =&gt; new WindowIcon("app.ico")</c>.
/// </remarks>
public static class ReactorAppDesktop
{
    /// <summary>The window title when none is given (upstream's is "Reactor App").</summary>
    public const string DefaultTitle = "Isotope App";

    extension(ReactorApp)
    {
        /// <summary>
        /// The builder the one-line <c>Run</c> overloads use: an <see cref="Application"/> with the Fluent theme, on the
        /// platform Avalonia detects (<c>UsePlatformDetect</c>). Pass it, configured further, to
        /// <c>ReactorApp.Run(builder, …)</c> to keep the defaults and add to them.
        /// </summary>
        public static AppBuilder CreateDefaultAppBuilder() => AppBuilder.Configure<DefaultApplication>().UsePlatformDetect();

        /// <summary>
        /// Runs an app whose one window holds a <typeparamref name="TRoot"/> component, as upstream's
        /// <c>ReactorApp.Run&lt;TRoot&gt;</c>. Returns the exit code.
        /// </summary>
        /// <param name="title">The window's title.</param>
        /// <param name="width">The window's initial width; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="height">The window's initial height; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="fullScreen">Opens the window full screen.</param>
        /// <param name="icon">Builds the window's icon when the window opens; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="configure">Called on the UI thread with the window's host before the root mounts.</param>
        public static int Run<TRoot>(string title = DefaultTitle, double? width = null, double? height = null,
            bool fullScreen = false, Func<WindowIcon>? icon = null, Action<ReactorHostControl>? configure = null)
            where TRoot : Component, new()
        {
            ArgumentNullException.ThrowIfNull(title);
            return ReactorApp.Run<TRoot>(ReactorApp.CreateDefaultAppBuilder(), title, CommandLineArgs(), width, height,
                fullScreen, icon, configure);
        }

        /// <summary>
        /// Runs an app whose one window holds the function component <paramref name="rootRender"/>, as upstream's
        /// <c>ReactorApp.Run(title, rootRender, …)</c>. Returns the exit code.
        /// </summary>
        /// <param name="title">The window's title.</param>
        /// <param name="rootRender">The root component.</param>
        /// <param name="width">The window's initial width; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="height">The window's initial height; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="fullScreen">Opens the window full screen.</param>
        /// <param name="icon">Builds the window's icon when the window opens; <c>null</c> leaves Avalonia's default.</param>
        /// <param name="configure">Called on the UI thread with the window's host before the root mounts.</param>
        public static int Run(string title, Func<RenderContext, Element> rootRender, double? width = null,
            double? height = null, bool fullScreen = false, Func<WindowIcon>? icon = null, Action<ReactorHostControl>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(rootRender);
            return ReactorApp.Run(ReactorApp.CreateDefaultAppBuilder(), title, rootRender, CommandLineArgs(), width, height,
                fullScreen, icon, configure);
        }
    }

    // The process's arguments without the program's path: what a Main(string[] args) receives.
    internal static string[] CommandLineArgs() => Environment.GetCommandLineArgs().Skip(1).ToArray();
}
