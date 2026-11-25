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
    /// <summary>
    /// Our rule (#77), descriptor-only mode: the skip members of a record outside the library that declares
    /// <c>Element</c>, appended to its descriptor's source, so a library's element is skipped when it equals the last
    /// render's, as a built-in one is (FINDINGS, decisions after Spike F, 3). <c>ShallowEqualsCore</c> compares every
    /// member the record (and its bases below <c>Element</c>) declares: delegates by presence, elements and reference-type
    /// collections (<c>Children</c>, <c>Items</c>, <c>Setters</c>) by reference, an <c>object</c> as the button arms compare
    /// their content (an element never equal), and the rest by <c>EqualityComparer&lt;T&gt;.Default</c>. <c>HasCallbacks</c>
    /// says whether any delegate is set. A member the record declares itself is left to it. The library's own records
    /// get nothing: their arms in <c>Element.ShallowEquals</c> decide, so they behave as before. Empty when nothing is
    /// emitted.
    /// </summary>
    private static string SkipMembers(INamedTypeSymbol element, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName("Isotope.UI.Core.Element") is not { } elementBase) return "";
        if (SymbolEqualityComparer.Default.Equals(element.ContainingAssembly, elementBase.ContainingAssembly)) return "";
        // A library older than #77 has no ShallowEqualsCore to override.
        if (elementBase.GetMembers("ShallowEqualsCore").IsEmpty) return "";
        if (element.IsGenericType || element.ContainingType is not null) return "";

        var compares = new List<string>();
        var callbacks = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        bool comparable = true;
        for (INamedTypeSymbol? t = element; t is not null && !SymbolEqualityComparer.Default.Equals(t, elementBase); t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member.IsStatic || member.Name == "EqualityContract") continue;
                if (!SymbolEqualityComparer.Default.Equals(t, element) && member.DeclaredAccessibility == Accessibility.Private) continue;
                ITypeSymbol type;
                switch (member)
                {
                    case IPropertySymbol { IsIndexer: false, GetMethod: not null } p: type = p.Type; break;
                    case IFieldSymbol { IsConst: false, IsImplicitlyDeclared: false, AssociatedSymbol: null } f: type = f.Type; break;
                    default: continue;
                }
                if (!seen.Add(member.Name)) continue;
                var name = "@" + member.Name;
                if (type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter)
                {
                    comparable = false;
                    continue;
                }
                if (type.TypeKind == TypeKind.Delegate)
                {
                    compares.Add($"(this.{name} is null) == (o.{name} is null)");
                    callbacks.Add($"this.{name} is not null");
                }
                else if (type.SpecialType == SpecialType.System_Object)
                    compares.Add($"!(this.{name} is global::Isotope.UI.Core.Element) && !(o.{name} is global::Isotope.UI.Core.Element) && global::System.Object.Equals(this.{name}, o.{name})");
                else if (type.IsReferenceType && type.SpecialType != SpecialType.System_String
                    && (DerivesFrom(type, elementBase) || type.TypeKind == TypeKind.Array || IsEnumerable(type)))
                    compares.Add($"global::System.Object.ReferenceEquals(this.{name}, o.{name})");
                else
                {
                    var fq = (type.IsReferenceType ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type).ToDisplayString(Fqn);
                    compares.Add($"global::System.Collections.Generic.EqualityComparer<{fq}>.Default.Equals(this.{name}, o.{name})");
                }
            }
        }

        bool emitEquals = comparable && element.GetMembers("ShallowEqualsCore").IsEmpty;
        bool emitCallbacks = callbacks.Count > 0 && element.GetMembers("HasCallbacks").IsEmpty;
        if (!emitEquals && !emitCallbacks) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("// Isotope (#77): the skip of an element equal to the last render's, as the built-in records have it.");
        sb.AppendLine($"partial record {element.Name}");
        sb.AppendLine("{");
        if (emitCallbacks)
        {
            sb.AppendLine("    /// <inheritdoc/>");
            sb.AppendLine($"    protected override bool HasCallbacks => {string.Join(" || ", callbacks)};");
        }
        if (emitEquals)
        {
            if (emitCallbacks) sb.AppendLine();
            sb.AppendLine("    /// <inheritdoc/>");
            sb.AppendLine("    protected override bool ShallowEqualsCore(global::Isotope.UI.Core.Element other)");
            // A type derived from this record, without its own override, would be compared on these members only: never equal.
            var all = new List<string> { $"GetType() == typeof({element.Name})", $"other is {element.Name} o" };
            all.AddRange(compares);
            sb.AppendLine("        => " + string.Join("\n            && ", all) + ";");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }

    private static bool IsEnumerable(ITypeSymbol type)
    {
        static bool Is(ITypeSymbol t) => t.ToDisplayString(Fqn) == "global::System.Collections.IEnumerable";
        return Is(type) || type.AllInterfaces.Any(Is);
    }
}
