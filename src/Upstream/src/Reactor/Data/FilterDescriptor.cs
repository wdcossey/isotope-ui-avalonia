// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Data/FilterDescriptor.cs, last changed upstream in ca65995135d5. Do not edit: change the tools/sync rules.
namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// Describes a filter operation on a field.
/// </summary>
public record FilterDescriptor(
    string Field,
    FilterOperator Operator,
    object? Value = null,
    object? ValueTo = null);

/// <summary>
/// Filter operators for data queries.
/// </summary>
public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    In,
    IsNull,
    IsNotNull,
}
