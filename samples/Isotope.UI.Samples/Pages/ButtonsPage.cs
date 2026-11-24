using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// The button family (#26): element content, toggle, three-state, repeat and hyperlink buttons, each with a tooltip.
/// </summary>
public static class ButtonsPage
{
    public static Element Render(RenderContext ctx)
    {
        var tips = ctx.UseContext(SampleApp.ShowTips);
        var (saves, setSaves) = ctx.UseState(0);
        var (bold, setBold) = ctx.UseState(false);
        var (filter, setFilter) = ctx.UseState<bool?>(false);
        // A repeat button clicks many times between renders: the updater form sees each click's result.
        var (volume, updateVolume) = ctx.UseReducer(50);
        var (links, setLinks) = ctx.UseState(0);

        // Any element as a button's content: here a coloured badge beside a label.
        var saveContent = HStack(6,
            Border(TextBlock("S")) with { Background = Brushes.SteelBlue, CornerRadius = 3, Padding = new Thickness(5, 0) },
            TextBlock("Save"));

        return VStack(12,
            TextBlock("Hover a button for its tooltip (View > Show tooltips turns them off)."),
            SampleApp.Row(
                Button(saveContent, () => setSaves(saves + 1)).Tip(tips, "A button whose content is a badge and a label"),
                TextBlock(saves == 1 ? "Saved once" : $"Saved {saves} times")),
            Separator(),
            SampleApp.Row(
                ToggleButton("Bold", bold, setBold).Tip(tips, "Stays pressed until clicked again"),
                TextBlock("Bold: " + (bold ? "on" : "off"))),
            SampleApp.Row(
                ThreeStateToggleButton("Filter", filter, setFilter).Tip(tips, "Each click moves it on, then mixed, then off"),
                TextBlock("Filter: " + filter switch { true => "on", false => "off", null => "mixed" })),
            Separator(),
            SampleApp.Row(
                RepeatButton("-", () => updateVolume(v => Math.Max(0, v - 1))).Tip(tips, "Hold to keep turning it down"),
                TextBlock($"Volume {volume}"),
                RepeatButton("+", () => updateVolume(v => Math.Min(100, v + 1))).Tip(tips, "Hold to keep turning it up")),
            Separator(),
            SampleApp.Row(
                // No NavigateUri: a click here runs the callback and opens no browser.
                HyperlinkButton("A link-styled button", onClick: () => setLinks(links + 1)).Tip(tips, "Looks like a link, clicks like a button"),
                TextBlock(links == 1 ? "Link clicked once" : $"Link clicked {links} times")))
            .Margin(12);
    }
}
