using Isotope.UI.Core;

namespace Isotope.UI;

// The conditional and list helpers (#43), as part of our own version (tier 3) of upstream's
// src/Reactor/Elements/Dsl.cs ("Conditional helpers"): upstream's names, signatures and behaviour. They are composition
// sugar: no hooks, no reconciler boundary. Reactor-only helpers, with no Avalonia counterpart, so Reactor's names.

public static partial class Factories
{
    /// <summary>
    /// <paramref name="then"/>'s element when <paramref name="condition"/> is true, otherwise nothing:
    /// <c>When(items.Any(), () =&gt; TextBlock("Has items"))</c>. <paramref name="then"/> runs only when it is needed.
    /// </summary>
    public static Element When(bool condition, Func<Element> then) =>
        condition ? then() : EmptyElement.Instance;

    /// <summary>
    /// If/else as an expression: <c>If(loggedIn, () =&gt; TextBlock("Welcome"), () =&gt; Button("Log in", logIn))</c>.
    /// With no <paramref name="otherwise"/>, a false condition renders nothing.
    /// </summary>
    public static Element If(bool condition, Func<Element> then, Func<Element>? otherwise = null) =>
        condition ? then() : (otherwise?.Invoke() ?? EmptyElement.Instance);

    /// <summary>
    /// A block of statements inside an element tree: runs <paramref name="render"/> and returns its element, or nothing
    /// when it returns null. It owns no hooks (use a component for those) and lets exceptions through to the nearest
    /// error boundary.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="render"/> is null.</exception>
    public static Element Expr(Func<Element?> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        return render() ?? EmptyElement.Instance;
    }

    /// <summary>
    /// The items mapped to elements, as a fragment (a <see cref="GroupElement"/>) flattened into its parent:
    /// <c>VStack(ForEach(todos, t =&gt; TextBlock(t.Title)))</c>. When <typeparamref name="T"/> implements
    /// <see cref="IReactorKeyed"/>, each element without a key gets the item's, so rows are matched by identity.
    /// </summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render)
    {
        var buffer = new List<Element>(items.TryGetNonEnumeratedCount(out var count) ? count : 4);
        foreach (var item in items) buffer.Add(AutoKey(render(item), item));
        return new GroupElement(buffer.ToArray());
    }

    /// <summary>The items and their indexes mapped to elements, keyed as <see cref="ForEach{T}(IEnumerable{T}, Func{T, Element})"/>.</summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, int, Element> render)
    {
        var buffer = new List<Element>(items.TryGetNonEnumeratedCount(out var count) ? count : 4);
        var index = 0;
        foreach (var item in items) buffer.Add(AutoKey(render(item, index++), item));
        return new GroupElement(buffer.ToArray());
    }

    // As upstream's: an element the render already keyed keeps its key; a null element passes through to the
    // container's filter, which drops it.
    private static Element AutoKey<T>(Element element, T item)
    {
        if (element is null || element.Key is not null) return element!;
        return item is IReactorKeyed keyed ? element with { Key = keyed.Key } : element;
    }
}
