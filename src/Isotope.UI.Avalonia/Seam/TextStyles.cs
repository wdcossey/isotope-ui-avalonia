using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Isotope.UI.Core.Diagnostics;
using Isotope.UI.Core.V1Protocol.Descriptor;

namespace Isotope.UI.Core;

// Named text styles (tier 3, #44): upstream's ApplyStyle on a text block, which its type-ramp factories (Title, Body…)
// use, and the heading level its Heading and SubHeading set. WinUI's keyed Style is Avalonia's keyed ControlTheme, set
// through the control's Theme.

public partial record TextBlockElement
{
    /// <summary>
    /// The key of the text block's <see cref="ControlTheme"/> (upstream's <c>ApplyStyle</c> name, a WinUI style key such
    /// as <c>TitleTextBlockStyle</c>): the theme of that key in the resources the control sees, else Isotope's type ramp.
    /// </summary>
    internal string? StyleKey { get; init; }

    /// <summary>Avalonia's <c>AutomationProperties.HeadingLevel</c> (upstream's heading-level modifier, set by
    /// <c>Heading</c> and <c>SubHeading</c>); <see langword="null"/> leaves the control's own.</summary>
    internal int? HeadingLevel { get; init; }

    // The skip arm's comparison of these members (Element.ShallowEquals, through TextStylingEquals).
    internal static bool TextStyleEquals(TextBlockElement x, TextBlockElement y)
        => string.Equals(x.StyleKey, y.StyleKey, StringComparison.Ordinal) && x.HeadingLevel == y.HeadingLevel;

    // The descriptor entries for these members (called from Customize): the theme follows the key, written when it
    // changes (upstream's applies it at mount only), and the heading level is cleared when it goes.
    private static ControlDescriptor<TextBlockElement, TextBlock> CustomizeTextStyles(ControlDescriptor<TextBlockElement, TextBlock> d)
        => d.OneWay<string?>(
                get: static e => e.StyleKey,
                set: static (c, key) => TextStyles.Apply(c, key),
                comparer: StringComparer.Ordinal)
            .OneWay<int>(
                get: static e => e.HeadingLevel is { } level ? level : Optional<int>.Unset,
                set: static (c, v) => AutomationProperties.SetHeadingLevel(c, v),
                dp:  AutomationProperties.HeadingLevelProperty);
}

/// <summary>
/// The type ramp on Avalonia: a text block's style key resolves to the <see cref="ControlTheme"/> of that key in the
/// resources it sees (its own, its ancestors', the window's, the application's and its styles'), so an app's theme
/// wins; else to Isotope's theme for that WinUI TextBlock style, with WinUI's values (Avalonia's Fluent theme defines
/// none of them). The key is watched: a change to the resources or the theme variant applies with no re-render.
/// </summary>
internal static class TextStyles
{
    // WinUI's TextBlock styles, from Microsoft.UI.Xaml's generic.xaml (Windows App SDK 1.8, MIT): every one is based
    // on BaseTextBlockStyle (14, SemiBold, wrapping, character-ellipsis trimming) and sets its size and weight. WinUI's
    // LineStackingStrategy, TextLineBounds and OpticalMarginAlignment have no Avalonia counterpart. WinUI's font family
    // is XamlAutoFontFamily (the system's UI font); these themes set none, so a text block keeps the one it inherits.
    private static readonly (string Key, double Size, FontWeight Weight)[] s_ramp =
    [
        ("BaseTextBlockStyle", 14, FontWeight.SemiBold),
        ("HeaderTextBlockStyle", 46, FontWeight.Light),
        ("SubheaderTextBlockStyle", 34, FontWeight.Light),
        ("TitleTextBlockStyle", 28, FontWeight.SemiBold),
        ("SubtitleTextBlockStyle", 20, FontWeight.SemiBold),
        ("BodyTextBlockStyle", 14, FontWeight.Normal),
        ("CaptionTextBlockStyle", 12, FontWeight.Normal),
        ("BodyStrongTextBlockStyle", 14, FontWeight.SemiBold),
        ("BodyLargeTextBlockStyle", 18, FontWeight.Normal),
        ("BodyLargeStrongTextBlockStyle", 18, FontWeight.SemiBold),
        ("TitleLargeTextBlockStyle", 40, FontWeight.SemiBold),
        ("DisplayTextBlockStyle", 68, FontWeight.SemiBold),
    ];

    private static readonly Lazy<Dictionary<string, ControlTheme>> s_builtin = new(Build);

    private static Dictionary<string, ControlTheme> Build()
    {
        var themes = new Dictionary<string, ControlTheme>(StringComparer.Ordinal);
        foreach (var (key, size, weight) in s_ramp)
        {
            var theme = new ControlTheme(typeof(TextBlock));
            theme.Setters.Add(new Setter(TextBlock.FontSizeProperty, size));
            theme.Setters.Add(new Setter(TextBlock.FontWeightProperty, weight));
            theme.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            theme.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            themes[key] = theme;
        }
        return themes;
    }

    /// <summary>Isotope's theme for a WinUI TextBlock style key, or null.</summary>
    internal static ControlTheme? Builtin(string key) => s_builtin.Value.GetValueOrDefault(key);

    /// <summary>The WinUI TextBlock style keys Isotope has a theme for.</summary>
    internal static IEnumerable<string> BuiltinKeys => s_ramp.Select(r => r.Key);

    private static readonly ConditionalWeakTable<TextBlock, Slot> s_slots = new();

    /// <summary>
    /// Gives <paramref name="control"/> the theme of <paramref name="key"/>, watched; a null key removes the one a
    /// previous key gave. The same key as last time changes nothing.
    /// </summary>
    public static void Apply(TextBlock control, string? key)
    {
        if (s_slots.TryGetValue(control, out var slot))
        {
            if (string.Equals(slot.Key, key, StringComparison.Ordinal)) return;
            s_slots.Remove(control);
            slot.Dispose();
        }
        if (key is null) return;
        s_slots.Add(control, new Slot(control, key));
    }

    /// <summary>Tests only: the style key a text block's theme follows now.</summary>
    internal static string? KeyOf(TextBlock control) => s_slots.TryGetValue(control, out var slot) ? slot.Key : null;

    // One text block's watched key: the theme property bound to the key's resource, mapped to a theme that fits.
    private sealed class Slot : IDisposable
    {
        private readonly TextBlock _control;
        private IDisposable? _binding;

        public Slot(TextBlock control, string key)
        {
            _control = control;
            Key = key;
            _binding = control.Bind(StyledElement.ThemeProperty, control.GetResourceObservable(key, Resolve));
        }

        public string Key { get; }

        // The resource of the key when it is a theme for a text block; else the built-in one, else none. A resource of
        // another kind is warned about. A missing one is warned about only once the control is in a logical tree: the
        // descriptor binds before the control has a parent, when only its own resources are in reach, and the
        // observable resolves again when it is attached.
        private object? Resolve(object? resource)
        {
            if (resource is ControlTheme { TargetType: { } target } theme && target.IsInstanceOfType(_control)) return theme;
            var found = resource is not null && resource != AvaloniaProperty.UnsetValue;
            if (found) Warn(Key, "is not a ControlTheme for a TextBlock; it is ignored.");
            if (Builtin(Key) is { } builtin) return builtin;
            if (!found && ((ILogical)_control).IsAttachedToLogicalTree)
                Warn(Key, "did not resolve to a ControlTheme for a TextBlock in the resources the control sees, and is not " +
                    "one of WinUI's TextBlock styles; the text block keeps its default appearance.");
            return AvaloniaProperty.UnsetValue;
        }

        public void Dispose()
        {
            var binding = _binding;
            _binding = null;
            binding?.Dispose();
            _control.ClearValue(StyledElement.ThemeProperty);
        }
    }

    // As upstream's ApplyStyle: an unknown key, or a resource of the key that isn't a theme, warns once per key (on the
    // Isotope-UI event source, and Debug output in DEBUG builds), up to a bound past which every miss warns. Overlong
    // keys are cut and never stored, so they warn every time (as upstream's).
    private const int MaxKeyChars = 256;
    private const int WarnedCap = 256;
    private static readonly ConcurrentDictionary<string, byte> s_warned = new(StringComparer.Ordinal);

    private static void Warn(string key, string problem)
    {
        if (s_warned.ContainsKey(key) || !DiagnosticLog.IsWarningEnabled) return;
        var bounded = key.Length <= MaxKeyChars;
        if (bounded && s_warned.Count < WarnedCap && !s_warned.TryAdd(key, 0)) return;
        var safeKey = bounded ? key : string.Concat(key.AsSpan(0, MaxKeyChars), "…");
        DiagnosticLog.Warning(LogCategory.Theme, "ApplyStyle", $"Style '{safeKey}' {problem}");
    }
}
