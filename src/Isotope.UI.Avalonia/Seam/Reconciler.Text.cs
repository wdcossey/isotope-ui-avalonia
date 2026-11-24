using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Isotope.UI.Core;

// Text styling (#42): the element-wide Foreground, FontSize, FontWeight and FontFamily modifiers, written where Avalonia
// has the property, as upstream writes them on a WinUI Control or TextBlock: on a TextBlock, or a TemplatedControl (a
// button, a text box, a check box, …). Elsewhere (a panel, a border) they do nothing, as upstream's.
public sealed partial class Reconciler
{
    // On a text block, FontSize, FontWeight and FontFamily have a second source, the element's own member: written as one
    // value, the element's first, so neither overwrites the other (as Background is on a Border).
    private static void ApplyTextModifiers(UIElement control, Element? oldEl, ElementModifiers? oldM, Element newEl, ElementModifiers? newM)
    {
        var oldText = oldEl as TextBlockElement;
        var newText = newEl as TextBlockElement;
        switch (control)
        {
            case TextBlock:
                SetRef(control, TextBlock.ForegroundProperty, oldM?.Foreground, newM?.Foreground);
                Set(control, TextBlock.FontSizeProperty, oldText?.FontSize ?? oldM?.FontSize, newText?.FontSize ?? newM?.FontSize);
                Set(control, TextBlock.FontWeightProperty, oldText?.FontWeight ?? oldM?.FontWeight, newText?.FontWeight ?? newM?.FontWeight);
                SetRef(control, TextBlock.FontFamilyProperty, oldText?.FontFamily ?? oldM?.FontFamily, newText?.FontFamily ?? newM?.FontFamily);
                break;
            case TemplatedControl when oldM is not null || newM is not null:
                SetRef(control, TemplatedControl.ForegroundProperty, oldM?.Foreground, newM?.Foreground);
                Set(control, TemplatedControl.FontSizeProperty, oldM?.FontSize, newM?.FontSize);
                Set(control, TemplatedControl.FontWeightProperty, oldM?.FontWeight, newM?.FontWeight);
                SetRef(control, TemplatedControl.FontFamilyProperty, oldM?.FontFamily, newM?.FontFamily);
                break;
        }
    }
}
