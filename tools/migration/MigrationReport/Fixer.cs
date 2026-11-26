// The migration's code fixes, applied in this process (#87): what one `dotnet format analyzers <project> --no-restore
// --severity warn --diagnostics <ids>` run does, without starting dotnet format. It follows the SDK's dotnet format
// (src/BuiltInTools/dotnet-format in dotnet/sdk: CodeFormatter, AnalyzerFormatter, AnalyzerRunner, SolutionCodeFixApplier):
//   - the files: the project's own documents that exist on disk, not generated code (by file name, a leading comment
//     marking it generated, or generated_code = true in .editorconfig); referenced projects are loaded but not fixed;
//   - the diagnostics: the migration analyzer's, not suppressed, warning or above, in those files, of the given IDs;
//   - the fixes: for each ID reported, the project is analysed again and the fix's Fix All runs in Solution scope, on the
//     diagnostics of that ID, through its FixAllProvider and ApplyChangesOperation; then the changes are saved.
// The IDs run in ordinal order; dotnet format takes them in a hash set's order, which changes from run to run.
// The workspace stays open between the passes of one project (Pass), so only the first pass loads it.
using System.Collections.Immutable;
using Isotope.UI.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

/// <summary>The fixes of one project: each call to <see cref="PassAsync"/> is one dotnet format run.</summary>
sealed class Fixer : IDisposable
{
    static readonly MigrationAnalyzer Analyzer = new();
    static readonly MigrationCodeFix Fix = new();

    readonly ImmutableHashSet<string> _ids;
    MSBuildWorkspace? _workspace;
    TextWriter _log = TextWriter.Null;

    public Fixer(string project, IEnumerable<string> ids)
    {
        Project = Path.GetFullPath(project);
        _ids = ids.ToImmutableHashSet(StringComparer.Ordinal);
    }

    public string Project { get; }

    /// <summary>Whether this is the fixer for the project and IDs (so its open workspace can serve another pass).</summary>
    public bool Fixes(string project, IEnumerable<string> ids) => Project == Path.GetFullPath(project) && _ids.SetEquals(ids);

    /// <summary>
    /// One pass: analyses the project, applies the fixes and saves the files. False when dotnet format would exit with an
    /// error: the project can't be opened, the changes can't be saved, or something else throws. As there, a fix that
    /// throws, or references that didn't load, are warnings: the pass fixes less and still succeeds. Warnings and errors
    /// go to log.
    /// </summary>
    public async Task<bool> PassAsync(TextWriter log, CancellationToken ct = default)
    {
        _log = log;
        try
        {
            if (_workspace is null)
            {
                // dotnet format's workspace (MSBuildWorkspaceLoader).
                _workspace = MSBuildWorkspace.Create(new Dictionary<string, string>(StringComparer.Ordinal) { ["AlwaysCompileMarkupFilesInSeparateDomain"] = bool.FalseString });
                await _workspace.OpenProjectAsync(Project, cancellationToken: ct).ConfigureAwait(false);
                // Warnings, as in dotnet format: a project that loads in part is still fixed.
                foreach (var d in _workspace.Diagnostics)
                    log.WriteLine($"fix: {Path.GetFileName(Project)}: loading the workspace: {d.Kind}: {d.Message}");
            }
            var original = _workspace.CurrentSolution;
            var fixedSolution = await FixAsync(original, ct).ConfigureAwait(false);
            if (_workspace.TryApplyChanges(fixedSolution)) return true;
            _log.WriteLine($"fix: {Path.GetFileName(Project)}: the changes could not be saved");
        }
        catch (Exception e)
        {
            _log.WriteLine($"fix: {Path.GetFileName(Project)}: {e.GetType().Name}: {e.Message}");
        }
        // The next pass loads the project again, as the next dotnet format run would.
        Dispose();
        return false;
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    async Task<Solution> FixAsync(Solution solution, CancellationToken ct)
    {
        // A project opened as the workspace: only it is formatted (dotnet format compares the paths ordinally).
        var project = solution.Projects.FirstOrDefault(p => p.FilePath == Project)
            ?? throw new InvalidOperationException("the workspace has no project " + Path.GetFileName(Project));
        var paths = await FormattablePathsAsync(project, ct).ConfigureAwait(false);
        if (!project.Documents.Any(d => d.FilePath is not null && paths.Contains(d.FilePath))) return solution;

        // The IDs reported (AnalyzerFormatter.GetProjectDiagnosticsAsync), then each fixed in turn (FixDiagnosticsAsync).
        // An analysis is reused while the solution is the one it analysed: the analyzer gives the same diagnostics.
        var analysed = solution;
        var diagnostics = await AnalyzeAsync(project, paths, ct).ConfigureAwait(false);
        var reported = diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var id in reported)
        {
            if (!Fix.FixableDiagnosticIds.Contains(id))
            {
                _log.WriteLine($"fix: unable to fix {id}: no associated code fix");
                continue;
            }
            project = solution.GetProject(project.Id)!;
            if (!ReferenceEquals(analysed, solution))
            {
                diagnostics = await AnalyzeAsync(project, paths, ct).ConfigureAwait(false);
                analysed = solution;
            }
            if (diagnostics.IsEmpty) continue;
            var changed = await ApplyAsync(solution, project, diagnostics, id, ct).ConfigureAwait(false);
            if (changed.GetChanges(solution).GetProjectChanges().Any(p => p.GetChangedDocuments().Any() || p.GetChangedAdditionalDocuments().Any()))
                solution = changed;
        }
        return solution;
    }

    /// <summary>The paths dotnet format formats in the project (CodeFormatter.DetermineFormattableFilesAsync).</summary>
    static async Task<ImmutableHashSet<string>> FormattablePathsAsync(Project project, CancellationToken ct)
    {
        var added = new HashSet<string>();
        var covered = new List<string>();
        var notCovered = new List<string>();
        foreach (var document in project.Documents)
        {
            // A file already added (a link) counts once.
            if (document.FilePath is null || !added.Add(document.FilePath)) continue;
            if (!File.Exists(document.FilePath) || !document.SupportsSyntaxTree) continue;
            var tree = (await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false))!;
            if (IsGeneratedCode(tree, ct)) continue;
            // As in dotnet format: the provider gives empty options to a file no .editorconfig covers, so in practice every
            // file counts as covered.
            var options = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree);
            if (options is null) notCovered.Add(document.FilePath);
            else if (!(options.TryGetValue("generated_code", out var value) && bool.TryParse(value, out var generated) && generated))
                covered.Add(document.FilePath);
        }
        // Files an .editorconfig covers, or all when none is covered.
        return (covered.Count == 0 ? notCovered : covered).ToImmutableHashSet();
    }

    /// <summary>Generated code by its file name or a leading auto-generated comment (dotnet format's GeneratedCodeUtilities).</summary>
    internal static bool IsGeneratedCode(SyntaxTree tree, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(tree.FilePath))
        {
            var name = Path.GetFileName(tree.FilePath);
            if (name.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                var stem = Path.GetFileNameWithoutExtension(tree.FilePath);
                if (stem.EndsWith(".designer", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".generated", StringComparison.OrdinalIgnoreCase)
                    || stem.EndsWith(".g", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".g.i", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        var root = tree.GetRoot(ct);
        if (!root.HasLeadingTrivia) return false;
        return root.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .Select(t => t.ToString())
            .Any(text => text.Contains("<autogenerated", StringComparison.Ordinal) || text.Contains("<auto-generated", StringComparison.Ordinal));
    }

    /// <summary>The migration diagnostics dotnet format would fix in the project (AnalyzerRunner.RunCodeAnalysisAsync).</summary>
    async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Project project, ImmutableHashSet<string> paths, CancellationToken ct)
    {
        if (!AllReferencedProjectsLoaded(project))
        {
            _log.WriteLine($"fix: required references did not load for {project.Name} or a referenced project");
            return [];
        }
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null) return [];
        var options = new CompilationWithAnalyzersOptions(project.AnalyzerOptions, onAnalyzerException: null, concurrentAnalysis: true,
            logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
        var diagnostics = await compilation.WithAnalyzers([Analyzer], options).GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
        return [.. diagnostics.Where(d => !d.IsSuppressed && d.Severity >= DiagnosticSeverity.Warning && d.Location.IsInSource
            && d.Location.SourceTree is { } tree && paths.Contains(tree.FilePath) && _ids.Contains(d.Id))];

        static bool AllReferencedProjectsLoaded(Project project) =>
            // mscorlib stands for the runtime's references.
            project.MetadataReferences.Any(r => r.Display?.EndsWith("mscorlib.dll") == true)
            && project.ProjectReferences.Select(r => project.Solution.GetProject(r.ProjectId)).All(p => p is not null && AllReferencedProjectsLoaded(p));
    }

    /// <summary>Fix All in Solution scope for one ID (SolutionCodeFixApplier.ApplyCodeFixesAsync).</summary>
    async Task<Solution> ApplyAsync(Solution solution, Project project, ImmutableArray<Diagnostic> diagnostics, string id, CancellationToken ct)
    {
        try
        {
            var fixAll = Fix.GetFixAllProvider();
            if (fixAll?.GetSupportedFixAllScopes()?.Contains(FixAllScope.Solution) != true) return solution;
            // The first diagnostic of any ID starts the Fix All, as in dotnet format; FixAllContext keeps only the ID's own.
            var diagnostic = diagnostics.FirstOrDefault(d => d.Location.SourceTree is not null);
            if (diagnostic is null || solution.GetDocument(diagnostic.Location.SourceTree) is not { } document) return solution;
            CodeAction? action = null;
            await Fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => action ??= a, ct)).ConfigureAwait(false);
            var context = new FixAllContext(document, Fix, FixAllScope.Solution, action?.EquivalenceKey!, [id], new Provider(project, diagnostics), ct);
            if (await fixAll.GetFixAsync(context).ConfigureAwait(false) is not { } fixAllAction)
            {
                _log.WriteLine($"fix: unable to fix {id}: no Fix All action");
                return solution;
            }
            var operations = await fixAllAction.GetOperationsAsync(ct).ConfigureAwait(false);
            if (operations.OfType<ApplyChangesOperation>().SingleOrDefault() is not { } apply)
            {
                _log.WriteLine($"fix: unable to fix {id}: an unexpected operation");
                return solution;
            }
            return apply.ChangedSolution;
        }
        catch (Exception e)
        {
            _log.WriteLine($"fix: failed to apply the fix for {id}: {e.Message}");
            return solution;
        }
    }

    /// <summary>The analysis's diagnostics for Fix All (SolutionCodeFixApplier.DiagnosticProvider).</summary>
    sealed class Provider(Project project, ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project p, CancellationToken ct) => GetProjectDiagnosticsAsync(p, ct);

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken ct)
        {
            var all = await GetProjectDiagnosticsAsync(document.Project, ct).ConfigureAwait(false);
            return all.Where(d => d.Location.SourceTree?.FilePath == document.FilePath).ToImmutableArray();
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project p, CancellationToken ct) =>
            Task.FromResult<IEnumerable<Diagnostic>>(p == project ? diagnostics : []);
    }
}
