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
    /// field the record (and its bases below <c>Element</c>) declares, through its property for an auto-property: callbacks
    /// (<c>On{X}</c> delegates, which the descriptor reads from the latest element) by presence, other delegates (a
    /// template, a formatter) and elements and reference-type collections (<c>Children</c>, <c>Items</c>, <c>Setters</c>)
    /// and records by reference, an <c>object</c> as the button arms compare their content (an element never equal), and the rest by
    /// <c>EqualityComparer&lt;T&gt;.Default</c>. <c>HasCallbacks</c> says whether any delegate is set. Nothing is compared,
    /// so the element is never skipped, when a field can't be: one the record can't reach (a base's internal member in
    /// another assembly), a member hidden by a same-named one, a struct holding an element (<c>Optional&lt;Element&gt;</c>), a
    /// ref-like or pointer type, or a base below <c>Element</c> that declares either skip member. One the record declares
    /// itself is left to it. The library's own records get nothing:
    /// their arms in <c>Element.ShallowEquals</c> decide, so they behave as before. Empty when nothing is emitted.
    /// </summary>
    private static string SkipMembers(INamedTypeSymbol element, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName("Isotope.UI.Core.Element") is not { } elementBase) return "";
        if (SymbolEqualityComparer.Default.Equals(element.ContainingAssembly, elementBase.ContainingAssembly)) return "";
        // A library older than #77 has no ShallowEqualsCore to override.
        if (elementBase.GetMembers("ShallowEqualsCore").IsEmpty) return "";
        if (element.IsGenericType || element.ContainingType is not null) return "";

        // A skip member a base below Element declares knows nothing of this record's members: then nothing is generated,
        // and the element is updated on every render. One the record declares itself is left to it.
        for (INamedTypeSymbol? b = element.BaseType; b is not null && !SymbolEqualityComparer.Default.Equals(b, elementBase); b = b.BaseType)
            if (!b.GetMembers("ShallowEqualsCore").IsEmpty || !b.GetMembers("HasCallbacks").IsEmpty) return "";

        var compares = new List<string>();
        var callbacks = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        bool comparable = true;
        for (INamedTypeSymbol? t = element; t is not null && !SymbolEqualityComparer.Default.Equals(t, elementBase); t = t.BaseType)
        {
            // A base from another assembly (a built-in record, a vendor's) shows none of its private state: nothing is compared.
            if (!SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly))
            {
                comparable = false;
                break;
            }
            // The type's state: its explicit fields, and the auto-properties (positional ones too) behind its backing
            // fields. A computed property is left out: it follows from the fields, and its getter could throw here.
            var autoProperties = new HashSet<string>(t.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic && f.AssociatedSymbol is IPropertySymbol).Select(f => f.AssociatedSymbol!.Name), System.StringComparer.Ordinal);
            foreach (var member in t.GetMembers())
            {
                if (member.IsStatic || member.Name == "EqualityContract") continue;
                ITypeSymbol type;
                switch (member)
                {
                    case IPropertySymbol { IsIndexer: false, GetMethod: not null, ExplicitInterfaceImplementations.IsEmpty: true } p when autoProperties.Contains(p.Name): type = p.Type; break;
                    case IFieldSymbol { IsConst: false, IsImplicitlyDeclared: false, AssociatedSymbol: null } f: type = f.Type; break;
                    default: continue;
                }
                // A member hidden by a same-named one (`new`): nothing is compared.
                if (!seen.Add(member.Name))
                {
                    comparable = false;
                    continue;
                }
                // A base's member the record can't reach (private, or internal to another assembly): nothing is compared.
                if (!compilation.IsSymbolAccessibleWithin(member, element))
                {
                    comparable = false;
                    continue;
                }
                var name = "@" + member.Name;
                if (type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter)
                {
                    comparable = false;
                    continue;
                }
                if (type.TypeKind == TypeKind.Delegate)
                {
                    // A callback by presence, as the built-in arms (the skip refreshes the element its handler reads); any
                    // other delegate by reference, since what it returns can change with what it captures.
                    compares.Add(IsCallbackName(member.Name)
                        ? $"(this.{name} is null) == (o.{name} is null)"
                        : $"global::System.Object.ReferenceEquals(this.{name}, o.{name})");
                    callbacks.Add($"this.{name} is not null");
                }
                else if (HoldsElement(type, elementBase, 0))
                {
                    comparable = false;
                    continue;
                }
                else if (type.SpecialType == SpecialType.System_Object)
                    compares.Add($"!(this.{name} is global::Isotope.UI.Core.Element) && !(o.{name} is global::Isotope.UI.Core.Element) && global::System.Object.Equals(this.{name}, o.{name})");
                else if (type.IsReferenceType && type.SpecialType != SpecialType.System_String
                    && (DerivesFrom(type, elementBase) || type.TypeKind == TypeKind.Array || IsEnumerable(type) || type.IsRecord))
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

    private static readonly DiagnosticDescriptor UnwiredCallback = new(
        id: "ISOTOPEGEN002",
        title: "Record callback is never wired",
        messageFormat: "'{0}' declares callback '{1}', but '{2}' has no routed event, [WrapEvent] event or controlled property it maps to, so the descriptor would never call it. Name it after a routed event of the control, add [WrapEvent(\"{3}\")] for a plain .NET event (the callback then takes the event's args), or mark it [WrapManual(\"{1}\")] and wire it in Customize.",
        category: "Isotope.Wrappers",
        defaultSeverity: DiagnosticSeverity.Error, // as ISOTOPEGEN001: a silent drop is what this catches
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CallbackMismatch = new(
        id: "ISOTOPEGEN003",
        title: "Record callback doesn't take what its event passes",
        messageFormat: "'{0}' declares '{1}' as '{2}', but the descriptor calls it with {3} when '{4}' fires. Declare it as '{5}'{6}.",
        category: "Isotope.Wrappers",
        defaultSeverity: DiagnosticSeverity.Error, // the generated call wouldn't compile
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedHandlerAttribute = new(
        id: "ISOTOPEGEN004",
        title: "Decorator records aren't supported on Isotope yet",
        messageFormat: "'{0}' has [{1}], which Isotope doesn't support yet: the handler it generates needs engine members Isotope doesn't have. Remove it and map the control's properties with [GenerateReactorDescriptor] (writing the rest in Setters or Customize), or wrap the control in a component.",
        category: "Isotope.Wrappers",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Our rule (#78): &lt;c&gt;[WrapDecorator]&lt;/c&gt; and &lt;c&gt;[WrapPolymorphic]&lt;/c&gt; generate a handler that calls engine members
    /// Isotope left out (Spike F, tier 3), so the error was reported inside the generated file. Reported at the attribute
    /// instead, and nothing is generated. Null when the record has neither.
    /// </summary>
    private static Diagnostic? UnsupportedHandler(INamedTypeSymbol element, Compilation compilation)
    {
        foreach (var name in new[] { "WrapDecorator", "WrapPolymorphic" })
        {
            if (compilation.GetTypeByMetadataName("Isotope.UI.Wrappers." + name + "Attribute") is not { } symbol) continue;
            var attr = element.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, symbol));
            if (attr is null) continue;
            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? element.Locations.FirstOrDefault();
            return Diagnostic.Create(UnsupportedHandlerAttribute, location, element.Name, name);
        }
        return null;
    }

    /// <summary>
    /// Our rule (#78), descriptor-only mode: checks the record's &lt;c&gt;On{X}&lt;/c&gt; delegate members against what the
    /// descriptor wires. A wired event whose callback can't take what the generated trampoline passes is reported at the
    /// member (ISOTOPEGEN003) and left unwired, so the error isn't inside the generated file. A callback the descriptor
    /// wires to nothing (a plain &lt;c&gt;EventHandler&lt;/c&gt; event, a name the control lacks) is reported (ISOTOPEGEN002):
    /// upstream drops it silently. A callback the author took over isn't reported (&lt;see cref="TakenOver"/&gt;).
    /// </summary>
    private static (ImmutableArray<EventInfo> Wired, ImmutableArray<Diagnostic> Diagnostics) CheckCallbacks(
        INamedTypeSymbol element, INamedTypeSymbol control, ImmutableArray<PropInfo> props, ImmutableArray<EventInfo> events,
        ImmutableHashSet<string> exclude, Compilation compilation)
    {
        var callbacks = new Dictionary<string, IPropertySymbol>(System.StringComparer.Ordinal);
        foreach (var p in element.GetMembers().OfType<IPropertySymbol>())
            if (!p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public && !p.IsImplicitlyDeclared
                && IsCallbackName(p.Name) && p.Type.TypeKind == TypeKind.Delegate && !callbacks.ContainsKey(p.Name))
                callbacks[p.Name] = p;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var wired = ImmutableArray.CreateBuilder<EventInfo>();
        var handled = new HashSet<string>(props.Where(p => p.Controlled).Select(p => "On" + p.Name + "Changed"), System.StringComparer.Ordinal);
        foreach (var e in events)
        {
            var name = "On" + e.Name;
            handled.Add(name);
            // Checked even when the author takes the callback over: the generated trampoline still calls it.
            if (!callbacks.TryGetValue(name, out var member)
                || PassedTypes(control, e) is not { } passed || Accepts(member.Type, passed, compilation))
            {
                wired.Add(e);
                continue;
            }
            var args = passed.Length == 0
                ? "no arguments"
                : "(" + string.Join(", ", passed.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ")";
            var expected = passed.Length == 0
                ? "Action"
                : "Action<" + string.Join(", ", passed.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">";
            diagnostics.Add(Diagnostic.Create(CallbackMismatch, member.Locations.FirstOrDefault(),
                element.Name, name, member.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), args,
                control.Name + "." + e.Name, expected, e.Typed ? ", or give [WrapEvent] the Arg or Args to pass" : ""));
        }

        foreach (var member in callbacks.Values.OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handled.Contains(member.Name) || TakenOver(member.Name, exclude)) continue;
            diagnostics.Add(Diagnostic.Create(UnwiredCallback, member.Locations.FirstOrDefault(),
                element.Name, member.Name, control.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), member.Name.Substring(2)));
        }
        return (wired.ToImmutable(), diagnostics.ToImmutable());
    }

    // A callback the author wires: the member itself, its event On{X} or its value On{P}Changed is [WrapManual] or
    // Exclude'd (Isotope's own records exclude the event they wire in Customize, or take the value over).
    private static bool TakenOver(string name, ImmutableHashSet<string> exclude)
    {
        var x = name.Substring(2);
        return exclude.Contains(name) || exclude.Contains(x)
            || (x.Length > 7 && x.EndsWith("Changed", System.StringComparison.Ordinal) && exclude.Contains(x.Substring(0, x.Length - 7)));
    }

    // The types the generated trampoline passes to the callback (Emit: none for an untyped event, else the whole args or
    // the [WrapEvent] Arg/Args projections). Null when they can't be resolved (upstream's WrapEventAnalyzer reports those).
    private static ITypeSymbol[]? PassedTypes(INamedTypeSymbol control, EventInfo e)
    {
        if (!e.Typed) return [];
        IEventSymbol? evt = null;
        for (INamedTypeSymbol? t = control; t is not null && evt is null; t = t.BaseType)
            evt = t.GetMembers(e.Name).OfType<IEventSymbol>().FirstOrDefault();
        if ((evt?.Type as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters is not { Length: 2 } ps) return null;
        var argsType = ps[1].Type;
        if (e.ArgProperties.IsDefaultOrEmpty) return [argsType];
        var types = new ITypeSymbol[e.ArgProperties.Length];
        for (int i = 0; i < types.Length; i++)
        {
            IPropertySymbol? prop = null;
            for (ITypeSymbol? t = argsType; t is not null && prop is null; t = t.BaseType)
                prop = t.GetMembers(e.ArgProperties[i]).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic);
            if (prop is null) return null;
            types[i] = prop.Type;
        }
        return types;
    }

    // The callback's delegate takes the passed values, in order, by implicit conversion (as the C# call would).
    private static bool Accepts(ITypeSymbol callback, ITypeSymbol[] passed, Compilation compilation)
    {
        if (callback is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }) return false;
        if (invoke.Parameters.Length != passed.Length) return false;
        for (int i = 0; i < passed.Length; i++)
            if (invoke.Parameters[i].RefKind != RefKind.None || !compilation.ClassifyCommonConversion(passed[i], invoke.Parameters[i].Type).IsImplicit)
                return false;
        return true;
    }

    /// <summary>
    /// Our rule (#78), descriptor-only mode: the descriptor reads &lt;c&gt;e.Setters&lt;/c&gt; only when the record (or a base
    /// record) declares it (a property, as upstream's polymorphic path checks, or a field). A record without it built with an error
    /// inside the generated file; &lt;c&gt;GetSetters&lt;/c&gt; is optional on the descriptor.
    /// </summary>
    private static string WithSettersIfDeclared(INamedTypeSymbol element, string source)
    {
        for (INamedTypeSymbol? t = element; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            if (t.GetMembers("Setters").Any(m => !m.IsStatic && m is IPropertySymbol or IFieldSymbol))
                return source;
        return source.Replace("            GetSetters = static e => e.Setters," + System.Environment.NewLine, "");
    }

    // On{X}, the callbacks' names (tools/naming/README.md).
    private static bool IsCallbackName(string name) => name.Length > 2 && name.StartsWith("On", System.StringComparison.Ordinal) && char.IsUpper(name[2]);

    // A struct with an element in it (Optional<Element>, a tuple, a field): its equality would compare the element by
    // value. A record or other class holding one is compared by reference instead (above).
    private static bool HoldsElement(ITypeSymbol type, INamedTypeSymbol elementBase, int depth)
    {
        if (!type.IsValueType || depth > 4) return false;
        if (type is not INamedTypeSymbol named) return false;
        bool Element(ITypeSymbol t) => DerivesFrom(t, elementBase) || HoldsElement(t, elementBase, depth + 1);
        if (named.IsGenericType && named.TypeArguments.Any(Element)) return true;
        return named.SpecialType == SpecialType.None
            && named.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && Element(f.Type));
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
