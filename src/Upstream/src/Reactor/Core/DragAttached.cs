// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/DragAttached.cs, last changed upstream in 7d1929ea8d2e; transforms: winui-usings, namespace-isotope. Do not edit: change the tools/sync rules.


namespace Isotope.UI.Core;

/// <summary>Attached element data for the window drag-from-background opt-out modifier.</summary>
public sealed record DragAttached(bool IsEnabled);

internal static class DragAttachedProperties
{
    public static readonly DependencyProperty IsDragEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDragEnabled",
            typeof(bool?),
            typeof(DragAttachedProperties),
            new PropertyMetadata(null));

    public static void SetIsDragEnabled(DependencyObject element, bool? value)
        => element.SetValue(IsDragEnabledProperty, value);

    public static bool? GetIsDragEnabled(DependencyObject element)
        => (bool?)element.GetValue(IsDragEnabledProperty);
}
