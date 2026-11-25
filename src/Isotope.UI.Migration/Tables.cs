using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Isotope.UI.Migration;

/// <summary>A member of an element record whose Reactor name or shape differs (renames.json, <c>members</c>).</summary>
internal sealed class MemberEntry
{
    public string Record = "", Reactor = "", Kind = "";
    public string[] Avalonia = [];
    public string? Note;

    /// <summary>One Avalonia member, as most entries have (Grid's Definition has two).</summary>
    public string? SingleAvalonia => Avalonia.Length == 1 ? Avalonia[0] : null;
}

/// <summary>A factory parameter renamed, or with no counterpart (renames.json, <c>parameters</c>): then
/// <see cref="Avalonia"/> is null, <see cref="Note"/> says what to do instead, and <see cref="Position"/> is its index, so
/// a positional argument is found too (NumberBox's header, #68).</summary>
internal sealed class ParameterEntry
{
    public string Factory = "", Reactor = "";
    public string? Avalonia, Note;
    public int? Position;
}

/// <summary>A type, factory or modifier name (renames.json, <c>types</c>, <c>factories</c>, <c>modifiers</c>).</summary>
internal sealed class NameEntry
{
    public string Reactor = "", Status = "";
    public string? Avalonia, Target, Note;

    /// <summary>
    /// A factory's object initializer, written onto each call it renames as <c>(Name(...) with { With })</c>, so the call
    /// keeps the Reactor factory's layout (ScrollView, #43). Its names are written in full: a using added for them could
    /// give an unported factory's name a meaning (Avalonia.Controls.Primitives has a Popup and a UniformGrid).
    /// </summary>
    public string? With;

    /// <summary>Only applied entries migrate: a proposed one is still Reactor's name in Isotope's code.</summary>
    public bool Applied => Status == "applied";
}

/// <summary>Isotope's rename table, <c>tools/naming/renames.json</c> (format: <c>tools/naming/README.md</c>).</summary>
internal sealed class RenameTable
{
    public Dictionary<string, string> Records = new(StringComparer.Ordinal);
    public List<MemberEntry> Members = [];
    public List<ParameterEntry> Parameters = [];
    public List<NameEntry> Modifiers = [], Types = [], Factories = [];

    public static RenameTable Parse(string json)
    {
        var root = Obj(Json.Parse(json), "renames.json");
        if (Num(root, "version") != 1) throw new FormatException("renames.json: version 1 expected.");
        var t = new RenameTable();
        foreach (var kv in Obj(root.TryGetValue("records", out var r) ? r : null, "records"))
            t.Records[kv.Key] = (string)kv.Value!;
        foreach (var m in Arr(root, "members").Select(o => Obj(o, "members")))
        {
            var avalonia = m["avalonia"] switch
            {
                string s => new[] { s },
                List<object?> list => list.Select(x => (string)x!).ToArray(),
                _ => throw new FormatException("renames.json: members[].avalonia is a string or an array."),
            };
            t.Members.Add(new MemberEntry
            {
                Record = Str(m, "record")!, Reactor = Str(m, "reactor")!, Avalonia = avalonia,
                Kind = Str(m, "kind")!, Note = Str(m, "note"),
            });
        }
        foreach (var p in Arr(root, "parameters").Select(o => Obj(o, "parameters")))
            t.Parameters.Add(new ParameterEntry
            {
                Factory = Str(p, "factory")!, Reactor = Str(p, "reactor")!, Avalonia = Str(p, "avalonia"), Note = Str(p, "note"),
                Position = p.TryGetValue("position", out var position) && position is double at ? (int)at : null,
            });
        t.Modifiers = Names(root, "modifiers");
        t.Types = Names(root, "types");
        t.Factories = Names(root, "factories");
        return t;
    }

    static List<NameEntry> Names(Dictionary<string, object?> root, string section) =>
        Arr(root, section).Select(o => Obj(o, section)).Select(e => new NameEntry
        {
            Reactor = Str(e, "reactor")!, Avalonia = Str(e, "avalonia"), Status = Str(e, "status")!,
            Target = Str(e, "target"), Note = Str(e, "note"), With = Str(e, "with"),
        }).ToList();

    internal static Dictionary<string, object?> Obj(object? o, string what) =>
        o as Dictionary<string, object?> ?? throw new FormatException(what + ": an object expected.");

    internal static List<object?> Arr(Dictionary<string, object?> o, string key) =>
        o.TryGetValue(key, out var v) && v is List<object?> list ? list : [];

    internal static string? Str(Dictionary<string, object?> o, string key) =>
        o.TryGetValue(key, out var v) ? v as string : null;

    static double Num(Dictionary<string, object?> o, string key) =>
        o.TryGetValue(key, out var v) && v is double d ? d : double.NaN;
}

/// <summary>A WinUI type and its Avalonia counterpart (winui-types.json).</summary>
internal sealed class WinUIType
{
    public string FullName = "", Namespace = "", Name = "";
    /// <summary>The Avalonia type's full name, or null when there is none (<see cref="Note"/> says what to use).</summary>
    public string? Avalonia;
    public string? Note;
    /// <summary>Members whose Avalonia name differs (<c>VirtualKey.Number0</c> is <c>Key.D0</c>); null: none.</summary>
    public Dictionary<string, string?> Members = new(StringComparer.Ordinal);

    public string? AvaloniaNamespace => Avalonia is null ? null : Avalonia.Substring(0, Math.Max(0, Avalonia.LastIndexOf('.')));
    public string? AvaloniaName => Avalonia is null ? null : Avalonia.Substring(Avalonia.LastIndexOf('.') + 1);
}

/// <summary>The WinUI to Avalonia type table (<c>Tables/winui-types.json</c>).</summary>
internal sealed class WinUITable
{
    /// <summary>The first segments of WinUI and WinRT namespaces (<c>Microsoft.UI</c>, <c>Windows</c>…).</summary>
    public List<string> Roots = [];
    /// <summary>Namespaces under a root that aren't WinUI (<c>Microsoft.UI.Reactor</c>).</summary>
    public List<string> Excluded = [];
    /// <summary>A WinUI namespace and the Avalonia namespace that holds most of its counterparts (for aliases).</summary>
    public Dictionary<string, string?> Namespaces = new(StringComparer.Ordinal);
    public List<WinUIType> Types = [];
    public Dictionary<string, WinUIType> ByFullName = new(StringComparer.Ordinal);
    public Dictionary<string, List<WinUIType>> ByName = new(StringComparer.Ordinal);

    public static WinUITable Parse(string json)
    {
        var root = RenameTable.Obj(Json.Parse(json), "winui-types.json");
        var t = new WinUITable
        {
            Roots = RenameTable.Arr(root, "roots").Select(x => (string)x!).ToList(),
            Excluded = RenameTable.Arr(root, "excluded").Select(x => (string)x!).ToList(),
        };
        foreach (var kv in RenameTable.Obj(root.TryGetValue("namespaces", out var n) ? n : null, "namespaces"))
            t.Namespaces[kv.Key] = kv.Value as string;
        foreach (var e in RenameTable.Arr(root, "types").Select(o => RenameTable.Obj(o, "types")))
        {
            var full = RenameTable.Str(e, "winui")!;
            var type = new WinUIType
            {
                FullName = full,
                Namespace = full.Substring(0, full.LastIndexOf('.')),
                Name = full.Substring(full.LastIndexOf('.') + 1),
                Avalonia = RenameTable.Str(e, "avalonia"),
                Note = RenameTable.Str(e, "note"),
            };
            if (e.TryGetValue("members", out var members) && members is Dictionary<string, object?> map)
                foreach (var kv in map) type.Members[kv.Key] = kv.Value as string;
            t.Types.Add(type);
            t.ByFullName[full] = type;
            if (!t.ByName.TryGetValue(type.Name, out var list)) t.ByName[type.Name] = list = [];
            list.Add(type);
        }
        return t;
    }

    /// <summary>Whether a dotted namespace (or a prefix of a qualified name) is WinUI's or WinRT's.</summary>
    public bool IsWinUINamespace(string dotted)
    {
        foreach (var ex in Excluded)
            if (dotted == ex || dotted.StartsWith(ex + ".", StringComparison.Ordinal)) return false;
        foreach (var root in Roots)
            if (dotted == root || dotted.StartsWith(root + ".", StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>A Reactor factory with no Isotope counterpart (<c>Tables/reactor-controls.json</c>).</summary>
internal sealed class ControlEntry
{
    public string Factory = "", Status = "";
    public string? Alternative;

    public bool WinUIOnly => Status == "winui-only";
}

/// <summary>Reactor's factories that Isotope doesn't have: WinUI-only controls and ones not ported yet.</summary>
internal sealed class ControlTable
{
    public Dictionary<string, ControlEntry> Factories = new(StringComparer.Ordinal);

    public static ControlTable Parse(string json)
    {
        var root = RenameTable.Obj(Json.Parse(json), "reactor-controls.json");
        var t = new ControlTable();
        foreach (var e in RenameTable.Arr(root, "factories").Select(o => RenameTable.Obj(o, "factories")))
        {
            var entry = new ControlEntry
            {
                Factory = RenameTable.Str(e, "factory")!, Status = RenameTable.Str(e, "status")!,
                Alternative = RenameTable.Str(e, "alternative"),
            };
            t.Factories[entry.Factory] = entry;
        }
        return t;
    }
}

/// <summary>The three tables the analyzer reads.</summary>
internal sealed class MigrationTables
{
    public RenameTable Renames;
    public WinUITable WinUI;
    public ControlTable Controls;

    public MigrationTables(RenameTable renames, WinUITable winui, ControlTable controls)
    {
        Renames = renames;
        WinUI = winui;
        Controls = controls;
    }

    static MigrationTables? _default;

    /// <summary>The tables embedded in this assembly.</summary>
    public static MigrationTables Default => _default ??= new MigrationTables(
        RenameTable.Parse(Resource("renames.json")),
        WinUITable.Parse(Resource("winui-types.json")),
        ControlTable.Parse(Resource("reactor-controls.json")));

    internal static string Resource(string name)
    {
        using var stream = typeof(MigrationTables).Assembly.GetManifestResourceStream("Isotope.UI.Migration." + name)
            ?? throw new InvalidOperationException("Missing embedded table " + name + ".");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
