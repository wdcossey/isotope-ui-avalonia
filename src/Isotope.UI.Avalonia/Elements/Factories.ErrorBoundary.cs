using Isotope.UI.Core;

namespace Isotope.UI;

// Error boundaries (#25), as upstream's Dsl.cs has them.
public static partial class Factories
{
    // ── Error boundaries ────────────────────────────────────────────

    /// <summary>
    /// Wraps a child subtree in an error boundary. If anything in the subtree throws while it renders (or in an effect
    /// that render flushes), <paramref name="fallback"/> is called with the exception and its element is shown in the
    /// child's place; the rest of the tree is untouched. Every re-render of the boundary retries the child.
    /// </summary>
    public static ErrorBoundaryElement ErrorBoundary(Element child, Func<Exception, Element> fallback) => new(child, fallback);

    /// <summary>Wraps a child subtree in an error boundary with a fixed fallback element.</summary>
    public static ErrorBoundaryElement ErrorBoundary(Element child, Element fallback) => new(child, _ => fallback);
}
