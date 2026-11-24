using System.Reflection;
using System.Text.Json;
using Isotope.UI.Core;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The public API has Avalonia's member names (#31), checked against the rename table, tools/naming/renames.json, which
/// the migration analyzer (#33) reads too. A record member is Avalonia-named when it is a property or event of the
/// record's Avalonia control, <c>On{Event}</c> for one of its events, or <c>On{Property}Changed</c> for one of its
/// properties; any other member needs a <c>kept</c> entry in the table, with the reason. Every entry must name what is
/// really there, so the table can't drift from the code.
/// </summary>
public class RenameTableTests
{
    private static readonly Assembly Library = typeof(Element).Assembly;

    [Fact]
    public void Every_record_member_has_an_Avalonia_name_or_a_kept_entry()
    {
        var table = RenameTable.Load();
        var problems = new List<string>();
        foreach (var (recordName, controlName) in table.Records)
        {
            var record = Record(table.IsotopeName(recordName));
            var control = Control(controlName);
            var kept = table.Members.Where(m => m.Record == recordName && m.Kind == "kept").Select(m => m.Avalonia[0]).ToHashSet();
            var renamedAway = table.Members.Where(m => m.Record == recordName && !m.Avalonia.Contains(m.Reactor)).Select(m => m.Reactor).ToHashSet();
            foreach (var member in Members(record))
            {
                if (renamedAway.Contains(member))
                    problems.Add($"{recordName}.{member}: Reactor's name, renamed in the table");
                else if (!IsAvaloniaName(member, control) && !kept.Contains(member))
                    problems.Add($"{recordName}.{member}: not a member of {controlName}, nor On{{Event}} or On{{Property}}Changed of one, nor kept in the table");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_descriptor_record_is_in_the_table_with_its_control()
    {
        var table = RenameTable.Load();
        var problems = new List<string>();
        var found = 0;
        foreach (var type in Library.GetExportedTypes())
        {
            var descriptor = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "GenerateReactorDescriptorAttribute");
            if (descriptor?.ConstructorArguments.FirstOrDefault().Value is not Type control) continue;
            found++;
            if (!table.Records.TryGetValue(table.ReactorName(type.Name), out var listed))
                problems.Add($"{type.Name} ({control.FullName}) is not in the table's records");
            else if (listed != control.FullName)
                problems.Add($"{type.Name}: the table says {listed}, the record's descriptor {control.FullName}");
        }
        Assert.True(found > 20, $"only {found} records carry [GenerateReactorDescriptor]: is the attribute still compiled in?");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_member_entry_names_a_record_and_the_member_it_became()
    {
        var table = RenameTable.Load();
        var problems = new List<string>();
        foreach (var entry in table.Members)
        {
            if (entry.Kind is not ("rename" or "shape" or "kept"))
                problems.Add($"{entry.Record}.{entry.Reactor}: kind '{entry.Kind}' is not rename, shape or kept");
            if (!table.Records.ContainsKey(entry.Record))
            {
                problems.Add($"{entry.Record}.{entry.Reactor}: {entry.Record} is not in the table's records");
                continue;
            }
            if (entry.Kind == "rename" && (entry.Avalonia.Length != 1 || entry.Avalonia[0] == entry.Reactor))
                problems.Add($"{entry.Record}.{entry.Reactor}: a rename names one other member");
            if (entry.Kind == "kept" && (entry.Avalonia.Length != 1 || entry.Avalonia[0] != entry.Reactor || entry.Note is null))
                problems.Add($"{entry.Record}.{entry.Reactor}: a kept member keeps its name and says why (note)");
            if (entry.Kind == "shape" && entry.Note is null)
                problems.Add($"{entry.Record}.{entry.Reactor}: a shape change says what changed (note)");
            var members = Members(Record(table.IsotopeName(entry.Record))).ToHashSet();
            foreach (var name in entry.Avalonia.Where(n => !members.Contains(n)))
                problems.Add($"{entry.Record}.{entry.Reactor}: {entry.Record} has no member {name}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Factory_parameters_have_the_Avalonia_names()
    {
        var table = RenameTable.Load();
        var factories = typeof(Factories).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var problems = new List<string>();
        foreach (var factory in factories)
        {
            var record = table.ReactorName(factory.ReturnType.Name);
            foreach (var parameter in factory.GetParameters())
            {
                var name = parameter.Name!;
                var pascal = char.ToUpperInvariant(name[0]) + name[1..];
                if (table.Members.Any(m => m.Record == record && m.Reactor == pascal && !m.Avalonia.Contains(pascal)))
                    problems.Add($"{factory.Name}({name}): Reactor's member name, renamed in the table");
                if (table.Parameters.Any(p => p.Factory == factory.Name && p.Reactor == name))
                    problems.Add($"{factory.Name}({name}): Reactor's parameter name, renamed in the table");
            }
        }
        foreach (var entry in table.Parameters)
        {
            var overloads = factories.Where(f => f.Name == entry.Factory).ToList();
            if (overloads.Count == 0)
                problems.Add($"parameters: no factory {entry.Factory}");
            else if (!overloads.Any(f => f.GetParameters().Any(p => p.Name == entry.Avalonia)))
                problems.Add($"parameters: no {entry.Factory} has a parameter {entry.Avalonia}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Type_factory_and_modifier_entries_match_their_status()
    {
        // An applied entry is in the code under its Avalonia name only; a proposed one is still under Reactor's, so
        // applying it later is a rename in the code and a status change here. A kept one is in the code under Reactor's
        // name, with the reason: no Avalonia counterpart (#44), or Avalonia's name is taken or set aside (#46).
        var table = RenameTable.Load();
        var types = Library.GetExportedTypes().Select(t => t.Name).ToHashSet();
        var factories = typeof(Factories).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        var modifiers = typeof(ElementExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        var problems = new List<string>();
        Check("types", table.Types, types);
        Check("factories", table.Factories, factories);
        Check("modifiers", table.Modifiers, modifiers);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        void Check(string section, IEnumerable<RenameTable.Rename> entries, HashSet<string> present)
        {
            foreach (var e in entries)
            {
                if (e.Avalonia is null)
                {
                    if (e.Status != "applied" || present.Contains(e.Reactor) || e.Note is null)
                        problems.Add($"{section}: {e.Reactor} is removed, so it is applied, gone from the code, and says what replaces it (note)");
                }
                else if (e.Status == "applied" && (!present.Contains(e.Avalonia) || present.Contains(e.Reactor)))
                    problems.Add($"{section}: {e.Reactor} -> {e.Avalonia} is applied, so only {e.Avalonia} exists");
                else if (e.Status == "proposed" && !present.Contains(e.Reactor))
                    problems.Add($"{section}: {e.Reactor} -> {e.Avalonia} is proposed, so {e.Reactor} exists");
                else if (e.Status == "kept" && (e.Avalonia != e.Reactor || !present.Contains(e.Reactor) || e.Note is null))
                    problems.Add($"{section}: {e.Reactor} is kept, so it keeps its name, exists, and says why (note)");
                else if (e.Status is not ("applied" or "proposed" or "kept"))
                    problems.Add($"{section}: {e.Reactor}: status '{e.Status}' is not applied, proposed or kept");
            }
        }
    }

    [Fact]
    public void Every_record_is_named_after_its_Avalonia_control_or_kept()
    {
        // Types follow Avalonia too (#46): a record is {Control}Element, or {Control}Data for an item's data (TabItemData),
        // unless a kept entry says why it isn't. A proposed rename fails here, since the code still has Reactor's name.
        var table = RenameTable.Load();
        var problems = new List<string>();
        foreach (var (recordName, controlName) in table.Records)
        {
            var isotope = table.IsotopeName(recordName);
            var control = ShortName(controlName);
            if (isotope == control + "Element" || isotope == control + "Data") continue;
            if (!table.Types.Any(t => t.Reactor == recordName && t.Status == "kept" && t.Note is not null))
                problems.Add($"{isotope}: not {control}Element or {control}Data, and no kept type entry for {recordName} says why");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_factory_of_a_record_is_named_after_its_control_or_kept()
    {
        // A factory that builds a record is named after the record's Avalonia control (TabControl, TabItem), unless a
        // kept entry says why it isn't (VStack and HStack, the named text styles).
        var table = RenameTable.Load();
        var problems = new List<string>();
        var factories = typeof(Factories).GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var factory in factories.DistinctBy(f => (f.Name, f.ReturnType)))
        {
            if (!table.Records.TryGetValue(table.ReactorName(factory.ReturnType.Name), out var controlName)) continue;
            var control = ShortName(controlName);
            if (factory.Name == control) continue;
            if (!table.Factories.Any(f => f.Reactor == factory.Name && f.Status == "kept" && f.Note is not null))
                problems.Add($"{factory.Name}: builds a {factory.ReturnType.Name}, so it is {control}, or a kept factory entry says why it isn't");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Applied_modifier_renames_are_properties_of_their_controls()
    {
        // A renamed modifier takes the name of the Avalonia property it writes: on its target's control, or on every
        // control (HorizontalAlignment, ContextFlyout).
        var table = RenameTable.Load();
        var problems = new List<string>();
        foreach (var m in table.Modifiers.Where(m => m.Status == "applied" && m.Avalonia is not null))
        {
            var control = m.Target is { } target ? Control(table.Records[target]) : typeof(global::Avalonia.Controls.Control);
            if (control.GetProperty(m.Avalonia!, BindingFlags.Public | BindingFlags.Instance) is null)
                problems.Add($"modifiers: {m.Reactor} -> {m.Avalonia}, which is not a property of {control.FullName}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

    [Fact]
    public void The_naming_rule_reads_events_properties_and_callbacks()
    {
        // The rule the other tests apply, on a control whose members are known.
        var button = typeof(global::Avalonia.Controls.Button);
        Assert.True(IsAvaloniaName("Content", button));
        Assert.True(IsAvaloniaName("Click", button));
        Assert.True(IsAvaloniaName("OnClick", button));
        Assert.True(IsAvaloniaName("OnIsEnabledChanged", button));
        Assert.False(IsAvaloniaName("Label", button));
        Assert.False(IsAvaloniaName("OnChanged", button));
        Assert.False(IsAvaloniaName("OnLabelChanged", button));
        Assert.Equal(["Content", "OnClick"], Members(typeof(TestRecord)).Order(StringComparer.Ordinal));
    }

    private sealed record TestRecord(string Content, Action? OnClick = null) : Element
    {
        internal int Hidden { get; init; }
    }

    /// <summary>Whether <paramref name="name"/> is Avalonia's name for a member of <paramref name="control"/>.</summary>
    internal static bool IsAvaloniaName(string name, Type control)
    {
        var properties = control.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        var events = control.GetEvents(BindingFlags.Public | BindingFlags.Instance).Select(e => e.Name).ToHashSet();
        if (properties.Contains(name) || events.Contains(name)) return true;
        if (name.Length > 2 && name.StartsWith("On", StringComparison.Ordinal))
        {
            var rest = name[2..];
            if (events.Contains(rest)) return true;
            if (rest.EndsWith("Changed", StringComparison.Ordinal) && properties.Contains(rest[..^"Changed".Length])) return true;
        }
        return false;
    }

    /// <summary>A record's public instance members, without the ones every element has.</summary>
    internal static IEnumerable<string> Members(Type record) =>
        record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType is { } d && !d.IsAssignableFrom(typeof(Element)))
            .Select(p => p.Name)
            .Distinct();

    private static Type Record(string name) =>
        Library.GetExportedTypes().SingleOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException($"no public type {name} in {Library.GetName().Name}");

    private static Type Control(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t is not null)
        ?? throw new InvalidOperationException($"no type {fullName} loaded");
}

/// <summary>tools/naming/renames.json, as the tests read it (the format is in tools/naming/README.md).</summary>
internal sealed record RenameTable(
    Dictionary<string, string> Records,
    List<RenameTable.Member> Members,
    List<RenameTable.Parameter> Parameters,
    List<RenameTable.Rename> Modifiers,
    List<RenameTable.Rename> Types,
    List<RenameTable.Rename> Factories)
{
    internal sealed record Member(string Record, string Reactor, string[] Avalonia, string Kind, string? Note);

    internal sealed record Parameter(string Factory, string Reactor, string Avalonia);

    /// <summary>
    /// The library's name for a Reactor record: the Avalonia name an applied type rename gives it (TreeViewNodeData is
    /// TreeViewItemData, #43), else the same. Records and member entries stay under Reactor's name, as the analyzer reads
    /// them.
    /// </summary>
    internal string IsotopeName(string reactor) =>
        Types.FirstOrDefault(t => t.Status == "applied" && t.Reactor == reactor && t.Avalonia is not null)?.Avalonia ?? reactor;

    /// <summary>
    /// Reactor's name for a library type: its own when the table lists it, else the one an applied type rename gave its
    /// Avalonia name.
    /// </summary>
    internal string ReactorName(string isotope) =>
        Records.ContainsKey(isotope) ? isotope // a Reactor record too (ScrollViewerElement), as the analyzer reads it first
            : Types.FirstOrDefault(t => t.Status == "applied" && t.Avalonia == isotope)?.Reactor ?? isotope;

    /// <summary>A rename, or a removal when <see cref="Avalonia"/> is null (then <see cref="Note"/> says what replaces it).</summary>
    internal sealed record Rename(string Reactor, string? Avalonia, string Status, string? Note, string? Target = null);

    internal static RenameTable Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path()));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        return new RenameTable(
            root.GetProperty("records").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
            root.GetProperty("members").EnumerateArray().Select(m => new Member(
                m.GetProperty("record").GetString()!,
                m.GetProperty("reactor").GetString()!,
                m.GetProperty("avalonia").ValueKind == JsonValueKind.Array
                    ? m.GetProperty("avalonia").EnumerateArray().Select(a => a.GetString()!).ToArray()
                    : [m.GetProperty("avalonia").GetString()!],
                m.GetProperty("kind").GetString()!,
                m.TryGetProperty("note", out var note) ? note.GetString() : null)).ToList(),
            root.GetProperty("parameters").EnumerateArray().Select(p => new Parameter(
                p.GetProperty("factory").GetString()!, p.GetProperty("reactor").GetString()!, p.GetProperty("avalonia").GetString()!)).ToList(),
            Renames(root, "modifiers"),
            Renames(root, "types"),
            Renames(root, "factories"));
    }

    private static List<Rename> Renames(JsonElement root, string section) =>
        root.GetProperty(section).EnumerateArray().Select(e => new Rename(
            e.GetProperty("reactor").GetString()!,
            e.GetProperty("avalonia").GetString(),
            e.TryGetProperty("status", out var status) ? status.GetString()! : "applied",
            e.TryGetProperty("note", out var note) ? note.GetString() : null,
            e.TryGetProperty("target", out var target) ? target.GetString() : null)).ToList();

    private static string Path()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "UPSTREAM.lock")))
                return System.IO.Path.Combine(dir.FullName, "tools", "naming", "renames.json");
        throw new InvalidOperationException("no UPSTREAM.lock above " + AppContext.BaseDirectory);
    }
}
