using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>The embedded tables against the code they describe, and the code fix's edits.</summary>
public class TableAndFixTests
{
    [Fact]
    public void The_rename_table_is_read_whole()
    {
        // Counted with System.Text.Json, independently of the analyzer's reader.
        using var json = JsonDocument.Parse(MigrationTables.Resource("renames.json"));
        var root = json.RootElement;
        var table = MigrationTables.Default.Renames;
        Assert.Equal(root.GetProperty("records").EnumerateObject().Count(), table.Records.Count);
        Assert.Equal(root.GetProperty("members").GetArrayLength(), table.Members.Count);
        Assert.Equal(root.GetProperty("parameters").GetArrayLength(), table.Parameters.Count);
        Assert.Equal(root.GetProperty("types").GetArrayLength(), table.Types.Count);
        Assert.Equal(root.GetProperty("factories").GetArrayLength(), table.Factories.Count);
        Assert.Equal(root.GetProperty("modifiers").GetArrayLength(), table.Modifiers.Count);
        Assert.Contains(table.Members, m => m.Record == "GridElement" && m.Avalonia.SequenceEqual(["ColumnDefinitions", "RowDefinitions"]));
    }

    [Fact]
    public async Task Every_Avalonia_type_and_member_in_the_WinUI_table_exists()
    {
        var compilation = (await Migrate.Project("class C { }").GetCompilationAsync(TestContext.Current.CancellationToken))!;
        var missing = new List<string>();
        foreach (var type in MigrationTables.Default.WinUI.Types)
        {
            if (type.Avalonia is null)
            {
                if (string.IsNullOrEmpty(type.Note)) missing.Add(type.FullName + ": no counterpart and no note");
                continue;
            }
            var avalonia = compilation.GetTypeByMetadataName(type.Avalonia);
            if (avalonia is null) { missing.Add(type.Avalonia); continue; }
            // A combination of flags (Multiple | Toggle, #94) names each one.
            foreach (var member in type.Members.Values.OfType<string>().SelectMany(m => m.Split('|')).Select(m => m.Trim()))
                if (!HasMember(avalonia, member)) missing.Add(type.Avalonia + "." + member);
        }
        foreach (var ns in MigrationTables.Default.WinUI.Namespaces.Values.OfType<string>())
            if (FindNamespace(compilation, ns) is null) missing.Add(ns);
        Assert.Empty(missing);
    }

    [Fact]
    public void No_control_in_the_table_is_an_Isotope_factory_yet()
    {
        // When Isotope gains one of these factories, its entry goes.
        var factories = typeof(Isotope.UI.Factories).GetMethods().Select(m => m.Name).ToHashSet();
        var stale = MigrationTables.Default.Controls.Factories.Keys.Where(factories.Contains).ToList();
        Assert.Empty(stale);
        Assert.All(MigrationTables.Default.Controls.Factories.Values, c =>
        {
            Assert.Contains(c.Status, new[] { "winui-only", "not-ported" });
            Assert.False(string.IsNullOrEmpty(c.Alternative), c.Factory);
        });
    }

    [Fact]
    public void Fixes_keep_the_file_s_line_endings_and_add_usings_after_the_last_one()
    {
        const string reactor = "// header\r\nusing Microsoft.UI.Text;\r\nusing System;\r\n\r\nstatic class C\r\n{\r\n    static object W => FontWeights.Bold;\r\n}\r\n";
        var fixedText = Migrate.Fix(reactor);
        Assert.Equal("// header\r\nusing System;\r\nusing Avalonia.Media;\r\n\r\nstatic class C\r\n{\r\n    static object W => FontWeight.Bold;\r\n}\r\n", fixedText);
    }

    [Fact]
    public void A_file_with_no_usings_gets_them_at_the_top()
    {
        const string reactor = "static class C\n{\n    static object K => Windows.System.VirtualKey.A;\n    static object W => FontWeights.Bold;\n}\n";
        var fixedText = Migrate.Fix(reactor);
        Assert.Equal("using Avalonia.Media;\n\nstatic class C\n{\n    static object K => Avalonia.Input.Key.A;\n    static object W => FontWeight.Bold;\n}\n", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void Usings_go_after_extern_aliases_and_one_already_there_is_not_added_again()
    {
        const string withExtern = "extern alias Old;\nusing Microsoft.UI.Text;\n\nstatic class C\n{\n    static object W => FontWeights.Bold;\n}\n";
        Assert.StartsWith("extern alias Old;\nusing Avalonia.Media;\n\nstatic class C", Migrate.Fix(withExtern));

        const string withGlobalPrefix = "using global::Avalonia.Media;\nusing Microsoft.UI.Text;\n\nstatic class C\n{\n    static object W => FontWeights.Bold;\n}\n";
        Assert.Equal("using global::Avalonia.Media;\n\nstatic class C\n{\n    static object W => FontWeight.Bold;\n}\n", Migrate.Fix(withGlobalPrefix));
    }

    [Fact]
    public async Task The_code_fix_offers_one_action_per_diagnostic_with_the_same_edit_as_fix_all()
    {
        var ct = TestContext.Current.CancellationToken;
        const string reactor = "using Microsoft.UI.Reactor;\nclass C { }\n";
        var project = Migrate.Project(reactor);
        var diagnostic = Assert.Single(Migrate.Analyze(project));
        var document = project.Documents.Single();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), ct);
        await new MigrationCodeFix().RegisterCodeFixesAsync(context);
        var action = Assert.Single(actions);
        var operations = await action.GetOperationsAsync(ct);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        Assert.Equal("using Isotope.UI;\nclass C { }\n", (await changed.GetDocument(document.Id)!.GetTextAsync(ct)).ToString());
        Assert.NotNull(new MigrationCodeFix().GetFixAllProvider());
    }

    static bool HasMember(ITypeSymbol type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (!t.GetMembers(name).IsEmpty) return true;
        return false;
    }

    static INamespaceSymbol? FindNamespace(Compilation compilation, string dotted)
    {
        var current = compilation.GlobalNamespace;
        foreach (var part in dotted.Split('.'))
        {
            current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
            if (current is null) return null;
        }
        return current;
    }
}
