using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Isotope.UI.Core;

namespace Isotope.UI.Mcp.Api;

/// <summary>What a public API entry is, as the tools group them.</summary>
internal enum ApiKind
{
    /// <summary>A static method of <c>Isotope.UI.Factories</c>, which builds an element.</summary>
    Factory,
    /// <summary>An extension method on elements (<c>.Margin(8)</c>).</summary>
    Modifier,
    /// <summary>A <c>Use*</c> method (<c>RenderContext.UseState</c>).</summary>
    Hook,
    /// <summary>An element record (a subtype of <c>Element</c>).</summary>
    Element,
    /// <summary>Any other public type.</summary>
    Type,
    /// <summary>A member of a public type that is none of the above: a property, a constructor, a method.</summary>
    Member,
}

/// <summary>One public type or member, with its C# declaration and its documentation ID.</summary>
internal sealed record ApiEntry(ApiKind Kind, string Name, string Owner, string Namespace, string Signature, string DocId, MemberInfo Member, XmlDocs Docs)
{
    /// <summary>The summary of its doc comment, on one line.</summary>
    public string Summary => Docs.Summary(DocId);

    /// <summary>The whole doc comment, as Markdown.</summary>
    public string FullDoc => Docs.Full(DocId);

    /// <summary>How it is written in code: <c>Factories.Button</c>, <c>ButtonElement</c>.</summary>
    public string QualifiedName => Kind is ApiKind.Element or ApiKind.Type ? Owner : Owner + "." + Name;

    public bool IsType => Kind is ApiKind.Element or ApiKind.Type;
}

/// <summary>
/// The public API of the library and the desktop startup, read by reflection from the assemblies this tool ships with
/// (the packages' own DLLs) and their XML doc files.
/// </summary>
internal sealed class ApiCatalog
{
    static readonly Lazy<ApiCatalog> Shared = new(() => new ApiCatalog([typeof(Factories).Assembly, typeof(ReactorAppDesktop).Assembly]));

    /// <summary>The catalog of <c>Isotope.UI.Avalonia</c> and <c>Isotope.UI.Avalonia.Desktop</c>.</summary>
    public static ApiCatalog Default => Shared.Value;

    public IReadOnlyList<ApiEntry> Entries { get; }

    /// <summary>The doc files read, by assembly name, with their member counts.</summary>
    public IReadOnlyDictionary<string, XmlDocs> Docs { get; }

    // Members every record has, which say nothing about it.
    static readonly HashSet<string> RecordNoise = ["Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct", "EqualityContract", "op_Equality", "op_Inequality"];

    public ApiCatalog(IEnumerable<Assembly> assemblies)
    {
        var signatures = new Signatures();
        var entries = new List<ApiEntry>();
        var docs = new Dictionary<string, XmlDocs>();
        foreach (var assembly in assemblies)
        {
            var xml = XmlDocs.For(assembly);
            docs[assembly.GetName().Name!] = xml;
            foreach (var type in assembly.GetExportedTypes().Where(Shown).OrderBy(t => t.FullName, StringComparer.Ordinal))
                AddType(type, xml, signatures, entries);
        }
        Entries = entries;
        Docs = docs;
    }

    static bool Shown(Type t) =>
        !t.Name.Contains('<') && !t.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && !(t.Namespace ?? "").Split('.').Contains("Internal")
        && t.GetCustomAttribute<EditorBrowsableAttribute>()?.State != EditorBrowsableState.Never
        && (!t.IsNested || Shown(t.DeclaringType!));

    static bool Shown(MemberInfo m) =>
        !m.Name.Contains('<') && !m.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && m.GetCustomAttribute<EditorBrowsableAttribute>()?.State != EditorBrowsableState.Never;

    void AddType(Type type, XmlDocs xml, Signatures signatures, List<ApiEntry> entries)
    {
        var owner = Signatures.Display(type);
        var ns = type.Namespace ?? "";
        var isElement = typeof(Element).IsAssignableFrom(type) && type != typeof(Element);
        entries.Add(new ApiEntry(isElement ? ApiKind.Element : ApiKind.Type, owner, owner, ns, Signatures.Type(type), DocIds.Of(type), type, xml));
        if (type.IsEnum)
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(Shown))
                entries.Add(new ApiEntry(ApiKind.Member, f.Name, owner, ns, owner + "." + f.Name, DocIds.Of(f), f, xml));
            return;
        }
        if (typeof(Delegate).IsAssignableFrom(type)) return;

        var record = Signatures.IsRecord(type);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // C# 14 extension blocks: the compiler's grouping types hold the members as declared (signature and doc), and
        // the static class holds their implementations under the same names, which are left out.
        var extensionMembers = ExtensionBlocks(type, xml, signatures, ns).ToList();
        entries.AddRange(extensionMembers);
        var implemented = extensionMembers.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(Shown))
        {
            if (record && c.GetParameters() is [var only] && only.ParameterType == type) continue; // the copy constructor
            entries.Add(new ApiEntry(ApiKind.Member, type.Name.Split('`')[0], owner, ns, signatures.Method(c, owner), DocIds.Of(c), c, xml));
        }
        foreach (var m in type.GetMethods(flags).Where(Shown))
        {
            if (m.IsSpecialName || implemented.Contains(m.Name) || (record && RecordNoise.Contains(m.Name))) continue;
            entries.Add(new ApiEntry(Classify(type, m), m.Name, owner, ns, signatures.Method(m, owner), DocIds.Of(m), m, xml));
        }
        foreach (var p in type.GetProperties(flags).Where(Shown))
        {
            if (record && RecordNoise.Contains(p.Name)) continue;
            entries.Add(new ApiEntry(ApiKind.Member, p.Name, owner, ns, signatures.Property(p, owner), DocIds.Of(p), p, xml));
        }
        foreach (var f in type.GetFields(flags).Where(Shown))
            entries.Add(new ApiEntry(ApiKind.Member, f.Name, owner, ns, signatures.Field(f, owner), DocIds.Of(f), f, xml));
        foreach (var e in type.GetEvents(flags).Where(Shown))
            entries.Add(new ApiEntry(ApiKind.Member, e.Name, owner, ns, signatures.Event(e, owner), DocIds.Of(e), e, xml));
    }

    static ApiKind Classify(Type type, MethodInfo m)
    {
        if (m.Name.Length > 3 && m.Name.StartsWith("Use", StringComparison.Ordinal) && char.IsUpper(m.Name[3]) && IsHookOwner(type, m)) return ApiKind.Hook;
        if (type.Name == "Factories" && m.IsStatic) return ApiKind.Factory;
        if (m.IsDefined(typeof(ExtensionAttribute), false) && m.GetParameters() is [var first, ..] && IsElement(first.ParameterType)) return ApiKind.Modifier;
        return ApiKind.Member;
    }

    // A hook is called on a component's context: a method of RenderContext or Component, or an extension of one.
    static bool IsHookOwner(Type type, MethodInfo m) =>
        IsContext(type)
        || (m.IsDefined(typeof(ExtensionAttribute), false) && m.GetParameters() is [var first, ..] && IsContext(first.ParameterType));

    static bool IsContext(Type t) => typeof(RenderContext).IsAssignableFrom(t) || typeof(Isotope.UI.Core.Component).IsAssignableFrom(t);

    // An element type, or a type parameter constrained to one (T Margin<T>(this T el) where T : Element).
    static bool IsElement(Type t) =>
        t.IsGenericParameter ? t.GetGenericParameterConstraints().Any(IsElement) : typeof(Element).IsAssignableFrom(t);

    // The members of the C# 14 extension blocks of a static class, under the type they extend (ReactorApp.Run).
    static IEnumerable<ApiEntry> ExtensionBlocks(Type type, XmlDocs xml, Signatures signatures, string ns)
    {
        if (!(type.IsAbstract && type.IsSealed)) yield break;
        foreach (var group in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(n => n.Name.StartsWith("<G>$", StringComparison.Ordinal)))
        {
            // The marker type's <Extension>$ method takes the receiver: its type is the extended one.
            var receiver = group.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(n => n.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .FirstOrDefault(m => m.Name == "<Extension>$")?.GetParameters().FirstOrDefault()?.ParameterType;
            if (receiver is null) continue;
            // extension<T>(T el) where T : Element: members of elements, listed under the static class, as modifiers.
            var owner = receiver.IsGenericParameter ? Signatures.Display(type) : Signatures.Display(receiver);
            foreach (var m in group.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(Shown))
            {
                if (m.IsSpecialName) continue;
                var kind = Classify(receiver, m) is ApiKind.Member && !m.IsStatic && IsElement(receiver) ? ApiKind.Modifier : Classify(receiver, m);
                yield return new ApiEntry(kind, m.Name, owner, ns, signatures.Method(m, owner), DocIds.Of(m), m, xml);
            }
            foreach (var p in group.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(Shown))
                yield return new ApiEntry(ApiKind.Member, p.Name, owner, ns, signatures.Property(p, owner), DocIds.Of(p), p, xml);
        }
    }

    /// <summary>The kinds search_api looks through (members are reached through their type, with get_api).</summary>
    public static readonly ApiKind[] Searchable = [ApiKind.Factory, ApiKind.Modifier, ApiKind.Hook, ApiKind.Element, ApiKind.Type];

    /// <summary>
    /// Entries whose name or summary matches the query, best first: the exact name, then names that start with it,
    /// then names that contain it, then summaries that hold every word of it.
    /// </summary>
    public IReadOnlyList<ApiEntry> Search(string query, IReadOnlyCollection<ApiKind>? kinds = null)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        kinds ??= Searchable;
        return Entries
            .Where(e => kinds.Contains(e.Kind))
            .Select(e => (Entry: e, Score: Score(e, q, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Array.IndexOf(Searchable, x.Entry.Kind))
            .ThenBy(x => x.Entry.Name, StringComparer.Ordinal)
            .Select(x => x.Entry)
            .ToList();
    }

    static int Score(ApiEntry e, string q, string[] words)
    {
        var name = e.Name.Split('<')[0];
        if (name.Equals(q, StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 60;
        var summary = e.Summary;
        if (words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase) || summary.Contains(w, StringComparison.OrdinalIgnoreCase))) return 20;
        return 0;
    }

    /// <summary>
    /// The entries a name refers to: every overload of a factory, modifier or hook (<c>Button</c>, <c>UseState</c>), a type
    /// (<c>ButtonElement</c>), or a type's member (<c>ButtonElement.OnClick</c>, <c>ReactorApp.Run</c>). Namespaces,
    /// <c>()</c> and generic arguments are ignored. Exact case first, then any case.
    /// </summary>
    public IReadOnlyList<ApiEntry> Find(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return [];
        return Find(n, StringComparison.Ordinal) is { Count: > 0 } exact ? exact : Find(n, StringComparison.OrdinalIgnoreCase);
    }

    List<ApiEntry> Find(string n, StringComparison cmp)
    {
        static string Bare(string s) => s.Split('<')[0];
        // A type, by its name or its namespace-qualified name.
        var types = Entries.Where(e => e.IsType && (Bare(e.Name).Equals(n, cmp) || (e.Namespace + "." + Bare(e.Name)).Equals(n, cmp))).ToList();
        var dot = n.LastIndexOf('.');
        if (dot > 0)
        {
            var owner = n[..dot];
            var member = n[(dot + 1)..];
            var members = Entries.Where(e => !e.IsType && Bare(e.Name).Equals(member, cmp)
                && (Bare(e.Owner).Equals(owner, cmp) || (e.Namespace + "." + Bare(e.Owner)).Equals(owner, cmp)
                    || Bare(e.Owner).Equals(owner.Split('.')[^1], cmp))).ToList();
            return [.. types, .. members];
        }
        var named = Entries.Where(e => e.Kind is ApiKind.Factory or ApiKind.Modifier or ApiKind.Hook && Bare(e.Name).Equals(n, cmp)).ToList();
        if (types.Count + named.Count > 0) return [.. types, .. named];
        // Otherwise members of any type with that name (OnClick: every record that has one).
        return Entries.Where(e => e.Kind == ApiKind.Member && Bare(e.Name).Equals(n, cmp) && !e.Name.Equals(Bare(e.Owner), cmp)).ToList();
    }

    static string Normalize(string name)
    {
        var n = name.Trim().TrimStart('.');
        if (n.EndsWith("()", StringComparison.Ordinal)) n = n[..^2];
        var paren = n.IndexOf('(');
        if (paren >= 0) n = n[..paren];
        // Generic arguments, nested or not, anywhere: Command<T>.Execute, UseState<List<int>>.
        var sb = new System.Text.StringBuilder(n.Length);
        var depth = 0;
        foreach (var ch in n)
        {
            if (ch == '<') depth++;
            else if (ch == '>') depth = Math.Max(0, depth - 1);
            else if (depth == 0) sb.Append(ch);
        }
        n = sb.ToString();
        if (n.StartsWith("global::", StringComparison.Ordinal)) n = n["global::".Length..];
        return n.Trim();
    }

    /// <summary>The public members of a type (constructors, properties, methods…), as listed under it by get_api.</summary>
    public IEnumerable<ApiEntry> MembersOf(ApiEntry type) =>
        Entries.Where(e => !e.IsType && e.Owner == type.Owner && e.Namespace == type.Namespace);

    /// <summary>The factories that return a type (for an element record: how to build one).</summary>
    public IEnumerable<ApiEntry> FactoriesOf(ApiEntry type) =>
        type.Member is Type t
            ? Entries.Where(e => e.Kind == ApiKind.Factory && e.Member is MethodInfo m && m.ReturnType == t)
            : [];
}
