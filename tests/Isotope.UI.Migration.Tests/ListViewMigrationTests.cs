using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>
/// Reactor's ListView and ListView&lt;T&gt; (#94): the list box, with Avalonia's selection modes; the members with no
/// counterpart are reported with what to do.
/// </summary>
public class ListViewMigrationTests
{
    const string Usings = """
        using System;
        using System.Collections.Generic;
        using Isotope.UI;
        using Isotope.UI.Core;
        using Microsoft.UI.Xaml.Controls;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void ListView_becomes_a_ListBox_and_its_selection_modes_become_Avalonia_s()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(int index, Action<int> setIndex, int mode) => ListView(index, setIndex,
                        TextBlock("Apples"), TextBlock("Pears"))
                    .SelectionMode(mode switch
                    {
                        1 => ListViewSelectionMode.Multiple,
                        2 => ListViewSelectionMode.Extended,
                        _ => ListViewSelectionMode.Single,
                    });

                public static bool IsMultiple(ListViewElement list) => list.SelectionMode == ListViewSelectionMode.Multiple;

                public static bool InPatterns(ListViewElement list) =>
                    list.SelectionMode is ListViewSelectionMode.Multiple
                    || list.SelectionMode switch { ListViewSelectionMode.Multiple => true, _ => false };
            }
            """;
        var before = Migrate.Analyze(reactor).Describe();
        Assert.Contains("ISOMIG009: Factory 'ListView' is 'ListBox' in Isotope", before);

        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("ListBox(index, setIndex,", fixedText);
        Assert.Contains("1 => (SelectionMode.Multiple | SelectionMode.Toggle),", fixedText);
        Assert.Contains("2 => SelectionMode.Multiple,", fixedText);
        Assert.Contains("_ => SelectionMode.Single,", fixedText);
        // In parentheses, so the comparison still compares with both flags.
        Assert.Contains("IsMultiple(ListBoxElement list) => list.SelectionMode == (SelectionMode.Multiple | SelectionMode.Toggle);", fixedText);
        Assert.Contains("list.SelectionMode is (SelectionMode.Multiple | SelectionMode.Toggle)", fixedText);
        Assert.Contains("list.SelectionMode switch { (SelectionMode.Multiple | SelectionMode.Toggle) => true, _ => false }", fixedText);
        Assert.Contains("using Avalonia.Controls;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }

    [Fact]
    public void A_typed_ListView_becomes_a_typed_ListBox_with_its_typed_callbacks()
    {
        const string reactor = Usings + """
            record Animal(int Id, string Name);

            static class C
            {
                public static Element Render(IReadOnlyList<Animal> animals, int selected, Action<int> select, Action<Animal> open) =>
                    ListView(animals, a => a.Id.ToString(), (a, i) => TextBlock(a.Name)) with
                    {
                        SelectedIndex = selected,
                        OnSelectedIndexChanged = select,
                        OnItemClick = open,
                    };
            }
            """;
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("ListBox(animals, a => a.Id.ToString(), (a, i) => TextBlock(a.Name)) with", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }

    [Fact]
    public void A_header_and_the_None_selection_mode_are_reported_with_what_to_do()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() =>
                    (ListView(TextBlock("a")) with { Header = "Fruit" }).SelectionMode(ListViewSelectionMode.None);
            }
            """;
        var diagnostics = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        Assert.Contains(diagnostics, d => d.StartsWith("ISOMIG010: Member 'ListBoxElement.Header' is gone in Isotope", StringComparison.Ordinal)
            && d.Contains("put a TextBlock above it", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.StartsWith("ISOMIG005: 'ListViewSelectionMode.None' has no Avalonia counterpart", StringComparison.Ordinal)
            && d.Contains("SelectionMode.Single", StringComparison.Ordinal));
    }
}
