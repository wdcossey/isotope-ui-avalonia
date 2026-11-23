// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Controls/Validation/ValidationMessage.cs, last changed upstream in ca65995135d5; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
namespace Isotope.UI.Controls.Validation;

/// <summary>
/// Severity level for a validation message.
/// </summary>
public enum Severity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// An immutable validation message associated with a specific field.
/// </summary>
/// <param name="Field">The field name this message applies to.</param>
/// <param name="Text">Human-readable message text.</param>
/// <param name="Severity">Message severity (default: Error).</param>
/// <param name="Code">Optional machine-readable code for i18n/dedup.</param>
public sealed record ValidationMessage(
    string Field,
    string Text,
    Severity Severity = Severity.Error,
    string? Code = null);
