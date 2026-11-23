using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// End-to-end examples (#2): UI as a function of state, written as function components with hooks, on Avalonia 12.
/// ExampleTests drive them headless and render them to PNG.
/// </summary>
internal static class Examples
{
    /// <summary>The README's counter.</summary>
    public static Element Counter(RenderContext ctx)
    {
        var (count, setCount) = ctx.UseState(0);
        return VStack(
            Text($"Clicked {count} times"),
            Button("Click", () => setCount(count + 1)));
    }

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

    /// <summary>
    /// A todo list: a controlled TextBox (UseState), the list in a reducer (UseReducer, two-argument form), keyed rows
    /// (each a component with its own state), a derived count (UseMemo), a theme from a context (UseContext), and a
    /// Flex layout.
    /// </summary>
    public static Element TodoApp(RenderContext ctx)
    {
        var (draft, setDraft) = ctx.UseState("");
        var (state, dispatch) = ctx.UseReducer<TodoState, TodoCommand>(Reduce, new TodoState([], 1));
        var left = ctx.UseMemo(() => state.Items.Count(t => !t.Done), state.Items);
        var accent = ctx.UseContext(Accent);

        void Add()
        {
            if (draft.Trim().Length == 0) return;
            dispatch(new TodoCommand(TodoAction.Add, Title: draft.Trim()));
            setDraft("");
        }

        return new FlexElement([
            Text("Todos").WithModifiers(m => m with { Background = accent }),
            new FlexElement([
                new TextBoxElement(draft, setDraft, PlaceholderText: "What needs doing?").Flex(grow: 1),
                Button("Add", Add),
            ]) { ColumnGap = 8 },
            new StackElement(Orientation.Vertical,
                state.Items.Select(t => (Element)new FuncElement(c => TodoRow(c, t, dispatch)) { Key = "todo-" + t.Id }).ToArray())
            { Spacing = 4 }.Flex(grow: 1),
            new FlexElement([
                Text($"{left} of {state.Items.Length} left").Flex(grow: 1),
                Button("Reverse", () => dispatch(new TodoCommand(TodoAction.Reverse))),
            ]) { ColumnGap = 8, AlignItems = FlexAlign.Center },
        ])
        {
            Direction = FlexDirection.Column,
            RowGap = 8,
            FlexPadding = new Thickness(12),
        };
    }

    /// <summary>The accent behind the title, provided by the app.</summary>
    public static readonly Context<IBrush?> Accent = new(null);

    // One row: its own hook state (how many times it was toggled), which stays with the row when the list reorders.
    private static Element TodoRow(RenderContext ctx, Todo todo, Action<TodoCommand> dispatch)
    {
        var (toggles, setToggles) = ctx.UseState(0);
        return new FlexElement([
            Text((todo.Done ? "[x] " : "[ ] ") + todo.Title).Flex(grow: 1),
            Text($"toggled {toggles}"),
            Button(todo.Done ? "Undo" : "Done", () =>
            {
                setToggles(toggles + 1);
                dispatch(new TodoCommand(TodoAction.Toggle, todo.Id));
            }),
            Button("Remove", () => dispatch(new TodoCommand(TodoAction.Remove, todo.Id))),
        ]) { ColumnGap = 8, AlignItems = FlexAlign.Center };
    }
}
