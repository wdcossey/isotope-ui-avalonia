// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Localization/NumberFormatOptions.cs, last changed upstream in ca65995135d5. Do not edit: change the tools/sync rules.
namespace Microsoft.UI.Reactor.Localization;

/// <summary>
/// Options for IntlAccessor.FormatNumber(). Mirrors ICU/Intl.NumberFormat options.
/// </summary>
public enum NumberStyle
{
    Default,
    Currency,
    Percent
}

public sealed class NumberFormatOptions
{
    public NumberStyle Style { get; init; }
    public int? MinimumFractionDigits { get; init; }
    public int? MaximumFractionDigits { get; init; }
}
