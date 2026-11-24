using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Layout modifiers (#49), for docs/guide/writing-components.md (DocSnippetTests); LayoutModifierTests runs it.</summary>
internal static class LayoutModifierExamples
{
    #region layout-modifiers
    public static Element Card(string title, string body, bool selected) =>
        VStack(4,
            TextBlock(title).SemiBold(),
            TextBlock(body).TextWrapping())
            .Padding(12)
            .WithBorder(selected ? "DodgerBlue" : "LightGray", 2)
            .CornerRadius(8)
            .MaxWidth(320);
    #endregion
}
