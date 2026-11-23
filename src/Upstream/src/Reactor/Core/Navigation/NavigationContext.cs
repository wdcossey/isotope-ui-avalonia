// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Navigation/NavigationContext.cs, last changed upstream in ca65995135d5; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
using Isotope.UI.Core;

namespace Isotope.UI.Navigation;

/// <summary>
/// Provides a static <see cref="Context{T}"/> instance per TRoute type
/// for sharing a <see cref="NavigationHandle{TRoute}"/> through the element tree.
/// Uses the static-generic-class pattern so each TRoute gets its own singleton context
/// without per-render allocation.
/// </summary>
internal static class NavigationContext<TRoute> where TRoute : notnull
{
    internal static readonly Context<NavigationHandle<TRoute>?> Instance =
        new(defaultValue: null, name: $"NavigationContext<{typeof(TRoute).Name}>");
}
