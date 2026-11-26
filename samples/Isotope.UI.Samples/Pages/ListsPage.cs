using Avalonia.Controls;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// List boxes (#94): a typed list of fruit whose rows are components with their own counts, keyed by the fruit, so a
/// row keeps its count when the rows move; Avalonia's selection modes, the selection as items, and the item click; and
/// a thousand element rows, of which Avalonia realises only those in view. Self-contained: one function component, with
/// no other page's helpers.
/// </summary>
public static class ListsPage
{
    public sealed record Fruit(string Id, string Name) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    private static readonly string[] Names = ["Apple", "Banana", "Cherry", "Damson", "Elderberry", "Fig", "Grape", "Kiwi"];

    /// <summary>The selection modes the combo box offers, with the Avalonia flags each one sets.</summary>
    public static readonly (string Name, SelectionMode Mode)[] Modes =
    [
        ("Single", SelectionMode.Single),
        ("Multiple (Ctrl, Shift)", SelectionMode.Multiple),
        ("Toggle (each tap)", SelectionMode.Multiple | SelectionMode.Toggle),
    ];

    /// <summary>The long list's rows: built once, so a render of the page passes the same items again.</summary>
    public static readonly Element[] Thousand = Enumerable.Range(1, 1000).Select(i => (Element)TextBlock($"Row {i}").WithKey("row" + i)).ToArray();

    public static Element Render(RenderContext ctx)
    {
        var (fruits, setFruits) = ctx.UseState<IReadOnlyList<Fruit>>(Names[..4].Select((n, i) => new Fruit("f" + i, n)).ToArray());
        var (next, setNext) = ctx.UseState(4);
        var (mode, setMode) = ctx.UseState(0);
        var (selected, setSelected) = ctx.UseState<IReadOnlyList<Fruit>>([]);
        var (clicked, setClicked) = ctx.UseState("nothing yet");

        void Add()
        {
            setFruits([.. fruits, new Fruit("f" + next, Names[next % Names.Length])]);
            setNext(next + 1);
        }

        return VStack(12,
            TextBlock("A typed list: each row is a component with its own count, keyed by its fruit, so it keeps the count when the rows move.")
                .TextWrapping(),
            HStack(8,
                Button("Reverse", () => setFruits(fruits.Reverse().ToArray())),
                Button("Rotate", () => setFruits([.. fruits.Skip(1), .. fruits.Take(1)])),
                Button("Add", Add),
                ComboBox(Modes.Select(m => m.Name).ToArray(), mode, setMode)),
            (ListBox(fruits, (fruit, _) => Component(c => FruitRow(c, fruit))) with
            {
                OnSelectionChanged = setSelected,
                OnItemClick = fruit => setClicked(fruit.Name),
            }).SelectionMode(Modes[mode].Mode).Height(180),
            TextBlock("Selected: " + (selected.Count == 0 ? "none" : string.Join(", ", selected.Select(f => f.Name)))),
            TextBlock("Last clicked: " + clicked),
            TextBlock("A thousand rows: Avalonia realises containers only for the rows in view."),
            ListBox(Thousand).Height(150))
            .Margin(12);
    }

    private static Element FruitRow(RenderContext ctx, Fruit fruit)
    {
        var (count, setCount) = ctx.UseState(0);
        // The button handles its own click: the list's item click isn't raised for it.
        return HStack(8,
            TextBlock(fruit.Name).Width(90).VerticalAlignment(global::Avalonia.Layout.VerticalAlignment.Center),
            TextBlock($"counted {count}").VerticalAlignment(global::Avalonia.Layout.VerticalAlignment.Center),
            Button("+1", () => setCount(count + 1)));
    }
}
