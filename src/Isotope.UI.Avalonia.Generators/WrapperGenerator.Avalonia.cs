using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Isotope.UI.Wrappers.Generator;

// Our part of upstream's wrappers generator (tier 3, written for Avalonia): the helpers the gen-* transform rules
// in tools/sync/transforms.json call from the vendored WrapperGenerator.cs. Upstream's code stays as vendored;
// what differs on Avalonia is here or in a one-line call-site rule.
public sealed partial class WrapperGenerator
{
    private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// Logic hunk 1: the static <c>{Prop}Property</c> member backing a control property is an Avalonia property
    /// (<c>StyledProperty&lt;T&gt;</c>, <c>DirectProperty&lt;TOwner,T&gt;</c>, <c>AttachedProperty&lt;T&gt;</c>), where WinUI has
    /// a <c>DependencyProperty</c>.
    /// </summary>
    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString(Fqn) == "global::Avalonia.AvaloniaProperty")
                return true;
        return false;
    }

    /// <summary>
    /// Logic hunk 2: the control's content property, which Avalonia marks with <c>[Content]</c> on the property itself
    /// (WinUI names it in a class-level <c>[ContentProperty]</c>). The most-derived declaration wins.
    /// </summary>
    private static string? AvaloniaContentProperty(INamedTypeSymbol control)
    {
        for (INamedTypeSymbol? t = control; t is not null; t = t.BaseType)
            foreach (var p in t.GetMembers().OfType<IPropertySymbol>())
                if (!p.IsStatic && p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString(Fqn) == "global::Avalonia.Metadata.ContentAttribute"))
                    return p.Name;
        return null;
    }

    /// <summary>Our rule: an <c>[Obsolete]</c> control property isn't surfaced.</summary>
    private static bool IsObsolete(ISymbol symbol)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString(Fqn) == "global::System.ObsoleteAttribute");

    /// <summary>
    /// Our rule: the type is, or implements, the non-generic <c>System.Collections.IList</c> (Avalonia's
    /// <c>ItemCollection</c>), which the runtime's <c>ItemsList.Of</c> adapts to the items host's <c>IList&lt;object&gt;</c>.
    /// </summary>
    private static bool ImplementsNonGenericIList(ITypeSymbol type)
    {
        static bool IsIList(ITypeSymbol t) => t.ToDisplayString(Fqn) == "global::System.Collections.IList";
        return IsIList(type) || type.AllInterfaces.Any(IsIList);
    }

    private static readonly DiagnosticDescriptor MissingCounterpart = new(
        id: "ISOTOPEGEN001",
        title: "Record member has no control counterpart",
        messageFormat: "'{0}' declares {1}, but '{2}' has no settable property of that name the generator can map, so the descriptor would not write it. Give it the control's member name (tools/naming/README.md), write it in Customize with [WrapManual], or remove it.",
        category: "Isotope.Wrappers",
        defaultSeverity: DiagnosticSeverity.Error, // decided 2026-11-23: a silent drop is what this catches
        isEnabledByDefault: true);

    /// <summary>
    /// Our rule, descriptor-only mode: the record's public instance properties (callbacks aside) that the generated
    /// descriptor doesn't write, and that the author hasn't taken over (<c>[WrapManual]</c>, <c>Exclude</c>, element
    /// slots). Upstream drops them silently; a record mirroring a WinUI control (Header, IsSpellCheckEnabled,
    /// StepFrequency, …) would otherwise lose them without a trace on Avalonia. Null when there are none.
    /// </summary>
    private static Diagnostic? MissingCounterparts(
        INamedTypeSymbol element, INamedTypeSymbol control, ImmutableArray<PropInfo> props, string? contentElementName,
        bool panelChildren, bool items, ImmutableHashSet<string> exclude)
    {
        var mapped = new HashSet<string>(props.Select(p => p.Name), System.StringComparer.Ordinal);
        if (contentElementName is not null) mapped.Add(contentElementName);
        if (panelChildren) mapped.Add("Children");
        if (items) mapped.Add("Items");
        var missing = element.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public
                && !p.IsImplicitlyDeclared && p.Name != "EqualityContract"
                && p.Type.TypeKind != TypeKind.Delegate
                && !mapped.Contains(p.Name) && !exclude.Contains(p.Name))
            .Select(p => p.Name)
            .Distinct()
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0) return null;
        return Diagnostic.Create(MissingCounterpart, element.Locations.FirstOrDefault(),
            element.Name, string.Join(", ", missing.Select(n => "'" + n + "'")), control.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }
}
