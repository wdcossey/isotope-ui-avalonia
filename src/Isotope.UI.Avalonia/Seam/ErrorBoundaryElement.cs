namespace Isotope.UI.Core;

/// <summary>
/// Catches errors thrown while its child subtree renders and shows a fallback instead (upstream's record, #25). Like
/// React's error boundary, it catches errors from renders and the effects they flush, not from event handlers. Every
/// re-render of the boundary retries the child, which is how it recovers.
/// </summary>
/// <param name="Child">The subtree to render.</param>
/// <param name="Fallback">Builds what to show in the child's place from the exception the child threw.</param>
public record ErrorBoundaryElement(Element Child, Func<Exception, Element> Fallback) : Element;
