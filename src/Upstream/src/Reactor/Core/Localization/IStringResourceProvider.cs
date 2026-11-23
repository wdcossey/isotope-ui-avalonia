// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Localization/IStringResourceProvider.cs, last changed upstream in ca65995135d5; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
namespace Isotope.UI.Localization;

/// <summary>
/// Abstracts string resource loading so that the localization system
/// can be tested without MRT/ResourceLoader.
/// </summary>
public interface IStringResourceProvider
{
    /// <summary>
    /// Gets a string resource for the given namespace and key in the specified locale.
    /// Returns null if the key is not found for that locale.
    /// </summary>
    string? GetString(string locale, string ns, string key);
}
