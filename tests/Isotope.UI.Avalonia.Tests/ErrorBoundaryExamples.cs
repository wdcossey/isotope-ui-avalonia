using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>The error-boundary guide's examples (docs/guide/error-boundaries.md), run by ErrorBoundaryTests.</summary>
internal static class ErrorBoundaryExamples
{
    /// <summary>Where the weather panel gets its forecast; it throws while none has arrived.</summary>
    public static Func<string> Forecast = () => throw new InvalidOperationException("no forecast yet");

    #region error-boundary
    // A part of the window that can fail: it throws while there's no forecast.
    public static Element WeatherPanel(RenderContext ctx) => TextBlock($"Today: {Forecast()}");

    public static Element Dashboard(RenderContext ctx)
    {
        var (attempt, setAttempt) = ctx.UseState(0);
        return VStack(
            ErrorBoundary(
                Component(WeatherPanel),
                error => VStack(
                    TextBlock($"The weather panel failed: {error.Message}"),
                    // Any re-render of the boundary retries its child: here, the click's.
                    Button("Retry", () => setAttempt(attempt + 1)))),
            // Outside the boundary: it keeps its state whatever the weather panel does.
            Component(Examples.Counter));
    }
    #endregion
}
