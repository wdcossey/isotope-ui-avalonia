using System.Text;
using Isotope.UI.Mcp.Api;
using Isotope.UI.Migration;

namespace Isotope.UI.Mcp;

/// <summary>
/// Isotope's name for a Reactor name, from the migration's own embedded tables: the rename table
/// (<c>tools/naming/renames.json</c>), the WinUI types and the Reactor controls Isotope lacks.
/// </summary>
internal static class ReactorNames
{
    const string ReactorNamespace = "Microsoft.UI.Reactor";

    /// <summary>What the tables say about a name, as Markdown lines; empty when they hold nothing for it.</summary>
    public static IReadOnlyList<string> Lookup(string name, MigrationTables? tables = null)
    {
        tables ??= MigrationTables.Default;
        var n = name.Trim();
        if (n.EndsWith("()", StringComparison.Ordinal)) n = n[..^2];
        if (n.StartsWith("global::", StringComparison.Ordinal)) n = n["global::".Length..];
        var hits = new List<string>();
        if (n.Length == 0) return hits;

        // Reactor's namespaces are Isotope.UI's.
        if (n == ReactorNamespace || n.StartsWith(ReactorNamespace + ".", StringComparison.Ordinal))
        {
            var rest = n[ReactorNamespace.Length..].TrimStart('.');
            var isotope = "Isotope.UI" + (rest.Length > 0 ? "." + rest : "");
            hits.Add($"Namespace: `{ReactorNamespace}` is `Isotope.UI` in Isotope, so `{n}` is `{isotope}` (the migration rewrites it, ISOMIG001).");
            n = rest;
            if (n.Length == 0) return hits;
        }

        // A WinUI namespace, type (by its full name or its short one) or renamed enum member.
        if (tables.WinUI.Namespaces.TryGetValue(n, out var avaloniaNamespace)) hits.Add(Namespace(n, avaloniaNamespace));
        if (tables.WinUI.ByFullName.TryGetValue(n, out var winuiType)) hits.Add(WinUI(winuiType));
        else if (!n.Contains('.') && tables.WinUI.ByName.TryGetValue(n, out var byName)) hits.AddRange(byName.Select(WinUI));
        if (n.LastIndexOf('.') is var last and > 0)
        {
            var typeName = n[..last];
            var memberName = n[(last + 1)..];
            List<WinUIType> types = tables.WinUI.ByFullName.TryGetValue(typeName, out var full) ? [full]
                : tables.WinUI.ByName.TryGetValue(typeName, out var shortNames) ? shortNames : [];
            foreach (var t in types)
                if (t.Members.TryGetValue(memberName, out var renamed))
                    hits.Add(renamed is null
                        ? $"WinUI member: `{t.FullName}.{memberName}` has no Avalonia counterpart."
                        : $"WinUI member: `{t.FullName}.{memberName}` is `{t.Avalonia}.{renamed}` in Avalonia.");
        }

        var found = hits.Count;
        var dot = n.LastIndexOf('.');
        // Record.Member of a Reactor record: only that record's member answers it, never another record's member or
        // another factory's parameter of that name (NumberBoxElement.Header is not ExpanderElement.Header, #68).
        var ofRecord = false;
        if (dot > 0)
        {
            // Record.Member, or Factory.parameter.
            var owner = n[..dot].Split('.')[^1];
            var member = n[(dot + 1)..];
            foreach (var m in tables.Renames.Members.Where(m => m.Record == owner && m.Reactor == member)) hits.Add(Member(m, tables));
            // Reactor's factory name or Isotope's: the table names parameters by Isotope's factory (NumberBox.header is NumericUpDown's).
            var isotopeFactory = tables.Renames.Factories.FirstOrDefault(f => f.Reactor == owner && f.Applied && f.Avalonia is not null)?.Avalonia;
            foreach (var p in tables.Renames.Parameters.Where(p => (p.Factory == owner || p.Factory == isotopeFactory) && p.Reactor == member)) hits.Add(Parameter(p));
            foreach (var mod in tables.Renames.Modifiers.Where(e => e.Reactor == member && e.Target == owner)) hits.Add(Name("Modifier", mod));
            if (hits.Count > found || tables.WinUI.IsWinUINamespace(n)) return hits;
            ofRecord = IsRecord(owner, tables);
            // Factories.TabView, Core.Element: the last part on its own.
            n = member;
        }

        // Record.Member: past the record's own entries, only a modifier for any record (HAlign) can answer it.
        if (ofRecord)
        {
            foreach (var mod in tables.Renames.Modifiers.Where(e => e.Reactor == n && e.Target is null)) hits.Add(Name("Modifier", mod));
            return hits;
        }
        foreach (var t in tables.Renames.Types.Where(e => e.Reactor == n)) hits.Add(Name("Type", t));
        if (tables.Renames.Records.TryGetValue(n, out var control2) && !tables.Renames.Types.Any(t => t.Reactor == n))
            hits.Add($"Element record: `{n}` keeps its name in Isotope; its members are named after Avalonia's `{control2}`.");
        foreach (var f in tables.Renames.Factories.Where(e => e.Reactor == n)) hits.Add(Name("Factory", f));
        foreach (var mod in tables.Renames.Modifiers.Where(e => e.Reactor == n)) hits.Add(Name("Modifier", mod));
        if (tables.Controls.Factories.TryGetValue(n, out var c)) hits.Add(Control(c));
        foreach (var m in tables.Renames.Members.Where(m => m.Reactor == n)) hits.Add(Member(m, tables));
        foreach (var p in tables.Renames.Parameters.Where(p => p.Reactor == n)) hits.Add(Parameter(p));
        return hits;
    }

    /// <summary>A Reactor element record the tables know (by its Reactor name), renamed or not.</summary>
    static bool IsRecord(string name, MigrationTables tables) =>
        tables.Renames.Records.ContainsKey(name) || tables.Renames.Types.Any(t => t.Reactor == name && name.EndsWith("Element", StringComparison.Ordinal));

    /// <summary>Whether Isotope's record has the member: declared on it or a base type (Element's Key), or as a modifier
    /// of that name (Padding is on every element).</summary>
    static bool Has(ApiCatalog catalog, string record, string member)
    {
        var type = catalog.Find(record).Select(e => e.Member).OfType<Type>().FirstOrDefault();
        if (type is null) return true; // a record the catalog doesn't know: claim nothing
        // A modifier counts only if it takes this record (Padding takes any element; Expander's Header doesn't).
        if (catalog.Find(member).Any(e => e.Kind == ApiKind.Modifier && e.Member is System.Reflection.MethodInfo m && Takes(m, type))) return true;
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            if (catalog.Find(t.Name + "." + member).Count > 0) return true;
        return false;
    }

    /// <summary>Whether a modifier's receiver (its first parameter, or that parameter's constraints) accepts the record.</summary>
    static bool Takes(System.Reflection.MethodInfo modifier, Type record)
    {
        var receiver = modifier.GetParameters().FirstOrDefault()?.ParameterType;
        if (receiver is null) return false;
        if (!receiver.IsGenericParameter) return receiver.IsAssignableFrom(record);
        return receiver.GetGenericParameterConstraints().All(c => c.IsAssignableFrom(record) || (c.ContainsGenericParameters && c.IsInterface));
    }

    /// <summary>The record's name in Isotope: its rename, or its own.</summary>
    static string IsotopeRecord(string name, MigrationTables tables) =>
        tables.Renames.Types.FirstOrDefault(t => t.Reactor == name && t.Applied && t.Avalonia is not null)?.Avalonia ?? name;

    /// <summary>The tables' answer, or, for a name they don't hold, whether Isotope has it under the same name.</summary>
    public static string Map(string name, ApiCatalog? catalog = null, MigrationTables? tables = null)
    {
        tables ??= MigrationTables.Default;
        var hits = Lookup(name, tables).ToList();
        catalog ??= ApiCatalog.Default;
        // A Reactor name the rename table doesn't hold is Isotope's name too (Button, UseState), unless the table renames it.
        // A hit of any table section answers the name (a parameter "header" is not the Header modifier).
        string[] answered = ["Type:", "Factory:", "Modifier:", "Member:", "Factory parameter:", "Element record:"];
        var renamed = hits.Any(h => answered.Any(a => h.StartsWith(a, StringComparison.Ordinal)));
        var trimmed = name.Trim();
        if (trimmed.StartsWith(ReactorNamespace + ".", StringComparison.Ordinal)) trimmed = trimmed[(ReactorNamespace.Length + 1)..];
        var winui = tables.WinUI.IsWinUINamespace(trimmed);
        // Record.Member that neither the tables nor Isotope's record (renamed or not) has: say which record it is in
        // Isotope and that the member is not on it, rather than nothing (#68).
        if (!renamed && !winui && trimmed.LastIndexOf('.') is var at and > 0)
        {
            var owner = trimmed[..at].Split('.')[^1];
            var member = trimmed[(at + 1)..];
            var isotope = IsotopeRecord(owner, tables);
            if (IsRecord(owner, tables) && !Has(catalog, isotope, member))
            {
                var control = tables.Renames.Records.TryGetValue(owner, out var c) ? $" (its members are Avalonia's `{c}`'s)" : "";
                var named = isotope == owner ? $"`{owner}` keeps its name in Isotope" : $"`{owner}` is `{isotope}` in Isotope";
                hits.Add($"Element record: {named}, and has no `{member}`{control}. get_api `{isotope}` lists its members.");
                renamed = true;
            }
        }
        // Owner.Member as a whole (ButtonElement.OnClick), else its last part as a factory, hook, modifier or type
        // (Factories.Button is Button, Core.Element is Element), never another record's member of that name.
        var found = renamed || winui ? [] : catalog.Find(trimmed) is { Count: > 0 } whole ? whole
            : catalog.Find(trimmed.Split('.')[^1]).Where(e => e.Kind != ApiKind.Member).ToList();
        // A bare name that is only some types' member (OnClick) is not one Isotope name; Owner.Member is.
        var same = trimmed.Contains('.') || found.Any(e => e.Kind != ApiKind.Member) ? found.Where(e => e.Kind != ApiKind.Member || trimmed.Contains('.')).ToList() : [];
        var sb = new StringBuilder();
        if (hits.Count == 0 && same.Count == 0)
            return $"`{name.Trim()}` is not in the rename table or the migration's tables, and Isotope's API has nothing by that name. "
                + "Try search_api with part of the name, or migrate_code on the code that uses it.";
        sb.AppendLine($"# `{name.Trim()}` in Isotope").AppendLine();
        foreach (var h in hits) sb.AppendLine("- " + h);
        if (same.Count > 0)
        {
            sb.AppendLine($"- Same name: the rename table has no entry for `{trimmed}`, so Isotope keeps Reactor's name. Isotope has:");
            foreach (var e in same.Take(10)) sb.AppendLine($"  - {e.Kind.ToString().ToLowerInvariant()} `{e.Signature}`");
            if (same.Count > 10) sb.AppendLine($"  - … {same.Count - 10} more (get_api lists them all)");
        }
        return sb.ToString().TrimEnd();
    }

    static string Name(string what, NameEntry e)
    {
        var note = e.Note is null ? "" : " " + e.Note;
        if (e.Avalonia is null) return $"{what}: `{e.Reactor}` has no counterpart in Isotope.{note}";
        if (e.Status == "kept") return $"{what}: `{e.Reactor}` keeps Reactor's name in Isotope.{note}";
        var target = e.Target is null ? "" : $" (on `{e.Target}`)";
        var status = e.Applied ? "" : $" ({e.Status}: Isotope's code still has Reactor's name)";
        var with = e.With is null ? "" : $" The migration writes `({e.Avalonia}(...) with {{ {e.With} }})` to keep Reactor's behaviour.";
        return $"{what}: `{e.Reactor}`{target} is `{e.Avalonia}` in Isotope{status}.{note}{with}";
    }

    static string Member(MemberEntry m, MigrationTables tables)
    {
        // The record's own name in Isotope, if its type was renamed.
        var record = tables.Renames.Types.FirstOrDefault(t => t.Reactor == m.Record && t.Applied && t.Avalonia is not null)?.Avalonia ?? m.Record;
        var note = m.Note is null ? "" : " " + m.Note;
        var avalonia = string.Join(" and ", m.Avalonia.Select(a => $"`{record}.{a}`"));
        return m.Kind switch
        {
            "rename" => $"Member: `{m.Record}.{m.Reactor}` is {avalonia} in Isotope.{note}",
            "shape" => $"Member: `{m.Record}.{m.Reactor}` changed shape: it is {avalonia} in Isotope.{note}",
            _ => $"Member: `{m.Record}.{m.Reactor}` keeps Reactor's name ({avalonia}).{note}",
        };
    }

    static string Parameter(ParameterEntry p) => p.Avalonia is null
        ? $"Factory parameter: `{p.Factory}({p.Reactor}: …)` has no counterpart in Isotope.{(p.Note is null ? "" : " " + p.Note)}"
        : $"Factory parameter: `{p.Factory}({p.Reactor}: …)` is `{p.Factory}({p.Avalonia}: …)` in Isotope.";

    static string Control(ControlEntry c) => c.WinUIOnly
        ? $"Factory: `{c.Factory}` is a WinUI control with no Isotope counterpart. {c.Alternative}".TrimEnd()
        : $"Factory: `{c.Factory}` has no Isotope factory yet. {c.Alternative}".TrimEnd();

    static string WinUI(WinUIType t)
    {
        var members = t.Members.Count == 0 ? "" : " Members renamed: " + string.Join(", ", t.Members.Select(kv => $"`{kv.Key}` is `{kv.Value ?? "(none)"}`")) + ".";
        var note = t.Note is null ? "" : " " + t.Note;
        return t.Avalonia is null
            ? $"WinUI type: `{t.FullName}` has no Avalonia counterpart.{note}"
            : $"WinUI type: `{t.FullName}` is `{t.Avalonia}` in Avalonia.{note}{members}";
    }

    static string Namespace(string winui, string? avalonia) => avalonia is null
        ? $"WinUI namespace: `{winui}` has no Avalonia counterpart (the migration removes its using)."
        : $"WinUI namespace: `{winui}`'s types are mostly in Avalonia's `{avalonia}`.";
}
