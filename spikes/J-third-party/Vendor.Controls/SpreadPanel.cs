using Avalonia;
using Avalonia.Controls;

namespace Vendor.Controls;

/// <summary>A panel: its children side by side, each given the same width, <see cref="Spacing"/> apart.</summary>
public class SpreadPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SpreadPanel, double>(nameof(Spacing));

    static SpreadPanel() => AffectsMeasure<SpreadPanel>(SpacingProperty);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private double Slot(double width) => Children.Count == 0 ? 0 : Math.Max(0, (width - Spacing * (Children.Count - 1)) / Children.Count);

    protected override Size MeasureOverride(Size availableSize)
    {
        var slot = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Slot(availableSize.Width);
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(slot, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width + Spacing * Math.Max(0, Children.Count - 1), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slot = Slot(finalSize.Width);
        for (int i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(i * (slot + Spacing), 0, slot, finalSize.Height));
        return finalSize;
    }
}
