using System.Collections.Immutable;
using Isotope.UI.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Isotope.UI.Mcp;

/// <summary>The migration's result: the code after its fixes, and what is left for a hand fix.</summary>
internal sealed record MigrationResult(string Code, bool Changed, IReadOnlyList<Diagnostic> Left, IReadOnlyList<Diagnostic> Errors);

/// <summary>
/// Runs the migration analyzer and its code fixes (<c>Isotope.UI.Migration</c>) on one C# file in memory, as
/// <c>dotnet format analyzers</c> does: Fix All on the document, repeated until nothing changes. The file is compiled
/// against this process's own assemblies, the ones the tool ships with: the library, the desktop startup and Avalonia.
/// </summary>
internal static class CodeMigration
{
    /// <summary>The longest file migrate_code takes.</summary>
    public const int MaxLength = 200_000;

    const int MaxPasses = 5;

    static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        _ = typeof(Factories).Assembly;
        _ = typeof(ReactorAppDesktop).Assembly;
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. tpa.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetFileName(p).StartsWith("Isotope.UI.Migration", StringComparison.Ordinal)
                && !Path.GetFileName(p).StartsWith("Isotope.UI.Mcp", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    public static async Task<MigrationResult> RunAsync(string code, CancellationToken ct = default)
    {
        var project = Project(code);
        var before = code;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var document = project.Documents.Single();
            var fixable = (await AnalyzeAsync(project, ct)).Where(d => Rules.Fixable.Contains(d.Id)).ToList();
            if (fixable.Count == 0) break;
            var after = await MigrationCodeFix.ApplyAsync(document, fixable, ct);
            var text = (await after.GetTextAsync(ct)).ToString();
            if (text == (await document.GetTextAsync(ct)).ToString()) break;
            project = after.Project;
        }
        var final = project.Documents.Single();
        var result = (await final.GetTextAsync(ct)).ToString();
        var left = await AnalyzeAsync(project, ct);
        var compilation = (await project.GetCompilationAsync(ct))!;
        var errors = compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error)
            .OrderBy(d => d.Location.SourceSpan.Start).ToList();
        return new MigrationResult(result, result != before, left, errors);
    }

    static Project Project(string code)
    {
        var workspace = new AdhocWorkspace();
        var exe = CSharpSyntaxTree.ParseText(code, Parse).GetRoot().ChildNodes().OfType<GlobalStatementSyntax>().Any();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Snippet", "Snippet",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: Parse,
            metadataReferences: References.Value));
        return project.AddDocument("Snippet.cs", SourceText.From(code)).Project;
    }

    static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(Project project, CancellationToken ct)
    {
        var compilation = (await project.GetCompilationAsync(ct))!;
        var diagnostics = await compilation.WithAnalyzers([new MigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(ct);
        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start)];
    }

    /// <summary>"ISOMIG012 (warning, line 3, column 9): message".</summary>
    public static string Describe(Diagnostic d)
    {
        var where = d.Location.IsInSource ? d.Location.GetLineSpan().StartLinePosition : default;
        return $"{d.Id} ({d.Severity.ToString().ToLowerInvariant()}, line {where.Line + 1}, column {where.Character + 1}): {d.GetMessage()}";
    }
}
