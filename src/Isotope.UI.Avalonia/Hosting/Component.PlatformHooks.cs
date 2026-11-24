using Avalonia.Controls;
using Isotope.UI.Hosting;

namespace Isotope.UI.Core;

/// <summary>
/// The platform hooks for class components: upstream's <c>Component</c> forwarders to the theme, window and display
/// hooks, which tools/sync drops from the vendored <c>Component</c> with the hooks themselves (they are WinUI-typed).
/// Each forwards to the Avalonia version on <see cref="RenderContext"/> (RenderContext.PlatformHooks.cs), with
/// upstream's names and Avalonia's value types. Tier 3. See docs/guide/platform-hooks.md.
/// </summary>
public abstract partial class Component
{
    /// <summary><see cref="ColorScheme.Light"/> or <see cref="ColorScheme.Dark"/>, as <see cref="RenderContext.UseColorScheme"/>.</summary>
    protected ColorScheme UseColorScheme() => Context.UseColorScheme();

    /// <summary><c>true</c> when the colour scheme is dark, as <see cref="RenderContext.UseIsDarkTheme"/>.</summary>
    protected bool UseIsDarkTheme() => Context.UseIsDarkTheme();

    /// <summary><c>true</c> when the platform asks for high contrast, as <see cref="RenderContext.UseHighContrast"/>.</summary>
    protected bool UseHighContrast() => Context.UseHighContrast();

    /// <summary>The window the host is in, or <c>null</c>, as <see cref="RenderContext.UseWindow"/>.</summary>
    protected Window? UseWindow() => Context.UseWindow();

    /// <summary>The host's top level's client size in DIPs, as <see cref="RenderContext.UseWindowSize()"/>.</summary>
    protected (double Width, double Height) UseWindowSize() => Context.UseWindowSize();

    /// <summary>The client size of <paramref name="topLevel"/> in DIPs, as <see cref="RenderContext.UseWindowSize(TopLevel)"/>.</summary>
    protected (double Width, double Height) UseWindowSize(TopLevel topLevel) => Context.UseWindowSize(topLevel);

    /// <summary>Whether the host's top level is at least <paramref name="minWidth"/> wide, as <see cref="RenderContext.UseBreakpoint(double)"/>.</summary>
    protected bool UseBreakpoint(double minWidth) => Context.UseBreakpoint(minWidth);

    /// <summary>Whether <paramref name="topLevel"/> is at least <paramref name="minWidth"/> wide, as <see cref="RenderContext.UseBreakpoint(TopLevel, double)"/>.</summary>
    protected bool UseBreakpoint(TopLevel topLevel, double minWidth) => Context.UseBreakpoint(topLevel, minWidth);

    /// <summary>The window's state, as <see cref="RenderContext.UseWindowState"/>.</summary>
    protected WindowState UseWindowState() => Context.UseWindowState();

    /// <summary>Whether the window is the active one, as <see cref="RenderContext.UseIsActive"/>.</summary>
    protected bool UseIsActive() => Context.UseIsActive();

    /// <summary>The window's position in DIPs, as <see cref="RenderContext.UseWindowPosition"/>.</summary>
    protected (double X, double Y) UseWindowPosition() => Context.UseWindowPosition();

    /// <summary>The DPI the top level renders at, as <see cref="RenderContext.UseDpi"/>.</summary>
    protected uint UseDpi() => Context.UseDpi();

    /// <summary>The top level's displays, as <see cref="RenderContext.UseDisplays"/>.</summary>
    protected IReadOnlyList<DisplayInfo> UseDisplays() => Context.UseDisplays();

    /// <summary>A "can the window close now?" check while mounted, as <see cref="RenderContext.UseClosingGuard"/>.</summary>
    protected void UseClosingGuard(Func<bool> canClose) => Context.UseClosingGuard(canClose);
}
