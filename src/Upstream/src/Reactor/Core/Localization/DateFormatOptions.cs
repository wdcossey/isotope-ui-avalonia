// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Localization/DateFormatOptions.cs, last changed upstream in ca65995135d5. Do not edit: change the tools/sync rules.
namespace Microsoft.UI.Reactor.Localization;

/// <summary>
/// Options for IntlAccessor.FormatDate(). Maps to .NET DateTimeFormatInfo styles.
/// </summary>
public enum DateStyle
{
    Default,
    Short,
    Long,
    Full
}

public sealed class DateFormatOptions
{
    public DateStyle Style { get; init; }
}
