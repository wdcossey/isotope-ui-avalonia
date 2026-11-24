# Writing components

A component is a C# function from state to UI: it takes a `RenderContext`, reads its state through hooks, and returns
an `Element`, a description of the controls to show. Isotope mounts the description as Avalonia controls and, when the
state changes, renders the function again and updates only the controls that changed. There is no XAML, no binding
and no view model.

Every code block on this page is a region of the test project (`tests/Isotope.UI.Avalonia.Tests`), compiled and run by
its tests, and `DocSnippetTests` fails if the page and the code drift apart.

## The vocabulary

Component files import the element factories and the element types:
`using Isotope.UI;`, `using Isotope.UI.Core;` and `using static Isotope.UI.Factories;`. The factories keep
Microsoft.UI.Reactor's names; their value types (`Thickness`, `IBrush`, `Orientation`, …) are Avalonia's.

| Factory | Element | Avalonia control |
|---|---|---|
| `TextBlock(content)` | `TextBlockElement` | `TextBlock` |
| `Button(label, onClick)` | `ButtonElement` | `Button` |
| `TextBox(value, onChanged, placeholderText)` | `TextBoxElement` | `TextBox` |
| `Slider(value, min, max, onValueChanged)` | `SliderElement` | `Slider` |
| `CheckBox(isChecked, onIsCheckedChanged, label)`, `ThreeStateCheckBox(checkedState, onCheckedStateChanged, label)` | `CheckBoxElement` | `CheckBox` |
| `ToggleSwitch(isOn, onIsOnChanged, onContent, offContent, header)` | `ToggleSwitchElement` | `ToggleSwitch` (`IsOn` is its `IsChecked`, `header` its `Content`) |
| `RadioButton(label, isChecked, onIsCheckedChanged, groupName)` | `RadioButtonElement` | `RadioButton` |
| `ComboBox(items, selectedIndex, onSelectedIndexChanged)` | `ComboBoxElement` | `ComboBox` |
| `ListBox(items, selectedIndex, onSelectedIndexChanged)` | `ListBoxElement` | `ListBox` |
| `DatePicker(date, onDateChanged)` | `DatePickerElement` | `DatePicker` (`Date` is its `SelectedDate`) |
| `TimePicker(time, onTimeChanged)` | `TimePickerElement` | `TimePicker` (`Time` is its `SelectedTime`) |
| `Progress(value)`, `ProgressIndeterminate()` | `ProgressElement` | `ProgressBar` |
| `Image(source)` | `ImageElement` | `Image` (a file path, `file://` or `avares://` URI) |
| `Border(child)` | `BorderElement` | `Border` |
| `ScrollViewer(child)` | `ScrollViewerElement` | `ScrollViewer` |
| `Expander(header, content, isExpanded, onIsExpandedChanged)` | `ExpanderElement` | `Expander` |
| `Grid(columns, rows, ...)` | `GridElement` | `Grid` (tracks are `GridSize.Auto`, `GridSize.Star(weight)`, `GridSize.Px(size)`) |
| `WrapPanel(...)` | `WrapPanelElement` | `WrapPanel` (Avalonia-only, so Avalonia's name) |
| `VStack(...)`, `HStack(...)`, `VStack(spacing, ...)`, `HStack(spacing, ...)` | `StackElement` | `StackPanel` |
| `Flex(...)`, `Flex(direction, ...)`, `FlexRow(...)`, `FlexColumn(...)` | `FlexElement` | `FlexPanel` (flexbox) |
| `Component(render)`, `RenderEachTime(render)` | `FuncElement` | a function component |
| `Component<T>()`, `Component<T, TProps>(props)` | `ComponentElement` | a `Component` class |
| `Memo(render, dependencies...)` | `MemoElement` | a function component that re-renders only when a dependency changes |
| `Group(...)`, `Empty()` | `GroupElement`, `EmptyElement` | none (a fragment; nothing) |

> **Coming from Reactor?** `Component(render)` is an Isotope addition: it wraps an inline function as a component, the
> same element upstream's `RenderEachTime(render)` builds. Both exist here, and `RenderEachTime` works as it does in
> Reactor. `TextBlock` keeps its name: it's the same control in WinUI and Avalonia.

Children may be `null`, so `show ? TextBlock("Saved") : null` is a child that comes and goes. Its unkeyed siblings
keep their controls when it does. An input control (`TextBox`, `Slider`, `CheckBox`, `ToggleSwitch`, `RadioButton`,
`ComboBox`, `ListBox`, `Expander`, `DatePicker`, `TimePicker`) given no value owns its own; given the state's value
and a change callback, it is controlled by it: what the user changes reaches the callback, and what the state writes
doesn't come back through it.

Inside a `Grid`, `.Grid(row: 1, column: 0, columnSpan: 2)` sets a child's cell. With the factories imported, a bare
`Grid`, `Border` or `ScrollViewer` names the factory, so Avalonia's attached-property calls need the type in full
(`Avalonia.Controls.Grid.GetRow(...)`) or an alias.

Modifiers and keys chain onto any element: `.Margin(...)` (uniform, horizontal and vertical, per side or a
`Thickness`), `.Width(...)`, `.Height(...)`, `.Size(...)`, `.HAlign(...)`, `.VAlign(...)`, `.Opacity(...)`,
`.IsEnabled(...)`, `.Background(...)` (a brush or a colour string), `.AutomationName(...)` and `.WithKey(...)`. Inside
a `Flex` container, `.Flex(grow: 1, ...)` sets a child's flex data. Container settings that have no factory parameter
are set with `with`: `FlexRow(...) with { ColumnGap = 8 }`.

More factories arrive with their controls. An app host that opens a window on a component is #16; the tests mount
components with a test host.

## A counter

The state is a number, `UseState` holds it, and the button's click sets it. Setting it asks for a render, which builds
the new description, and Isotope updates the text block's text in place.

<!-- snippet: counter -->
```csharp
public static Element Counter(RenderContext ctx)
{
    var (count, setCount) = ctx.UseState(0);
    return VStack(
        TextBlock($"Clicked {count} times"),
        Button("Click", () => setCount(count + 1)));
}
```

## A todo app

A controlled text box (`UseState`), the list in a reducer (`UseReducer`), keyed rows that are components with their own
state, a derived count (`UseMemo`), an accent from a context (`UseContext`) and a flexbox layout. The rows are keyed
by the todo's id, so when the list is reversed each row keeps its controls and its own toggle count.

<!-- snippet: todo -->
```csharp
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
```

## The hooks

Hooks are called at the top of a component, in the same order on every render.

### UseState

A value the component keeps between renders, and a setter that asks for a render when the value changes.

<!-- snippet: use-state -->
```csharp
public static Element Toggle(RenderContext ctx)
{
    var (on, setOn) = ctx.UseState(false);
    return Button(on ? "On" : "Off", () => setOn(!on));
}
```

### UseReducer

State changed by actions: the reducer takes the state and an action and returns the next state.

<!-- snippet: use-reducer -->
```csharp
public static Element Stepper(RenderContext ctx)
{
    var (value, dispatch) = ctx.UseReducer<int, string>(
        (n, action) => action == "+" ? n + 1 : n - 1, 0);
    return HStack(
        Button("-", () => dispatch("-")),
        TextBlock(value.ToString()),
        Button("+", () => dispatch("+")));
}
```

### UseEffect

Work done after the render is applied, such as subscribing to something outside the component. The function it
returns cleans up. The effect runs again when a dependency changes, and the cleanup runs first.

<!-- snippet: use-effect -->
```csharp
public sealed class Ticker
{
    public event Action<int>? Ticked;
    public void Tick(int n) => Ticked?.Invoke(n);
}

public static Element LastTick(RenderContext ctx, Ticker ticker)
{
    var (last, setLast) = ctx.UseState(0);
    ctx.UseEffect(() =>
    {
        ticker.Ticked += setLast;               // runs after the render is applied
        return () => ticker.Ticked -= setLast;  // on unmount, or before re-running for a new ticker
    }, ticker);
    return TextBlock($"Last tick: {last}");
}
```

### UseMemo

A derived value, computed again only when a dependency changes.

<!-- snippet: use-memo -->
```csharp
public static Element Search(RenderContext ctx, string[] names)
{
    var (query, setQuery) = ctx.UseState("");
    var matches = ctx.UseMemo(
        () => names.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(),
        query, names);
    return VStack(
        TextBox(query, setQuery, placeholderText: "Search"),
        TextBlock($"{matches.Length} of {names.Length} match"));
}
```

### UseCallback

A delegate that stays the same object until a dependency changes. Everything it captures that can change is a
dependency.

<!-- snippet: use-callback -->
```csharp
public static Element SaveButton(RenderContext ctx, string draft, Action<string> save)
{
    // The same delegate on every render until draft changes, so a memoized child that takes it can skip.
    var onSave = ctx.UseCallback(() => save(draft), draft, save);
    return Button("Save", onSave);
}
```

### UseRef

A mutable box that keeps its value between renders. Changing it doesn't ask for a render.

<!-- snippet: use-ref -->
```csharp
public static Element RenderCounter(RenderContext ctx)
{
    var renders = ctx.UseRef(0);
    renders.Current++; // a ref keeps a value between renders and never asks for one
    var (clicks, setClicks) = ctx.UseState(0);
    return Button($"Rendered {renders.Current} times", () => setClicks(clicks + 1));
}
```

### UseContext

A value provided to a subtree with `.Provide(context, value)`, read anywhere below it. Outside any provider it is the
context's default.

<!-- snippet: use-context -->
```csharp
public static readonly Context<string> Greeting = new("Hello");

public static Element Greeter(RenderContext ctx) => TextBlock($"{ctx.UseContext(Greeting)}, world");

public static Element Greeters(RenderContext ctx) => VStack(
    Component(Greeter),
    Component(Greeter).Provide(Greeting, "Bonjour"));
```

### Platform hooks

The theme, the window and the displays, with Reactor's names: `UseColorScheme`, `UseIsDarkTheme`, `UseHighContrast`,
`UseWindow`, `UseWindowSize`, `UseBreakpoint`, `UseWindowState`, `UseIsActive`, `UseWindowPosition`, `UseDpi`,
`UseDisplays` and `UseClosingGuard`. Each reads its value during the render and re-renders the component when it
changes. See [Platform hooks](platform-hooks.md) for what each returns, examples, and what differs from Reactor.

## Running an app

An app is a root component in a window. `ReactorApp.Run` takes the app's Avalonia `AppBuilder` (its `Application`,
platform and theme), opens one window around the root component and runs until it closes; the sample app
([`samples/Isotope.UI.Samples`](../../samples/Isotope.UI.Samples/Program.cs)) does this with the counter and the todo
app. Run it with `dotnet run` in that folder.

The window's content is a `ReactorHostControl` (`using Isotope.UI.Hosting;`), which can also hold a root component
anywhere in an existing Avalonia layout. It renders on Avalonia's dispatcher: however many state changes arrive before
a pass runs, the root renders once. When a render throws, the host shows the exception (or what the
`RenderErrorHandler` returns, or `ReactorApp.DefaultRenderErrorHandler` for every host), and the next state change
renders again. Leaving the visual tree doesn't unmount it; `Dispose` does.

<!-- snippet: host-control -->
```csharp
// A root component inside any Avalonia layout: here, a window's content.
var host = new ReactorHostControl(Counter)
{
    // Optional: what to show when a render throws (null keeps the built-in panel with the exception).
    RenderErrorHandler = error => TextBlock($"Something broke: {error.Exception.Message}"),
};
window.Content = host;

// When it's no longer needed: unmounts the tree and runs the effects' cleanups.
host.Dispose();
```
