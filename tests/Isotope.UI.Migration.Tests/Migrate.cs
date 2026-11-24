using System.Collections.Immutable;
using Isotope.UI.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Isotope.UI.Migration.Tests;

/// <summary>
/// Runs the analyzer and the code fix on source text, compiled against the library, its desktop startup and Avalonia
/// (this process's own assemblies), as `dotnet format analyzers` does: Fix All per document, repeated until nothing
/// changes.
/// </summary>
internal static class Migrate
{
    static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        // Load the library, the desktop startup and Avalonia, so they are among the process's assemblies.
        _ = typeof(Isotope.UI.Factories).Assembly;
        _ = typeof(Isotope.UI.ReactorAppDesktop).Assembly;
        _ = typeof(Avalonia.Controls.Button).Assembly;
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. tpa.Where(p => !Path.GetFileName(p).StartsWith("Isotope.UI.Migration", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    /// <summary>A project holding the sources (file names a.cs, b.cs…), with or without top-level statements.</summary>
    public static Project Project(params string[] sources)
    {
        var workspace = new AdhocWorkspace();
        var exe = sources.Any(s => CSharpSyntaxTree.ParseText(s, Parse).GetRoot().ChildNodes().OfType<GlobalStatementSyntax>().Any());
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Sample", "Sample",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: Parse,
            metadataReferences: References.Value));
        for (var i = 0; i < sources.Length; i++)
            project = project.AddDocument((char)('a' + i) + ".cs", SourceText.From(sources[i])).Project;
        return project;
    }

    /// <summary>The migration's diagnostics on the sources, in source order.</summary>
    public static ImmutableArray<Diagnostic> Analyze(string source, MigrationTables? tables = null) => Analyze(Project(source), tables);

    public static ImmutableArray<Diagnostic> Analyze(Project project, MigrationTables? tables = null)
    {
        var compilation = project.GetCompilationAsync().Result!;
        var analyzer = tables is null ? new MigrationAnalyzer() : new MigrationAnalyzer(tables);
        var diagnostics = compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().Result;
        return [.. diagnostics.OrderBy(d => d.Location.SourceTree?.FilePath).ThenBy(d => d.Location.SourceSpan.Start)];
    }

    /// <summary>The sources after the fixes, applied per document until nothing changes (at most 5 passes).</summary>
    public static string[] Fix(string[] sources, MigrationTables? tables = null)
    {
        var project = Project(sources);
        for (var pass = 0; pass < 5; pass++)
        {
            var fixable = Analyze(project, tables).Where(d => Rules.Fixable.Contains(d.Id)).ToList();
            var changed = false;
            foreach (var group in fixable.GroupBy(d => d.Location.SourceTree!.FilePath))
            {
                var document = project.Documents.Single(d => d.Name == group.Key);
                var before = document.GetTextAsync().Result.ToString();
                var after = MigrationCodeFix.ApplyAsync(document, group, CancellationToken.None).Result;
                var text = after.GetTextAsync().Result.ToString();
                if (text == before) continue;
                project = after.Project;
                changed = true;
            }
            if (!changed) break;
        }
        return [.. project.Documents.OrderBy(d => d.Name).Select(d => d.GetTextAsync().Result.ToString())];
    }

    public static string Fix(string source, MigrationTables? tables = null) => Fix([source], tables)[0];

    /// <summary>The compiler's errors for the sources.</summary>
    public static ImmutableArray<Diagnostic> Errors(params string[] sources) =>
        [.. Project(sources).GetCompilationAsync().Result!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>"ID: message" for each diagnostic, to compare in one assertion.</summary>
    public static string[] Describe(this IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(d => d.Id + ": " + d.GetMessage())];

    /// <summary>The tables with renames.json's entries edited, for rules switched by the table.</summary>
    public static MigrationTables WithRenames(Action<RenameTable> edit)
    {
        var renames = RenameTable.Parse(MigrationTables.Resource("renames.json"));
        edit(renames);
        return new MigrationTables(renames, MigrationTables.Default.WinUI, MigrationTables.Default.Controls);
    }
}
