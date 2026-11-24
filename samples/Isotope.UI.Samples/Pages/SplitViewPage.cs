using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;
using SplitViewDisplayMode = Avalonia.Controls.SplitViewDisplayMode;

namespace Isotope.UI.Samples;

/// <summary>
/// SplitView (#26): the pane opens and closes from a toggle button, and the control's own changes (a light dismiss in
/// the overlay modes) come back through OnPaneOpenChanged, so the button follows. The pane's buttons pick the content.
/// </summary>
public static class SplitViewPage
{
    public static readonly string[] ModeNames = ["Inline", "Overlay", "Compact inline", "Compact overlay"];

    private static readonly SplitViewDisplayMode[] Modes =
        [SplitViewDisplayMode.Inline, SplitViewDisplayMode.Overlay, SplitViewDisplayMode.CompactInline, SplitViewDisplayMode.CompactOverlay];

    public static readonly string[] Sections = ["Inbox", "Sent", "Archive"];

    public static Element Render(RenderContext ctx)
    {
        var tips = ctx.UseContext(SampleApp.ShowTips);
        var (open, setOpen) = ctx.UseState(true);
        var (mode, setMode) = ctx.UseState(0);
        var (section, setSection) = ctx.UseState(Sections[0]);

        var pane = VStack(4, Sections.Select(s => (Element)Button(s, () => setSection(s)).Width(140)).ToArray()).Margin(8);
        var content = (Border(VStack(8,
            TextBlock("Showing " + section),
            TextBlock("In an overlay mode, a click here closes the pane."))) with
        {
            Background = Brushes.Transparent, Padding = new Thickness(12),
        });

        return FlexColumn(
            SampleApp.Row(
                ToggleButton("Pane", open, setOpen).Tip(tips, "Opens and closes the pane"),
                ComboBox(ModeNames, mode, setMode).Tip(tips, "The display mode"),
                TextBlock("Pane " + (open ? "open" : "closed"))),
            (SplitView(pane, content) with
            {
                IsPaneOpen = open,
                OnPaneOpenChanged = setOpen,
                DisplayMode = Modes[mode],
                OpenPaneLength = 160,
                CompactPaneLength = 48,
                PaneBackground = Brushes.Gainsboro,
            }).Flex(grow: 1))
            with { RowGap = 8, FlexPadding = new Thickness(12) };
    }
}
