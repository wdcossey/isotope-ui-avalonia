// Lists the migration's diagnostics that are left in a project, with compile errors, after `dotnet format analyzers`
// (#33). A build stops before analyzers when a file has declaration errors (an unresolved using), so this compiles the
// project's inputs itself and runs the analyzer, as dotnet format does.
//
//   dotnet run --project tools/migration/MigrationReport -- <project.csproj> [--json]
//
// Default: one line per migration diagnostic (file(line,col): ID message), then the number of compile errors per
// file. --json: per source file under the project (relative path), its compile errors and migration diagnostics by
// ID, and the commonest messages (tools/migration/measure-samples.mjs reads it).
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Isotope.UI.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--json"))
{
    Console.Error.WriteLine("usage: MigrationReport <project.csproj> [--json]");
    return 2;
}
var project = Path.GetFullPath(args[0]);
var asJson = args.Length == 2;
// The project's compile inputs, after a build of the project itself (its project references aren't built: one that
// fails would leave no references at all).
var msbuild = new ProcessStartInfo("dotnet")
{
    WorkingDirectory = Path.GetDirectoryName(project)!,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
foreach (var a in new[] { "msbuild", project, "-nologo", "-v:q", "-restore", "-t:Build", "-p:BuildProjectReferences=false",
             "-getItem:Compile", "-getItem:ReferencePathWithRefAssemblies", "-getItem:Analyzer", "-getProperty:LangVersion", "-getProperty:Nullable",
             "-getProperty:OutputType", "-getProperty:DefineConstants", "-getProperty:AllowUnsafeBlocks", "-getProperty:AssemblyName" })
    msbuild.ArgumentList.Add(a);
using var process = Process.Start(msbuild)!;
var stderr = process.StandardError.ReadToEndAsync();
var text = await process.StandardOutput.ReadToEndAsync();
await process.WaitForExitAsync();
await stderr;
// msbuild prints the build's own output first; the JSON is the last top-level object.
var start = text.LastIndexOf("\n{", StringComparison.Ordinal);
var json = JsonNode.Parse(start >= 0 ? text[(start + 1)..] : text)!;
var props = json["Properties"]!;
var items = json["Items"]!;
string Prop(string name) => props[name]?.GetValue<string>() ?? "";
IEnumerable<string> Paths(string item) => items[item]!.AsArray().Select(i => i!["FullPath"]!.GetValue<string>());

var projectDir = Path.GetDirectoryName(project)!;
var languageVersion = LanguageVersionFacts.TryParse(Prop("LangVersion"), out var lv) ? lv : LanguageVersion.Default;
var parse = new CSharpParseOptions(languageVersion, preprocessorSymbols: Prop("DefineConstants").Split(';', StringSplitOptions.RemoveEmptyEntries));
var trees = Paths("Compile").Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parse, path: p)).ToList();
// A referenced sample project that doesn't build has no output: its types are then missing, as in a real build.
var references = Paths("ReferencePathWithRefAssemblies").Where(File.Exists).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
var options = new CSharpCompilationOptions(
    Prop("OutputType") is "Exe" or "WinExe" ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
    nullableContextOptions: Prop("Nullable") == "enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable,
    allowUnsafe: Prop("AllowUnsafeBlocks") == "true");
Compilation compilation = CSharpCompilation.Create(Prop("AssemblyName"), trees, references, options);
// The project's source generators (System.Text.Json's, LibraryImport…), as a build runs them; not the migration analyzer.
var loader = new Loader();
var generators = new List<ISourceGenerator>();
foreach (var path in Paths("Analyzer").Where(File.Exists).Where(p => !Path.GetFileName(p).StartsWith("Isotope.UI.Migration", StringComparison.Ordinal)))
{
    try { generators.AddRange(new AnalyzerFileReference(path, loader).GetGenerators(LanguageNames.CSharp)); }
    catch (Exception e) { Console.Error.WriteLine($"generator not loaded: {Path.GetFileName(path)}: {e.GetType().Name}"); }
}
CSharpGeneratorDriver.Create(generators, parseOptions: parse).RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _);
var sourceTrees = new HashSet<SyntaxTree>(trees);
// The analyzer reports nothing without Isotope among the references (an unbuilt reference has no output to read).
if (!references.Any(r => Path.GetFileName(r.Display ?? "") == "Isotope.UI.Avalonia.dll"))
{
    Console.Error.WriteLine("Isotope.UI.Avalonia.dll isn't among the project's built references: build the project (dotnet build) first.");
    return 3;
}
var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
var migration = await compilation.WithAnalyzers([new MigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

// Only the project's own files count: not obj/, not generated code, not other projects.
string? Relative(Diagnostic d) => d.Location.SourceTree is { } t && sourceTrees.Contains(t) ? Relative2(t.FilePath) : null;
string? Relative2(string path)
{
    var rel = Path.GetRelativePath(projectDir, path).Replace('\\', '/');
    return rel.StartsWith("..", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal) ? null : rel;
}
static JsonObject Count(IEnumerable<Diagnostic> ds) =>
    new(ds.GroupBy(d => d.Id).OrderBy(g => g.Key).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count())));

var files = new JsonObject();
foreach (var tree in trees)
{
    if (Relative2(tree.FilePath) is not { } rel) continue;
    files[rel] = new JsonObject
    {
        ["errors"] = Count(errors.Where(d => d.Location.SourceTree == tree)),
        ["migration"] = Count(migration.Where(d => d.Location.SourceTree == tree)),
    };
}
// Messages by count, for the summary's "what is left" lists: migration diagnostics, and compile errors by ID and the
// name they are about (the message's first quoted name).
static JsonObject Messages(IEnumerable<string> messages) =>
    new(messages.GroupBy(m => m).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count())));
static string ErrorKey(Diagnostic d)
{
    var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
    var quoted = System.Text.RegularExpressions.Regex.Match(message, "'([^']+)'");
    return d.Id + " " + (quoted.Success ? quoted.Groups[1].Value : message);
}
var underProject = errors.Where(d => Relative(d) is not null).ToList();
var result = new JsonObject
{
    ["files"] = files,
    // Whether MSBuild built the project (its references already built).
    ["builds"] = process.ExitCode == 0,
    // Errors with no file under the project (a missing reference, a missing Main).
    ["otherErrors"] = Count(errors.Where(d => Relative(d) is null)),
    ["migrationMessages"] = Messages(migration.Where(d => Relative(d) is not null).Select(d => d.Id + ": " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))),
    ["errorMessages"] = Messages(underProject.Select(ErrorKey)),
};
if (asJson)
{
    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    return 0;
}
foreach (var d in migration.Where(d => Relative(d) is not null).OrderBy(d => d.Location.SourceTree!.FilePath, StringComparer.Ordinal).ThenBy(d => d.Location.SourceSpan.Start))
{
    var pos = d.Location.GetLineSpan().StartLinePosition;
    Console.WriteLine($"{Relative(d)}({pos.Line + 1},{pos.Character + 1}): {d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
}
Console.WriteLine();
foreach (var (rel, n) in underProject.GroupBy(d => Relative(d)!).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Count())))
    Console.WriteLine($"{rel}: {n} compile error(s)");
Console.WriteLine($"{migration.Count(d => Relative(d) is not null)} migration diagnostic(s), {underProject.Count} compile error(s) in {trees.Count(t => Relative2(t.FilePath) is not null)} file(s)");
return 0;

/// <summary>Loads generator assemblies into this process (Roslyn's own are already loaded).</summary>
sealed class Loader : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath) { }
    public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
}
