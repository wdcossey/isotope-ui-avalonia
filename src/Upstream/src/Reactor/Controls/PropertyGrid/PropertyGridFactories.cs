// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Controls/PropertyGrid/PropertyGridFactories.cs, last changed upstream in 43bec45c7c9f; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
using Isotope.UI.Core;
using Isotope.UI.Controls;

namespace Isotope.UI;

public static partial class Factories
{
    /// <summary>
    /// Creates a PropertyGrid element for editing the target object's properties.
    /// Keyed by target type so switching between different target types creates
    /// a fresh component (avoiding broken reconciliation of different structures).
    /// </summary>
    public static ComponentElement<PropertyGridElement> PropertyGrid(
        object target,
        TypeRegistry registry,
        Action<object>? onRootChanged = null)
    {
        var props = new PropertyGridElement
        {
            Target = target,
            Registry = registry,
            OnRootChanged = onRootChanged,
        };

        return ComponentWithKey<PropertyGridComponent, PropertyGridElement>(props,
            $"pg-{target.GetType().FullName}");
    }
}
