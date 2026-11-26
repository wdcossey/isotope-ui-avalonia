using Avalonia.Controls;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>List boxes (#94), for docs/guide/writing-components.md (DocSnippetTests); ListBoxTests runs them.</summary>
internal static class ListBoxExamples
{
    #region list-box
    public sealed record Person(string Id, string Name);

    public static Element People(RenderContext ctx, IReadOnlyList<Person> people)
    {
        var (selected, setSelected) = ctx.UseState(-1);
        var (opened, setOpened) = ctx.UseState("");
        return VStack(8,
            // Data items: the template builds each row, keyed by the person's id, so a row that moves keeps its control.
            ListBox(people, p => p.Id, (p, i) => TextBlock($"{i + 1}. {p.Name}")) with
            {
                SelectedIndex = selected,
                OnSelectedIndexChanged = setSelected,
                OnItemClick = p => setOpened(p.Name),
            },
            TextBlock(opened == "" ? "Click a person" : "Opened " + opened),
            // Elements as items, several selected with Ctrl and Shift.
            ListBox(TextBlock("Apples"), TextBlock("Pears"), TextBlock("Plums")).SelectionMode(SelectionMode.Multiple));
    }
    #endregion
}
