using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The platform hooks' examples, for docs/guide/platform-hooks.md: each region is a snippet of the page (DocSnippetTests
/// checks they match) and PlatformHooksTests runs it in a headless window.
/// </summary>
internal static class PlatformHookExamples
{
    #region platform-adaptive
    public static Element Adaptive(RenderContext ctx)
    {
        var wide = ctx.UseBreakpoint(640);   // re-renders when the window's width crosses 640
        var dark = ctx.UseIsDarkTheme();     // the window's (and the app's) theme variant
        var (width, height) = ctx.UseWindowSize();
        var summary = TextBlock($"{width:0} x {height:0}, {(dark ? "dark" : "light")}");
        return wide
            ? HStack(8, summary, TextBlock("Details beside"))
            : VStack(8, summary, TextBlock("Details below"));
    }
    #endregion

    #region platform-closing-guard
    public static Element Editor(RenderContext ctx)
    {
        var (draft, setDraft) = ctx.UseState("");
        var (saved, setSaved) = ctx.UseState("");
        // The window doesn't close while there are unsaved changes.
        ctx.UseClosingGuard(() => draft == saved);
        return VStack(
            TextBox(draft, setDraft),
            Button(draft == saved ? "Saved" : "Save", () => setSaved(draft)));
    }
    #endregion
}
