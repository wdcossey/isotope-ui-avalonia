using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Isotope.UI.Migration;

/// <summary>
/// The migration's diagnostics. The ones with a fix carry their edit in the diagnostic's properties (see
/// <see cref="Props"/>), so the one code fix applies any of them, and a Fix All applies a whole document at once.
/// </summary>
internal static class Rules
{
    const string Category = "Migration";

    static DiagnosticDescriptor Rule(string id, string title, string message, string description) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: description);

    public static readonly DiagnosticDescriptor ReactorNamespace = Rule("ISOMIG001", "Reactor namespace",
        "'{0}' is '{1}' in Isotope",
        "Reactor's Microsoft.UI.Reactor namespaces are Isotope.UI in Isotope. Fix: rename the namespace.");

    public static readonly DiagnosticDescriptor NoIsotopeNamespace = Rule("ISOMIG002", "Reactor namespace with no Isotope counterpart",
        "'{0}' has no Isotope counterpart ('{1}' doesn't exist)",
        "A Reactor namespace or type that Isotope doesn't have (Charting, Docking, Advanced…). No fix.");

    public static readonly DiagnosticDescriptor WinUIUsing = Rule("ISOMIG003", "WinUI namespace",
        "'{0}' is a WinUI namespace: {1}",
        "WinUI namespaces don't exist on Avalonia. Fix: remove the using and add the Avalonia namespaces of the WinUI types the file uses.");

    public static readonly DiagnosticDescriptor WinUIType = Rule("ISOMIG004", "WinUI type",
        "'{0}' is '{1}' on Avalonia",
        "A WinUI type or member with an Avalonia counterpart under another name or namespace. Fix: use Avalonia's.");

    public static readonly DiagnosticDescriptor NoAvaloniaType = Rule("ISOMIG005", "WinUI type with no Avalonia counterpart",
        "'{0}' has no Avalonia counterpart{1}",
        "A WinUI type or member that Avalonia doesn't have. No fix; the message says what to use where there is something.");

    public static readonly DiagnosticDescriptor MemberRenamed = Rule("ISOMIG006", "Reactor member renamed",
        "'{0}.{1}' is '{2}' in Isotope",
        "An element member with Avalonia's name in Isotope (tools/naming/renames.json). Fix: rename it.");

    public static readonly DiagnosticDescriptor MemberShape = Rule("ISOMIG007", "Reactor member changed shape",
        "'{0}.{1}' changed shape in Isotope: {2}",
        "An element member merged, split or retyped in Isotope (tools/naming/renames.json, kind shape) where the value doesn't fit the new member. No fix.");

    public static readonly DiagnosticDescriptor ParameterRenamed = Rule("ISOMIG008", "Factory parameter renamed",
        "Parameter '{1}' of '{0}' is '{2}' in Isotope",
        "A factory parameter with Avalonia's name in Isotope (tools/naming/renames.json). Fix: rename the named argument.");

    public static readonly DiagnosticDescriptor NameRenamed = Rule("ISOMIG009", "Reactor name renamed",
        "{0} '{1}' is '{2}' in Isotope",
        "A type, factory or modifier renamed in Isotope (tools/naming/renames.json, status applied). Fix: rename it.");

    public static readonly DiagnosticDescriptor NameRemoved = Rule("ISOMIG010", "Reactor name removed",
        "{0} '{1}' is gone in Isotope: {2}",
        "A type, factory or modifier Isotope removed (tools/naming/renames.json, an applied entry with no Avalonia name). No fix.");

    public static readonly DiagnosticDescriptor WinUIOnlyControl = Rule("ISOMIG011", "WinUI-only control",
        "'{0}' is a WinUI control with no Isotope counterpart: {1}",
        "A Reactor control whose WinUI control has no counterpart in Avalonia's core. No fix.");

    public static readonly DiagnosticDescriptor ControlNotPorted = Rule("ISOMIG012", "Reactor control not in Isotope yet",
        "'{0}' has no Isotope factory: {1}",
        "A Reactor factory Isotope doesn't have yet, although Avalonia has a counterpart. No fix.");

    public static readonly DiagnosticDescriptor NoIsotopeMember = Rule("ISOMIG013", "Reactor member with no Isotope counterpart",
        "'{0}' has no member '{1}' in Isotope",
        "A member or modifier of an Isotope type that Reactor has and Isotope doesn't (most are WinUI-only). No fix.");

    public static readonly DiagnosticDescriptor WindowIcon = Rule("ISOMIG014", "Window icon",
        "Isotope's window icon is a factory, called when the window opens: '{0}' is '{1}'",
        "Reactor's icon: WindowIcon.FromPath(path) is icon: () => new Avalonia.Controls.WindowIcon(path) in Isotope. Fix: rewrite it.");

    public static readonly DiagnosticDescriptor Startup = Rule("ISOMIG015", "WinUI startup",
        "{0}: an Isotope app starts with ReactorApp.Run<TRoot>(title, width, height) (Isotope.UI.Avalonia.Desktop){1}",
        "WinUI application startup, or a Reactor startup Isotope doesn't have (WindowSpec, several windows). No fix.");

    public static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
        ReactorNamespace, NoIsotopeNamespace, WinUIUsing, WinUIType, NoAvaloniaType, MemberRenamed, MemberShape,
        ParameterRenamed, NameRenamed, NameRemoved, WinUIOnlyControl, ControlNotPorted, NoIsotopeMember, WindowIcon, Startup);

    /// <summary>The IDs whose diagnostics carry an edit.</summary>
    public static readonly ImmutableArray<string> Fixable = ImmutableArray.Create(
        ReactorNamespace.Id, WinUIUsing.Id, WinUIType.Id, MemberRenamed.Id, ParameterRenamed.Id, NameRenamed.Id, WindowIcon.Id);
}

/// <summary>The keys of a fixable diagnostic's properties.</summary>
internal static class Props
{
    /// <summary>The text that replaces the diagnostic's span.</summary>
    public const string Replace = "replace";
    /// <summary>"1": remove the line(s) of the diagnostic's span (a using directive).</summary>
    public const string RemoveLine = "removeLine";
    /// <summary>Namespaces the file must import, separated by ';'; "global X" for a global using.</summary>
    public const string Usings = "usings";
}
