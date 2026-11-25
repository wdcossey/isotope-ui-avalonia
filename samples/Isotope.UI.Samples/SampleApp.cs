using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// The sample app: one tab per area (#30), so a real-window run can check what headless tests can't (menus opened with
/// the mouse, hover tooltips, right-click menus, the look of tabs and the split view). UI as a function of state: each
/// page is a C# function of its hooks' state, and the root holds the selected page and the tooltip switch.
/// </summary>
public static class SampleApp
{
    /// <summary>The accent behind the todo title, provided by the Basics page.</summary>
    public static readonly Context<IBrush?> Accent = new(null);

    /// <summary>Whether the pages put tooltips on their controls (the View menu's "Show tooltips").</summary>
    public static readonly Context<bool> ShowTips = new(true);

    /// <summary>The window's size, as Program opens it.</summary>
    public const double WindowWidth = 720, WindowHeight = 680;

    /// <summary>The pages, in tab order: each tab and each item of the Go menu comes from this list.</summary>
    public static readonly (string Name, Func<RenderContext, Element> Render)[] Pages =
    [
        ("Basics", Basics),
        ("Buttons", ButtonsPage.Render),
        ("Menus", MenusPage.Render),
        ("Numbers", NumbersPage.Render),
        ("Split view", SplitViewPage.Render),
        ("Rich text", RichTextPage.Render),
        ("Errors", ErrorsPage.Render),
    ];

    public static Element Root(RenderContext ctx)
    {
        var (page, setPage) = ctx.UseState(0);
        var (tips, setTips) = ctx.UseState(true);
        return FlexColumn(
            // The Go menu and the tabs show one state: choosing either moves both.
            MenuBar(
                Menu("Go", Pages.Select((p, i) => (MenuFlyoutItemBase)RadioMenuItem(p.Name, "page", i == page, () => setPage(i))).ToArray()),
                Menu("View", ToggleMenuItem("Show tooltips", tips, setTips))),
            TabControl(page, setPage, Pages.Select(p => TabItem(p.Name, Component(p.Render))).ToArray())
                .Provide(ShowTips, tips)
                .Flex(grow: 1));
    }

    /// <summary>A row of controls, eight apart and centred on one line.</summary>
    public static FlexElement Row(params Element?[] children) =>
        FlexRow(children) with { ColumnGap = 8, AlignItems = FlexAlign.Center };

    /// <summary>Puts a tooltip on <paramref name="el"/> when <paramref name="on"/> (read from <see cref="ShowTips"/>).</summary>
    public static T Tip<T>(this T el, bool on, string tip) where T : Element => on ? el.ToolTip(tip) : el;

    /// <summary>The counter and the todo app of the guide ("Writing components"), one above the other.</summary>
    public static Element Basics(RenderContext ctx) =>
        FlexColumn(
            TextBlock("Counter"),
            Component(Counter),
            TextBlock("Todo"),
            Component(TodoApp).Provide(Accent, Brushes.LightSteelBlue).Flex(grow: 1))
        with { RowGap = 8, FlexPadding = new Thickness(12) };

    public static Element Counter(RenderContext ctx)
    {
        var (count, setCount) = ctx.UseState(0);
        return VStack(
            TextBlock($"Clicked {count} times"),
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
        TodoAction.Reverse => s with { Items = Enumerable.Reverse(s.Items).ToArray() },
        _ => s,
    };

    public static Element TodoApp(RenderContext ctx)
    {
        var (draft, setDraft) = ctx.UseState("");
        var (state, dispatch) = ctx.UseReducer<TodoState, TodoCommand>(Reduce, new TodoState([], 1));
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
            with { RowGap = 8 };
    }

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
}
