using Avalonia.Layout;
using Isotope.UI.Core;
using Isotope.UI.Layout;

namespace Isotope.UI;

// Our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs, for the controls the library has today. The
// factories keep upstream's names and parameter order, and return the same records; their parameter names follow the
// records' Avalonia member names (#31; tools/naming/renames.json maps Reactor's), and their value types are Avalonia's.
// Add a factory here when its control arrives.

/// <summary>
/// The element factories: <c>using static Isotope.UI.Factories;</c> lets a component read as a description of its UI.
/// <code>
/// VStack(
///     TextBlock($"Clicked {count} times"),
///     Button("Click", () => setCount(count + 1)))
/// </code>
/// Factories return element records (a description of the UI), never Avalonia controls.
/// </summary>
public static partial class Factories
{
    // ── Text ────────────────────────────────────────────────────────

    /// <summary>Text, as an Avalonia <see cref="Avalonia.Controls.TextBlock"/>.</summary>
    public static TextBlockElement TextBlock(string text) => new(text);

    // ── Buttons ─────────────────────────────────────────────────────

    /// <summary>A push button with a text content, as an Avalonia <see cref="Avalonia.Controls.Button"/>.</summary>
    public static ButtonElement Button(string content, Action? onClick = null) => new(content, onClick);

    // ── Input ───────────────────────────────────────────────────────

    /// <summary>
    /// A text box. <paramref name="text"/> defaults to <see cref="Optional{T}.Unset"/>: omit it to let the control own
    /// its text, or pass the state's value and <paramref name="onTextChanged"/> to control it.
    /// </summary>
    public static TextBoxElement TextBox(Optional<string> text = default, Action<string>? onTextChanged = null, string? placeholderText = null)
        => new(text, onTextChanged, placeholderText);

    /// <summary>
    /// A slider from <paramref name="minimum"/> to <paramref name="maximum"/>. <paramref name="value"/> defaults to
    /// <see cref="Optional{T}.Unset"/>: omit it to let the control own its value.
    /// </summary>
    public static SliderElement Slider(Optional<double> value = default, double minimum = 0, double maximum = 100, Action<double>? onValueChanged = null)
        => new(value, minimum, maximum, onValueChanged);

    // ── Layout ──────────────────────────────────────────────────────

    /// <summary>Children stacked top to bottom (a vertical <see cref="Avalonia.Controls.StackPanel"/>; spacing 8).</summary>
    public static StackElement VStack(params Element?[] children) => new(Orientation.Vertical, FilterChildren(children));

    /// <summary>Children stacked top to bottom, <paramref name="spacing"/> DIPs apart.</summary>
    public static StackElement VStack(double spacing, params Element?[] children)
        => new(Orientation.Vertical, FilterChildren(children)) { Spacing = spacing };

    /// <summary>Children stacked left to right (a horizontal <see cref="Avalonia.Controls.StackPanel"/>; spacing 8).</summary>
    public static StackElement HStack(params Element?[] children) => new(Orientation.Horizontal, FilterChildren(children));

    /// <summary>Children stacked left to right, <paramref name="spacing"/> DIPs apart.</summary>
    public static StackElement HStack(double spacing, params Element?[] children)
        => new(Orientation.Horizontal, FilterChildren(children)) { Spacing = spacing };

    /// <summary>A flexbox container (direction <see cref="FlexDirection.Row"/>); set each child's flex data with <c>.Flex(...)</c>.</summary>
    public static FlexElement Flex(params Element?[] children) => new(FilterChildren(children));

    /// <summary>A flexbox container with an explicit direction.</summary>
    public static FlexElement Flex(FlexDirection direction, params Element?[] children)
        => new(FilterChildren(children)) { Direction = direction };

    /// <summary>A flexbox container laid out in a row.</summary>
    public static FlexElement FlexRow(params Element?[] children)
        => new(FilterChildren(children)) { Direction = FlexDirection.Row };

    /// <summary>A flexbox container laid out in a column.</summary>
    public static FlexElement FlexColumn(params Element?[] children)
        => new(FilterChildren(children)) { Direction = FlexDirection.Column };

    // ── Components ──────────────────────────────────────────────────

    /// <summary>A function component: <paramref name="render"/> runs on every render of its parent, with its own hooks.
    /// Isotope's name for upstream's <see cref="RenderEachTime"/>.</summary>
    public static FuncElement Component(Func<RenderContext, Element> render) => new(render);

    /// <summary>A <see cref="Core.Component"/> class as a child element: <c>Component&lt;MyWidget&gt;()</c>.</summary>
    public static ComponentElement Component<T>() where T : Component, new() =>
        new(typeof(T)) { _factory = () => new T() };

    /// <summary>A <see cref="Component{TProps}"/> class with typed props: <c>Component&lt;MyWidget, string&gt;("param")</c>.</summary>
    public static ComponentElement<TProps> Component<T, TProps>(TProps props) where T : Component<TProps>, new() =>
        new(typeof(T), props) { _factory = () => new T() };

    /// <summary>
    /// A memoized function component: it re-renders from its parent only when a dependency changes. No dependencies:
    /// it renders once, then only on its own state changes.
    /// </summary>
    public static MemoElement Memo(Func<RenderContext, Element> render, params object?[] dependencies)
        => new(render, dependencies.Length == 0 ? null : dependencies);

    /// <summary>A function component that re-renders on every render of its parent (upstream's name).</summary>
    public static FuncElement RenderEachTime(Func<RenderContext, Element> render) => new(render);

    // ── Structure ───────────────────────────────────────────────────

    /// <summary>Groups elements without a layout container (like React's Fragment); flattened into the parent.</summary>
    public static Element Group(params Element?[] children) => new GroupElement(FilterChildren(children));

    /// <summary>Renders nothing.</summary>
    public static Element Empty() => EmptyElement.Instance;

    /// <summary>
    /// As upstream's: drops <see langword="null"/> and <see cref="EmptyElement"/> children and flattens one level of
    /// <see cref="GroupElement"/>, so <c>cond ? X : null</c> works as a child. When it drops or flattens anything it
    /// records in <see cref="ChildSlots"/> which argument each kept child came from, so a conditional child keeps its
    /// slot among its unkeyed siblings across renders. With nothing to drop, the caller's array is returned as is (the
    /// container's children compare by reference, so a caller must not mutate an array after passing it).
    /// </summary>
    private static Element[] FilterChildren(Element?[] children)
    {
        bool needsExpansion = false;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is null or GroupElement or EmptyElement)
            {
                needsExpansion = true;
                break;
            }
        }
        if (!needsExpansion) return (Element[])(object)children;

        int count = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                foreach (var gc in group.Children)
                    if (gc is not null and not EmptyElement) count++;
            }
            else if (children[i] is not null and not EmptyElement)
            {
                count++;
            }
        }

        var result = new Element[count];
        long[]? slots = count > 0 ? new long[count] : null;
        // A map equal to the implicit index ids (only a trailing argument was dropped) is not registered.
        bool identity = true;
        int idx = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                var groupSlots = slots is null ? null : ChildSlots.Get(group.Children);
                for (int j = 0; j < group.Children.Length; j++)
                {
                    var gc = group.Children[j];
                    if (gc is not null and not EmptyElement)
                    {
                        if (slots is not null)
                        {
                            slots[idx] = groupSlots is null ? ChildSlots.ForGroupChild(i, j) : ChildSlots.Nest(i, groupSlots[j]);
                            identity = false;
                        }
                        result[idx++] = gc;
                    }
                }
            }
            else if (children[i] is not null and not EmptyElement)
            {
                if (slots is not null)
                {
                    slots[idx] = ChildSlots.ForIndex(i);
                    identity &= i == idx;
                }
                result[idx++] = children[i]!;
            }
        }
        if (slots is not null && !identity) ChildSlots.Set(result, slots);
        return result;
    }
}
