namespace Isotope.UI.Core;

/// <summary>
/// A colour scheme a component can adapt its rendering to, as <see cref="RenderContext.UseColorScheme"/> returns it.
/// Upstream's enum, written here because upstream's file (src/Reactor/Core/ColorScheme.cs, vendored but not compiled)
/// also holds a WinUI-only mapping class. <see cref="RenderContext.UseColorScheme"/> only returns
/// <see cref="Light"/> or <see cref="Dark"/>; <see cref="RenderContext.UseHighContrast"/> reports high contrast.
/// </summary>
public enum ColorScheme
{
    /// <summary>The standard light theme.</summary>
    Light,

    /// <summary>The standard dark theme.</summary>
    Dark,

    /// <summary>High contrast (upstream's value; the hooks here report high contrast through UseHighContrast).</summary>
    HighContrast,
}
