// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Data/GridAttributes.cs, last changed upstream in ca65995135d5; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
namespace Isotope.UI.Data;

/// <summary>
/// Specifies the preferred column width for a property when displayed in a DataGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnWidthAttribute(double width) : Attribute
{
    public double Width { get; } = width;
    public double MinWidth { get; set; }
    public double MaxWidth { get; set; }
}

/// <summary>
/// Pins a column to the left or right edge of a DataGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnPinAttribute(PinPosition position) : Attribute
{
    public PinPosition Position { get; } = position;
}

/// <summary>
/// Marks a column as not sortable in a DataGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotSortableAttribute : Attribute { }

/// <summary>
/// Marks a column as not filterable in a DataGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotFilterableAttribute : Attribute { }
