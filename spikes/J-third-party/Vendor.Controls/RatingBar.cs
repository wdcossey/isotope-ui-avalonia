using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Vendor.Controls;

/// <summary>What a <see cref="RatingBar"/> raises when its value changes.</summary>
public sealed class RatingChangedEventArgs(int oldValue, int newValue) : EventArgs
{
    public int OldValue { get; } = oldValue;
    public int NewValue { get; } = newValue;
}

/// <summary>
/// A plain (not templated) control: a row of <see cref="Maximum"/> squares, the first <see cref="Value"/> of them
/// filled with <see cref="Fill"/>. A click picks a value. It raises <see cref="ValueChanged"/> after every change of
/// <see cref="Value"/>, whoever made it (a click or code), as Avalonia's own controls raise theirs.
/// </summary>
public class RatingBar : Control
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<RatingBar, int>(nameof(Value), coerce: (o, v) => Math.Clamp(v, 0, ((RatingBar)o).Maximum));

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<RatingBar, int>(nameof(Maximum), 5, validate: v => v is >= 1 and <= 50);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<RatingBar, IBrush?>(nameof(Fill), Brushes.Goldenrod);

    public static readonly StyledProperty<double> ItemSizeProperty =
        AvaloniaProperty.Register<RatingBar, double>(nameof(ItemSize), 16);

    static RatingBar()
    {
        AffectsRender<RatingBar>(ValueProperty, MaximumProperty, FillProperty);
        AffectsMeasure<RatingBar>(MaximumProperty, ItemSizeProperty);
    }

    public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double ItemSize { get => GetValue(ItemSizeProperty); set => SetValue(ItemSizeProperty, value); }

    /// <summary>Raised after <see cref="Value"/> changes.</summary>
    public event EventHandler<RatingChangedEventArgs>? ValueChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
            ValueChanged?.Invoke(this, new RatingChangedEventArgs(change.GetOldValue<int>(), change.GetNewValue<int>()));
        else if (change.Property == MaximumProperty)
            CoerceValue(ValueProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(ItemSize * Maximum, ItemSize);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Value = (int)(e.GetPosition(this).X / ItemSize) + 1;
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Brushes.Gray, 1);
        for (int i = 0; i < Maximum; i++)
        {
            var rect = new Rect(i * ItemSize + 1, 1, ItemSize - 2, ItemSize - 2);
            context.DrawRectangle(i < Value ? Fill : null, pen, rect);
        }
    }
}
