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
        var oldText = FontMembers(oldEl);
        var newText = FontMembers(newEl);
        switch (control)
        {
            case TextBlock:
                SetRef(control, TextBlock.ForegroundProperty, oldM?.Foreground, newM?.Foreground);
                Set(control, TextBlock.FontSizeProperty, oldText.Size ?? oldM?.FontSize, newText.Size ?? newM?.FontSize);
                Set(control, TextBlock.FontWeightProperty, oldText.Weight ?? oldM?.FontWeight, newText.Weight ?? newM?.FontWeight);
                SetRef(control, TextBlock.FontFamilyProperty, oldText.Family ?? oldM?.FontFamily, newText.Family ?? newM?.FontFamily);
                break;
            case TemplatedControl when oldM is not null || newM is not null:
                SetRef(control, TemplatedControl.ForegroundProperty, oldM?.Foreground, newM?.Foreground);
                Set(control, TemplatedControl.FontSizeProperty, oldM?.FontSize, newM?.FontSize);
                Set(control, TemplatedControl.FontWeightProperty, oldM?.FontWeight, newM?.FontWeight);
                SetRef(control, TemplatedControl.FontFamilyProperty, oldM?.FontFamily, newM?.FontFamily);
                break;
        }
    }

    // The font members of a text block's element: a text block's, or a selectable text block's (#65).
    private static (double? Size, Avalonia.Media.FontWeight? Weight, Avalonia.Media.FontFamily? Family) FontMembers(Element? element)
        => element switch
        {
            TextBlockElement t => (t.FontSize, t.FontWeight, t.FontFamily),
            SelectableTextBlockElement s => (s.FontSize, s.FontWeight, s.FontFamily),
            _ => default,
        };
}
