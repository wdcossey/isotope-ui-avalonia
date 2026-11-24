// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/ColorScheme.cs, last changed upstream in ffe1de4d884d; transforms: drop-ColorScheme-ColorSchemeContext, winui-usings, namespace-isotope. Do not edit: change the tools/sync rules.


namespace Isotope.UI.Core;

/// <summary>
/// Represents a color scheme variant a component can adapt its rendering to.
/// <para>
/// Note that <see cref="RenderContext.UseColorScheme"/> reports the app-global theme and
/// only ever returns <see cref="Light"/> or <see cref="Dark"/>; <see cref="HighContrast"/>
/// is produced solely by the <see cref="ElementTheme.Default"/> mapping arm below. Use
/// <see cref="RenderContext.UseHighContrast"/> to detect forced-colors mode.
/// </para>
/// </summary>
public enum ColorScheme
{
    /// <summary>The standard light theme.</summary>
    Light,

    /// <summary>The standard dark theme.</summary>
    Dark,

    /// <summary>Windows High Contrast mode is active.</summary>
    HighContrast,
}




















































