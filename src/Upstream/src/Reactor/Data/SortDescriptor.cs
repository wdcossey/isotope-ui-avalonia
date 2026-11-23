// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Data/SortDescriptor.cs, last changed upstream in ca65995135d5. Do not edit: change the tools/sync rules.
namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// Describes a sort operation on a field.
/// </summary>
public record SortDescriptor(string Field, SortDirection Direction = SortDirection.Ascending);

/// <summary>
/// Sort direction for data queries.
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending,
}
