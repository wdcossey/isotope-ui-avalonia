using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Icons (#91), for docs/guide/writing-components.md (DocSnippetTests); IconTests runs it.</summary>
internal static class IconExamples
{
    #region icons
    const string Tick = "M 2,13 L 5,10 L 9,14 L 19,4 L 22,7 L 9,20 Z";

    public static Element SaveButton(RenderContext ctx)
    {
        var (saved, setSaved) = ctx.UseState(false);
        return HStack(8,
            Button(Icon(PathIcon(Tick)).Foreground(saved ? "SeaGreen" : "Gray"), () => setSaved(true)),
            // A glyph while unsaved, a shape once saved: another kind of data mounts another control.
            Icon(saved ? PathIcon(Tick) : FontIcon("!", fontSize: 16)).Width(24).Height(24),
            Icon(ImageIcon(new Uri("avares://MyApp/Assets/logo.png"))).Width(24).Height(24));
    }
    #endregion

    #region icons-segoe
    // Windows only: WinUI's Save symbol, from the Segoe Fluent Icons font that Windows installs.
    public static Element SaveSymbol() => Icon(FontIcon("\uE74E", "Segoe Fluent Icons"));
    #endregion
}
