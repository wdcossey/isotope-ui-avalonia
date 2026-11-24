using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace Isotope.UI.Samples;

/// <summary>NumberBox (#26): a number box and a slider showing one state, with a range, a step and a reset.</summary>
public static class NumbersPage
{
    public const double Start = 20;

    public static Element Render(RenderContext ctx)
    {
        var tips = ctx.UseContext(SampleApp.ShowTips);
        var (value, setValue) = ctx.UseState(Start);
        // A whole number: a dragged slider gives fractions. An emptied box reports NaN: the value stays as it was.
        void Set(double v)
        {
            if (!double.IsNaN(v)) setValue(Math.Round(v));
        }

        return VStack(12,
            TextBlock("Type a number (then Tab or Enter), use the spinner or the arrow keys, or drag the slider:"),
            TextBlock("both show one whole number from 0 to 100, and the spinner and arrow keys step by 5."),
            (NumberBox(value, Set) with { Minimum = 0, Maximum = 100, SmallChange = 5, FormatString = "F0" })
                .Width(160).HAlign(HorizontalAlignment.Left)
                .Tip(tips, "Up and down change it by 5"),
            Slider(value, 0, 100, Set).Width(300).HAlign(HorizontalAlignment.Left),
            TextBlock($"Value: {value:F0}"),
            Button("Reset", () => setValue(Start)))
            .Margin(12);
    }
}
