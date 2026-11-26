# Writing components

A component is a C# function from state to UI: it takes a `RenderContext`, reads its state through hooks, and returns
an `Element`, a description of the controls to show. Isotope mounts the description as Avalonia controls and, when the
state changes, renders the function again and updates only the controls that changed. There is no XAML, no binding
and no view model.

Every C# block on this page is a region of the test project (`tests/Isotope.UI.Avalonia.Tests`), compiled and run by
its tests, and `DocSnippetTests` fails if the page and the code drift apart.

## A project

An app's project file needs only the desktop package, `Isotope.UI.Avalonia.Desktop`: it brings the library and its
generator with it (referencing `Isotope.UI.Avalonia` too does no harm). `dotnet add package Isotope.UI.Avalonia.Desktop
--prerelease` adds the latest preview; `<version>` below is that version. The packages target `net9.0`, `net10.0` and
`net11.0`; on `net9.0`, add `<LangVersion>14</LangVersion>` for `ReactorApp.Run`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Isotope.UI.Avalonia.Desktop" Version="<version>" />
  </ItemGroup>
</Project>
```

`ReactorApp.Run(...)` in `Program.cs` opens the window (see *Running an app* below).

## The vocabulary

Component files import the element factories and the element types:
`using Isotope.UI;`, `using Isotope.UI.Core;` and `using static Isotope.UI.Factories;`. The names are Avalonia's: a
factory and its element are named after the Avalonia control (`TabControl(...)` builds a `TabControlElement`), and a
modifier after the property it sets (`.HorizontalAlignment(...)`). Microsoft.UI.Reactor's names stay where Avalonia has
no counterpart, and for the stack shortcuts `VStack` and `HStack`. Factory parameters and element members have Avalonia's names (`Text`, `Minimum`,
`IsChecked`, `SelectedDate`; a callback is `On` plus an Avalonia event, `OnClick`, or `On` plus a property and
`Changed`, `OnTextChanged`), and their value types (`Thickness`, `IBrush`, `Orientation`, …) are Avalonia's.

| Factory | Element | Avalonia control |
|---|---|---|
| `TextBlock(text)`, `TextBlock(inlines)` | `TextBlockElement` | `TextBlock` (text, or `Inlines`: see rich text below) |
| `SelectableTextBlock(text)`, `SelectableTextBlock(inlines)`, `SelectableTextBlock(paragraphs)` | `SelectableTextBlockElement` | `SelectableTextBlock` (Reactor's `RichTextBlock`) |
| `Run(text)`, `Span(...)`, `Bold(...)`, `Italic(...)`, `Underline(...)`, `LineBreak()`, `InlineUIContainer(child)`, `Paragraph(...)` | `RunElement`, `SpanElement`, …, `RichTextParagraph` | Avalonia's inlines (`Run`, `Span`, …); a paragraph is a `Span` |
| `Button(content, onClick)`, `Button(command)` | `ButtonElement` | `Button` (`content` is a string or any element) |
| `ToggleButton(content, isChecked, onIsCheckedChanged)`, `ThreeStateToggleButton(content, isChecked, onIsCheckedChanged)`, `ToggleButton(command, isChecked)` | `ToggleButtonElement` | `ToggleButton` |
| `RepeatButton(content, onClick)`, `RepeatButton(command)` | `RepeatButtonElement` | `RepeatButton` |
| `HyperlinkButton(content, navigateUri, onClick)`, `HyperlinkButton(command)` | `HyperlinkButtonElement` | `HyperlinkButton` |
| `TextBox(text, onTextChanged, placeholderText)` | `TextBoxElement` | `TextBox` |
| `AutoCompleteBox(text, onTextChanged, onQuerySubmitted)` | `AutoCompleteBoxElement` | `AutoCompleteBox` (its `ItemsSource` shown as given; `FilterMode` lets Avalonia filter them) |
| `Slider(value, minimum, maximum, onValueChanged)` | `SliderElement` | `Slider` |
| `NumericUpDown(value, onValueChanged)` | `NumericUpDownElement` | `NumericUpDown` (Reactor's `double` values; `NaN` is an empty box) |
| `CheckBox(isChecked, onIsCheckedChanged, content)`, `ThreeStateCheckBox(isChecked, onIsCheckedChanged, content)` | `CheckBoxElement` | `CheckBox` |
| `ToggleSwitch(isChecked, onIsCheckedChanged, onContent, offContent, content)` | `ToggleSwitchElement` | `ToggleSwitch` |
| `RadioButton(content, isChecked, onIsCheckedChanged, groupName)` | `RadioButtonElement` | `RadioButton` |
| `ComboBox(items, selectedIndex, onSelectedIndexChanged)` | `ComboBoxElement` | `ComboBox` |
| `ListBox(items, selectedIndex, onSelectedIndexChanged)` (strings), `ListBox(selectedIndex, onSelectedIndexChanged, items...)` (elements), `ListBox(items, keySelector, itemTemplate)` (data) | `ListBoxElement`, `ListBoxElement<T>` | `ListBox` (Reactor's `ListView` too; see list boxes below) |
| `DatePicker(selectedDate, onSelectedDateChanged)` | `DatePickerElement` | `DatePicker` |
| `TimePicker(selectedTime, onSelectedTimeChanged)` | `TimePickerElement` | `TimePicker` |
| `ProgressBar(value)`, `ProgressIndeterminate()` | `ProgressBarElement` | `ProgressBar` |
| `Image(source)` | `ImageElement` | `Image` (a file path, `file://` or `avares://` URI) |
| `Icon(PathIcon(data))`, `Icon(FontIcon(glyph))`, `Icon(BitmapIcon(source))`, `Icon(ImageIcon(source))` | `IconElement` | `PathIcon`, a `TextBlock`, a bitmap icon or an `Image`, from the data (see icons below) |
| `Border(child)` | `BorderElement` | `Border` |
| `ScrollViewer(content)` | `ScrollViewerElement` | `ScrollViewer` (Reactor's `ScrollView` is one with `HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled`, content as wide as the view; the migration writes it) |
| `Viewbox(child)` | `ViewboxElement` | `Viewbox` |
| `Expander(header, content, isExpanded, onIsExpandedChanged)` | `ExpanderElement` | `Expander` (`.Header(element)` for an element header) |
| `TabControl(selectedIndex, onSelectedIndexChanged, TabItem(header, content), ...)`, `TabControl(TabItem(...), ...)` | `TabControlElement` | `TabControl` (a `TabItem` per tab) |
| `TreeView(TreeViewItem(header, items...), ...)` | `TreeViewElement` | `TreeView` (a `TreeViewItem` per `TreeViewItemData`; `OnItemInvoked` and `OnExpanding` get the node's data) |
| `SplitView(pane, content)` | `SplitViewElement` | `SplitView` (open by default, as Reactor's) |
| `MenuBar(Menu(header, items...), ...)` | `MenuElement` | `Menu` (items: `MenuItem(header, onClick)` or `MenuItem(command)`, `ToggleMenuItem`, `RadioMenuItem`, `MenuSubItem`, `MenuSeparator()`) |
| `Separator()` | `SeparatorElement` | `Separator` (Avalonia-only, so Avalonia's name) |
| `Grid(columnDefinitions, rowDefinitions, ...)` | `GridElement` | `Grid` (tracks are `GridSize.Auto`, `GridSize.Star(weight)`, `GridSize.Px(size)`) |
| `WrapPanel(...)` | `WrapPanelElement` | `WrapPanel` (Avalonia-only, so Avalonia's name) |
| `VStack(...)`, `HStack(...)`, `VStack(spacing, ...)`, `HStack(spacing, ...)` | `StackPanelElement` | `StackPanel` |
| `Flex(...)`, `Flex(direction, ...)`, `FlexRow(...)`, `FlexColumn(...)` | `FlexElement` | `FlexPanel` (flexbox) |
| `Component(render)`, `RenderEachTime(render)` | `FuncElement` | a function component |
| `Component<T>()`, `Component<T, TProps>(props)` | `ComponentElement` | a `Component` class |
| `Memo(render, dependencies...)` | `MemoElement` | a function component that re-renders only when a dependency changes |
| `Group(...)`, `Empty()` | `GroupElement`, `EmptyElement` | none (a fragment; nothing) |
| `When(condition, then)`, `If(condition, then, otherwise)`, `Expr(render)`, `ForEach(items, render)` | the element they choose, `EmptyElement` or a `GroupElement` | none (helpers: `ForEach` keys each element from an `IReactorKeyed` item) |
| `ErrorBoundary(child, fallback)` | `ErrorBoundaryElement` | a `Border` holding the child, or the fallback when the child throws ([Error boundaries](error-boundaries.md)) |

A control library can add its own controls to this vocabulary, with factories and elements of the same kind in its own
namespace: see [Writing controls for Isotope](writing-controls.md).

> **Coming from Reactor?** `Component(render)` is an Isotope addition: it wraps an inline function as a component, the
> same element upstream's `RenderEachTime(render)` builds. Both exist here, and `RenderEachTime` works as it does in
> Reactor. Factories, elements and modifiers take Avalonia's names where Reactor's differ: `TabControl` and `TabItem`
> for `TabView` and `Tab`, `NumericUpDown` for `NumberBox`, `AutoCompleteBox` for `AutoSuggestBox`, `ProgressBar` for
> `Progress`, `ScrollViewer` for `ScrollView`, `TreeViewItem` for `TreeNode`, `.HorizontalAlignment` and
> `.VerticalAlignment` for `.HAlign` and `.VAlign` (Reactor has them as aliases), and `.ContextFlyout` for
> `.WithContextFlyout`. The elements follow (`StackPanelElement` for `StackElement`, `MenuElement` for `MenuBarElement`).
> `VStack`, `HStack` and the menu factories (`MenuBar`, `Menu`, `MenuItem`, `MenuItems`…) keep Reactor's names.
> Members and parameters take Avalonia's names too (`Text` for `Content`, `Minimum` for `Min`, `IsChecked` for `IsOn`,
> one `Content` for `Label` and `ContentElement`). `tools/naming/renames.json` lists every one, with the reason for each
> name kept, and the migration tool (#33) reads it.

Children may be `null`, so `show ? TextBlock("Saved") : null` is a child that comes and goes. Its unkeyed siblings
keep their controls when it does. An input control (`TextBox`, `AutoCompleteBox`, `Slider`, `NumericUpDown`, `CheckBox`, `ToggleSwitch`,
`RadioButton`, `ComboBox`, `ListBox`, `TabControl`, `Expander`, `DatePicker`, `TimePicker`) given no value owns its own; given the state's value
and a change callback, it is controlled by it: what the user changes reaches the callback, and what the state writes
doesn't come back through it. `ToggleButton`'s `isChecked`, a toggle menu item's and `SplitView`'s `IsPaneOpen` are
written when they change, as in Reactor: a click toggles the control, and it keeps that state until the value changes.

A button (`Button`, `ToggleButton`, `RepeatButton`, `HyperlinkButton`) takes a string or any element as its content:
`Button(HStack(Image("save.png"), TextBlock("Save")), onSave)`. An element content, a tab's content and a split view's
pane and content are reconciled in place, so a component inside keeps its state.

Inside a `Grid`, `.Grid(row: 1, column: 0, columnSpan: 2)` sets a child's cell. With the factories imported, a bare
`Grid`, `Border` or `ScrollViewer` names the factory, so Avalonia's attached-property calls need the type in full
(`Avalonia.Controls.Grid.GetRow(...)`) or an alias. The same goes for `Menu`, `MenuItem`, `SplitView`, `Separator`, `Viewbox`, `TreeView`,
`TreeViewItem`, `TabControl`, `TabItem`, `NumericUpDown`, `AutoCompleteBox` and `ProgressBar`. A type still names
Avalonia's control: `new TabControl()`, `typeof(ProgressBar)` and a variable's type are unaffected.

Modifiers and keys chain onto any element: `.Margin(...)` (uniform, horizontal and vertical, per side or a
`Thickness`), `.Width(...)`, `.Height(...)`, `.Size(...)`, `.HorizontalAlignment(...)`, `.VerticalAlignment(...)`, `.Opacity(...)`,
`.IsEnabled(...)`, `.Background(...)` (a brush or a colour string), `.AutomationName(...)`, `.ToolTip(text)` (or
`.ToolTip(text, placement)`), `.WithToolTip(element)` (any element as the tooltip; a component inside keeps its
state), `.ContextFlyout(MenuItems(...))` (a context menu, as an Avalonia `MenuFlyout`) and
`.WithKey(...)`. Inside
a `Flex` container, `.Flex(grow: 1, ...)` sets a child's flex data. Container settings that have no factory parameter
are set with `with`: `FlexRow(...) with { ColumnGap = 8 }`.

An auto-complete box reports what the user types to `onTextChanged`, a suggestion they pick to `OnSuggestionChosen`,
and Enter to `onQuerySubmitted`. Its suggestions are a function of the state, so derive them from the typed text:

<!-- snippet: auto-suggest-box -->
```csharp
public static Element FruitPicker(RenderContext ctx, string[] fruit)
{
    var (query, setQuery) = ctx.UseState("");
    var (chosen, setChosen) = ctx.UseState("");
    var matches = query.Length == 0 ? [] : fruit.Where(f => f.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    return VStack(
        AutoCompleteBox(onTextChanged: setQuery, onQuerySubmitted: setChosen).SuggestionChosen(setChosen)
            with { ItemsSource = matches },
        TextBlock($"Chosen: {chosen}"));
}
```

A `Command` bundles an action with its label, enabled state, description and shortcut. `Button(command)`,
`MenuItem(command)` and `.Command(command)` on any button use it: a click runs it, the button is disabled while
`IsEnabled` is false, the description is the tooltip and the `Accelerator` the shortcut (`Accelerator(Key.S, KeyModifiers.Control)`). It is Avalonia's
`ICommand` underneath. `UseCommand` tracks an async action, so the button is disabled while it runs:

<!-- snippet: command -->
```csharp
public static Element Uploader(RenderContext ctx, Func<Task> upload)
{
    var send = ctx.UseCommand(new Command { Label = "Upload", ExecuteAsync = upload, Description = "Upload the file" });
    return VStack(
        Button(send),
        TextBlock(send.IsExecuting ? "Uploading..." : "Ready"));
}
```

A menu item's `KeyboardAccelerators` and `Description` are its shortcut and tooltip:
`MenuItem("Save", save) with { KeyboardAccelerators = [new(Key.S, KeyModifiers.Control)] }`.

> **Coming from Reactor?** `AutoCompleteBox` (Reactor's `AutoSuggestBox`) has no `Header` or `QueryIcon` here, a command's `AccessKey` isn't written
> (Avalonia's access keys come from an underscore in the content), and menu items have no icons yet: a command's `Icon`
> isn't drawn, on a menu item or a button. A click on a
> suggestion doesn't submit the query.

Text is styled with upstream's modifiers, which take Avalonia's value types: `.FontSize(20)`, `.Bold()`,
`.SemiBold()`, `.FontWeight(FontWeight.Light)`, `.FontStyle(FontStyle.Italic)`, `.FontFamily("Consolas")`,
`.TextWrapping()`, `.TextAlignment(...)`, `.TextTrimming(TextTrimming.CharacterEllipsis)`, `.LineHeight(...)` and
`.MaxLines(...)` on a text block, and `.Foreground("Red")` (or any `IBrush`), `.FontSize`, `.FontWeight` and `.FontFamily`
on a text block or any templated control (a button, a text box, …). As Reactor's, these do nothing on a panel or a
border.

<!-- snippet: text-styling -->
```csharp
public static Element Notice(string title, string body, bool urgent, Action dismiss) =>
    VStack(
        TextBlock(title).FontSize(20).SemiBold(),
        TextBlock(body).TextWrapping().MaxLines(3).TextTrimming(TextTrimming.CharacterEllipsis),
        Button("Dismiss", dismiss).Foreground(urgent ? "Red" : "Gray"));
```

> **Coming from Reactor?** `TextBlockElement.Weight` is `FontWeight`, and its `HorizontalAlignment` member is the
> `.HorizontalAlignment(...)` modifier. A text block has no `IsTextSelectionEnabled` (Avalonia's `SelectableTextBlock` is another
> control), and no `CharacterSpacing` or `TextDecorations` yet.

Rich text is a text block made of inlines: `TextBlock("Hello, ", Bold("world"))`, where a string is a run. The inlines
are Avalonia's: `Run`, `Span`, `Bold`, `Italic`, `Underline`, `LineBreak`, and `InlineUIContainer(element)`, which puts an
element in the flow of the text. They take the text modifiers (`.FontSize`, `.Foreground`, `.Bold()`, `.Italic()`,
`.Underline()`, `.Strikethrough()`, `.LetterSpacing`, …) and a key, as children do: a render updates each inline in place,
and a keyed one moves with its key. An element in an inline keeps its state. `SelectableTextBlock(...)` is text the user
can select and copy, made of inlines, or of Reactor's paragraphs, each on a line of its own:

<!-- snippet: rich-text -->
```csharp
public static Element ReleaseNote(RenderContext ctx)
{
    var (likes, setLikes) = ctx.UseState(0);
    return VStack(
        TextBlock("Version 2 is ", Bold("out"), ". Liked ", Run($"{likes}").Foreground("SeaGreen"), " times ",
            InlineUIContainer(Button("Like", () => setLikes(likes + 1)))),
        SelectableTextBlock(
            Paragraph(Italic("New: "), "rich text, as runs, spans and line breaks."),
            Paragraph("Select this text to copy it.").FontSize(12)));
}
```

> **Coming from Reactor?** `RichTextBlock` is `SelectableTextBlock`, which is always selectable, and `InlineUI` is
> `InlineUIContainer`. A run's `IsBold`, `IsItalic` and `IsStrikethrough` are `.Bold()`, `.Italic()` and `.Strikethrough()`
> (Avalonia's `FontWeight`, `FontStyle` and `TextDecorations`), and `CharacterSpacing` (1/1000 em) is `.LetterSpacing(...)`, in DIPs.
> On an inline, only the text modifiers apply: a margin or a tooltip does nothing, and is reported once on the `Isotope-UI`
> event source. Avalonia's text has no blocks, so a paragraph is a span on a
> line of its own, with no `Margin`, `TextIndent`, `TextAlignment` or `LineHeight`. There is no `Hyperlink` inline: put a
> `HyperlinkButton` in an `InlineUIContainer`. Avalonia lays the text out again whenever one of its inlines changes, and
> an element inside keeps its state but loses focus.

Icons are `Icon(data)`, where the data says what the icon shows: `PathIcon("M 0,0 L 24,0 L 12,20 Z")` a shape, an
Avalonia `PathIcon` filled with the foreground and scaled to the icon's size (20 by 20 in the Fluent theme);
`FontIcon(glyph, fontFamily, fontSize)` a glyph of a font, as a text block; `ImageIcon(uri)` an image in its own
colours, as an `Image`; and `BitmapIcon(uri)` a bitmap's shape in the foreground (`showAsMonochrome: false` draws its
colours). Size an icon with `.Width(...)` and `.Height(...)` (a glyph with its font size), and colour it with
`.Foreground(...)` or a theme token. A render with data of the same kind updates the icon's control in place; data of
another kind mounts a new control:

<!-- snippet: icons -->
```csharp
const string Tick = "M 2,13 L 5,10 L 9,14 L 19,4 L 22,7 L 9,20 Z";

public static Element SaveButton(RenderContext ctx)
{
    var (saved, setSaved) = ctx.UseState(false);
    return HStack(8,
        Button(Icon(PathIcon(Tick)).Foreground(saved ? "SeaGreen" : "Gray"), () => setSaved(true)),
        // A glyph while unsaved, a shape once saved: another kind of data mounts another control.
        Icon(saved ? PathIcon(Tick) : FontIcon("!", fontSize: 16)).Width(24).Height(24),
        Icon(ImageIcon(new Uri("avares://MyApp/Assets/logo.png"))).Width(24).Height(24));
}
```

**WinUI symbols.** On Windows, `FontIcon(glyph, "Segoe Fluent Icons")` shows a WinUI symbol from the font Windows
installs: give the glyph as its code point, `"\uE74E"` for Save, and find the others on Microsoft's
[Segoe Fluent Icons page](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font). An app that runs
elsewhere brings its own icons: a `PathIcon` with path data from an icon set whose licence lets it ship them, or a
`FontIcon` in a font it ships. Isotope ships neither a symbol set nor a font: the Segoe fonts may not be shipped to other
platforms, and which icons an app uses is its own choice.

<!-- snippet: icons-segoe -->
```csharp
// Windows only: WinUI's Save symbol, from the Segoe Fluent Icons font that Windows installs.
public static Element SaveSymbol() => Icon(FontIcon("\uE74E", "Segoe Fluent Icons"));
```

> **Coming from Reactor?** WinUI's symbols aren't here: `Icon(Symbol.Home)`, `Icon("Home")` and `SymbolIcon(...)` show
> glyphs of Windows' Segoe icon fonts, which Isotope doesn't ship (see WinUI symbols above).
> A font the app ships is named by its resource: `FontIcon(glyph, "avares://MyApp/Assets/Fonts#My Icons")`.
> A `FontIcon` with no font uses WinUI's, Segoe Fluent Icons then Segoe MDL2 Assets, which only Windows has, at WinUI's
> size, 20. Its font is a name, as upstream's (null is the default), or Avalonia's `FontFamily`, and a glyph is centred in
> a box sized with `.Width(...)` and `.Height(...)`, as WinUI's is. An `ImageIcon` or `BitmapIcon` URI is
> a file or an `avares://` resource (WinUI's `ms-appx:///` isn't).

List boxes take strings, elements or data items. `ListBox(items, selectedIndex, onSelectedIndexChanged)` lists strings;
`ListBox(TextBlock("Apples"), TextBlock("Pears"))` lists elements, reconciled as a panel's children are (by key, else by
position, updated in place, and a keyed one moved with its control and a component's state); and
`ListBox(items, keySelector, itemTemplate)` builds an element per data item, keyed by the selector (items that are
`IReactorKeyed` can leave it out). `SelectedIndex` and `OnSelectedIndexChanged` are the controlled selection, as on the
other selecting controls; `.SelectionMode(...)` takes Avalonia's flags (`Multiple`: Ctrl and Shift add to the
selection, `Multiple | Toggle`: each tap selects or deselects), and `OnSelectionChanged` then receives every selected
index, or every selected item on a typed list. `OnItemClick` is called with the index or item the user taps or presses
Enter or Space on (after the tap selects it; a tap on a focusable control in an item, such as a button or a text box,
is that control's):

<!-- snippet: list-box -->
```csharp
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
```

Every item's element is built and mounted, while Avalonia realises containers only for the items in view: 10,000 element
rows realise 8 containers in a 300-high list and mount in 0.26 to 0.57 s headless (two invocations of
`ListBoxMeasurements`, three runs each), where 10,000 strings take 3 to 44 ms. A keyed reorder moves the item controls, so nothing remounts, but Avalonia takes a moved item out of its
container and puts it back, so a control in it loses keyboard focus. The selection follows its items when they move,
and `OnSelectedIndexChanged` and `OnSelectionChanged` report the new indexes; a removed item's selection is reported as
gone (-1).

> **Coming from Reactor?** `ListView` is `ListBox`, and `ListView<T>` is `ListBox<T>` (the migration renames both, and
> its `viewBuilder` is `itemTemplate`). `ListViewSelectionMode.Extended` is `SelectionMode.Multiple`,
> `Multiple` is `SelectionMode.Multiple | SelectionMode.Toggle`, and `None` has no counterpart: an Avalonia list box
> always lets the user select, so use `Single` and ignore it. A list box has no `Header` (put a `TextBlock` above it),
> `ItemContainerStyle` or `IncrementalLoadingTrigger`. Upstream's `ListView` mounts only the items in view and
> rebuilds them when the items array changes; here every item stays mounted and keeps its state.

Layout has upstream's modifiers too: `.Padding(...)` (one value, horizontal and vertical, or per side:
`.Padding(top: 8)`), `.WithBorder("Gray", 2)`, `.BorderBrush(...)`, `.BorderThickness(...)`, `.CornerRadius(...)`,
`.MinWidth`, `.MaxWidth`, `.MinHeight` and `.MaxHeight`, `.Center()`, `.HorizontalContentAlignment(...)`,
`.VerticalContentAlignment(...)` and `.IsVisible(false)` (`.WithBorder` and `.BorderBrush` also take a theme token, as in
[Theme tokens](theme-tokens.md)). A stack, grid or wrap panel has no padding, border or corners
on Avalonia, so one that is given them is mounted inside a `Border` that has them, which takes its margin, size,
alignment and background too. As Reactor's, they do nothing on a flex container (`.FlexPadding(...)` is its padding).

<!-- snippet: layout-modifiers -->
```csharp
public static Element Card(string title, string body, bool selected) =>
    VStack(4,
        TextBlock(title).SemiBold(),
        TextBlock(body).TextWrapping())
        .Padding(12)
        .WithBorder(selected ? "DodgerBlue" : "LightGray", 2)
        .CornerRadius(8)
        .MaxWidth(320);
```

> **Coming from Reactor?** A panel that gains or loses its padding, border width or corner radius between renders is
> mounted again, and the state of the components inside it is lost: give it `.Padding(0)` rather than none. A border
> brush alone draws nothing without a width, so it doesn't need the `Border`, and a brush that comes and goes (a
> selection highlight) keeps the panel. The BiDi-aware
> `MarginInlineStart` family isn't here.

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
    TodoAction.Reverse => s with { Items = Enumerable.Reverse(s.Items).ToArray() },
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

An app is a root component in a window. With the `Isotope.UI.Avalonia.Desktop` package, one line in `Program.cs`
starts it, as in Reactor (`using Isotope.UI;`; top-level statements are fine):

<!-- snippet: run-app -->
```csharp
ReactorApp.Run<CounterApp>("Counter", width: 400, height: 300);
```

Here the root is a class component:

<!-- snippet: counter-app -->
```csharp
public sealed class CounterApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            TextBlock($"Clicked {count} times"),
            Button("Click", () => setCount(count + 1)));
    }
}
```

`ReactorApp.Run("Counter", Counter, width: 400, height: 300)` does the same for a function component. Both build the
default app builder, `ReactorApp.CreateDefaultAppBuilder()` (Avalonia's platform detection and the Fluent theme), open
one window and run until it closes, then return the exit code. Their other parameters are Reactor's: `fullScreen`,
`icon` and `configure`, which gets the window's `ReactorHostControl` before the root mounts. The icon is a factory,
`icon: () => new WindowIcon("app.ico")`, called when the window opens, because Avalonia can only build a `WindowIcon`
once the platform is set up. On Windows, a `Main` on an MTA thread (top-level statements, an async `Main`) is moved
to an STA thread, as Reactor does, because the clipboard and drag-and-drop need one.

**Desktop only for now:** Windows, macOS and Linux, with Avalonia's classic desktop lifetime. The browser and mobile
lifetimes aren't hosted yet.

An app that sets Avalonia up itself (its own `Application`, resources or theme) passes its `AppBuilder` instead:
`ReactorApp.Run(builder, title, root, args)`, or `ReactorApp.Run<TRoot>(builder, title, args)`, with the same options.
These need only the `Isotope.UI.Avalonia` library. The sample app ([`samples/Isotope.UI.Samples`](../../samples/Isotope.UI.Samples/Program.cs))
does this with the counter and the todo app. Run it with `dotnet run` in that folder.

Coming from Reactor: `Run` returns the exit code (Reactor's returns nothing, so its calls compile as they are), the
default title is "Isotope App", `configure` gets a `ReactorHostControl` (Reactor's `ReactorHost`), and an icon is
`() => new WindowIcon(path)` (Reactor's `WindowIcon.FromPath(path)`). The `WindowSpec` overloads and the multi-window
`Run(Action<ReactorAppContext>)` aren't here yet.

The window's content is a `ReactorHostControl` (`using Isotope.UI.Hosting;`), which can also hold a root component
anywhere in an existing Avalonia layout. It renders on Avalonia's dispatcher: however many state changes arrive before
a pass runs, the root renders once. Leaving the visual tree doesn't unmount it; `Dispose` does.

When a render throws, the host's `RenderErrorHandler` is told (or `ReactorApp.DefaultRenderErrorHandler`, for every
host that has none), and what it returns is shown instead. With no handler, or one that returns `null`, the exception
is shown.

- **A child component that throws** while it renders, or in an effect that render runs, is replaced in its own place.
  The rest of the window keeps its controls and state. The handler gets `IsHostLevel` false, with the source
  `ComponentRender` or `Effects`, and what it returns fills that component's place. Each later render of the
  component tries it again: a change of its own state, or a render of its parent that renders it again (a function
  component always is; a memo component when its dependencies change, a class component when its props do).
- **The root's render, its effects, or a control that fails to mount or update** replaces the host's whole content
  (`IsHostLevel` true). The next state change renders again.
- **A handler that throws, or whose element throws,** gets a neutral "Something went wrong." in that place instead. A
  handler that calls `error.Propagate()` gets nothing shown, and the exception is rethrown out of the dispatcher.
- **An [error boundary](error-boundaries.md)** shows a fallback of your own for its part of the tree. The handler isn't
  told about an error a boundary caught.

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
