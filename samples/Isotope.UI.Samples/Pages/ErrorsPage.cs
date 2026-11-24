using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// An error boundary (#25): the panel inside it throws from its render when "Throw" is pressed, the boundary shows its
/// fallback with the message and a retry, and the counter outside the boundary keeps its state throughout.
/// </summary>
public static class ErrorsPage
{
    public const string Message = "the Throw button was pressed";

    public static Element Render(RenderContext ctx)
    {
        var (broken, setBroken) = ctx.UseState(false);
        var (failures, setFailures) = ctx.UseState(0);

        return VStack(12,
            TextBlock("The panel below is inside an error boundary; the counter under it is not."),
            (Border(ErrorBoundary(
                Component(c => Fragile(c, broken, () =>
                {
                    setFailures(failures + 1);
                    setBroken(true);
                })),
                error => VStack(8,
                    TextBlock("The panel failed: " + error.Message),
                    // Any re-render of the boundary retries its child; this one also clears the cause.
                    Button("Retry", () => setBroken(false))))) with
            {
                BorderBrush = Brushes.Gray, BorderThickness = 1, CornerRadius = 3, Padding = new Thickness(12),
            }),
            TextBlock(failures == 1 ? "The panel has failed once" : $"The panel has failed {failures} times"),
            TextBlock("Outside the boundary:"),
            Component(SampleApp.Counter))
            .Margin(12);
    }

    // The part that can fail: it throws from its render while 'broken' is set.
    private static Element Fragile(RenderContext ctx, bool broken, Action breakIt)
    {
        var (clicks, setClicks) = ctx.UseState(0);
        if (broken) throw new InvalidOperationException(Message);
        return VStack(8,
            TextBlock($"The panel works: clicked {clicks} times"),
            SampleApp.Row(
                Button("Click the panel", () => setClicks(clicks + 1)),
                Button("Throw", breakIt)));
    }
}
