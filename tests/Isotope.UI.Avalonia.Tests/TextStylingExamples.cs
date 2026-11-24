using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Text styling (#42), for docs/guide/writing-components.md (DocSnippetTests); TextStylingTests runs it.</summary>
internal static class TextStylingExamples
{
    #region text-styling
    public static Element Notice(string title, string body, bool urgent, Action dismiss) =>
        VStack(
            TextBlock(title).FontSize(20).SemiBold(),
            TextBlock(body).TextWrapping().MaxLines(3).TextTrimming(TextTrimming.CharacterEllipsis),
            Button("Dismiss", dismiss).Foreground(urgent ? "Red" : "Gray"));
    #endregion
}
