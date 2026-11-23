using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// End-to-end examples (#2): UI as a function of state, written as function components with hooks, on Avalonia 12.
/// ExampleTests drive them headless and render them to PNG. The regions are the README's and the guide's snippets
/// (DocSnippetTests checks they match).
/// </summary>
internal static class Examples
{
    #region counter
    public static Element Counter(RenderContext ctx)
    {
        var (count, setCount) = ctx.UseState(0);
        return VStack(
            TextBlock($"Clicked {count} times"),
            Button("Click", () => setCount(count + 1)));
    }
    #endregion

    #region todo
    public sealed record Todo(int Id, string Title, bool Done);

    private enum TodoAction { Add, Toggle, Remove, Reverse }

    private sealed record TodoCommand(TodoAction Action, int Id = 0, string Title = "");

    private sealed record TodoState(Todo[] Items, int NextId);

    private static TodoState Reduce(TodoState s, TodoCommand c) => c.Action switch
    {
        TodoAction.Add => s with { Items = [.. s.Items, new Todo(s.NextId, c.Title, false)], NextId = s.NextId + 1 },
        TodoAction.Toggle => s with { Items = s.Items.Select(t => t.Id == c.Id ? t with { Done = !t.Done } : t).ToArray() },
        TodoAction.Remove => s with { Items = s.Items.Where(t => t.Id != c.Id).ToArray() },
        TodoAction.Reverse => s with { Items = s.Items.Reverse().ToArray() },
        _ => s,
    };

    /// <summary>The accent behind the title, provided by the app.</summary>
    public static readonly Context<IBrush?> Accent = new(null);

    public static Element TodoApp(RenderContext ctx)
    {
        var (draft, setDraft) = ctx.UseState("");
        var (state, dispatch) = ctx.UseReducer<TodoState, TodoCommand>(Reduce, new TodoState([], 1));
        // One dependency, the array, compared by reference: the reducer returns a new array on every change.
        var left = ctx.UseMemo(() => state.Items.Count(t => !t.Done), (object)state.Items);
        var accent = ctx.UseContext(Accent);

        void Add()
        {
            if (draft.Trim().Length == 0) return;
            dispatch(new TodoCommand(TodoAction.Add, Title: draft.Trim()));
            setDraft("");
        }

        return FlexColumn(
            TextBlock("Todos").Background(accent),
            FlexRow(
                TextBox(draft, setDraft, placeholderText: "What needs doing?").Flex(grow: 1),
                Button("Add", Add)) with { ColumnGap = 8 },
            VStack(4, state.Items
                .Select(t => (Element)Component(c => TodoRow(c, t, dispatch)).WithKey("todo-" + t.Id))
                .ToArray()).Flex(grow: 1),
            FlexRow(
                TextBlock($"{left} of {state.Items.Length} left").Flex(grow: 1),
                Button("Reverse", () => dispatch(new TodoCommand(TodoAction.Reverse))))
                with { ColumnGap = 8, AlignItems = FlexAlign.Center })
            with { RowGap = 8, FlexPadding = new Thickness(12) };
    }

    // One row: its own hook state (how many times it was toggled), which stays with the row when the list reorders.
    private static Element TodoRow(RenderContext ctx, Todo todo, Action<TodoCommand> dispatch)
    {
        var (toggles, setToggles) = ctx.UseState(0);
        return FlexRow(
            TextBlock((todo.Done ? "[x] " : "[ ] ") + todo.Title).Flex(grow: 1),
            TextBlock($"toggled {toggles}"),
            Button(todo.Done ? "Undo" : "Done", () =>
            {
                setToggles(toggles + 1);
                dispatch(new TodoCommand(TodoAction.Toggle, todo.Id));
            }),
            Button("Remove", () => dispatch(new TodoCommand(TodoAction.Remove, todo.Id))))
            with { ColumnGap = 8, AlignItems = FlexAlign.Center };
    }
    #endregion
}
