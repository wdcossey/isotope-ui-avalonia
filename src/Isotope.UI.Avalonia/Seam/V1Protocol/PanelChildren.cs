namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// The children a generated panel strategy reconciles. The generator emits <c>PanelChildren.Of(e.Children)</c>, so a
/// <see cref="GroupElement"/> in a library's panel record is flattened into its siblings as in Isotope's own panels
/// (#79): the record needn't flatten its children itself.
/// </summary>
public static class PanelChildren
{
    /// <summary>
    /// <paramref name="children"/> with any group flattened into them, each child's slot registered so a group that
    /// grows or shrinks doesn't shift its siblings onto other controls. A list with no group comes back as itself.
    /// </summary>
    public static IReadOnlyList<Element> Of(IReadOnlyList<Element> children)
    {
        if (children is Element[] array) return Reconciler.Flatten(array);
        for (int i = 0; i < children.Count; i++)
            if (children[i] is GroupElement) return Reconciler.Flatten([.. children]);
        return children;
    }
}
