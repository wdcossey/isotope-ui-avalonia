// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Localization/RtlHelper.cs, last changed upstream in ca65995135d5; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
namespace Isotope.UI.Localization;

/// <summary>
/// Determines text direction for a locale based on CLDR data.
/// </summary>
public static class RtlHelper
{
    private static readonly HashSet<string> RtlLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "he", "fa", "ur", "ps", "sd", "ug", "yi", "ku", "dv", "ha", "ks", "syr"
    };

    /// <summary>
    /// Returns true if the given locale uses right-to-left script.
    /// Handles both bare language codes ("ar") and full BCP 47 tags ("ar-SA").
    /// </summary>
    public static bool IsRtlLocale(string locale)
    {
        var lang = locale.Split('-')[0];
        return RtlLanguages.Contains(lang);
    }
}
