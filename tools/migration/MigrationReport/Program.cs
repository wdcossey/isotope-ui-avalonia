// Usage: MigrationReport <items.json> <project directory>
// items.json: the output of `dotnet msbuild <project> -t:Build -getItem:Compile -getItem:ReferencePathWithRefAssemblies
// -getProperty:...` (see measure-samples.mjs). Prints one JSON object: per source file under the project directory
// (relative path), its compile errors and migration diagnostics by ID.
using System.Text.Json;
using System.Text.Json.Nodes;
using Isotope.UI.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: MigrationReport <items.json> <project directory>");
    return 2;
}
var text = File.ReadAllText(args[0]);
// msbuild prints the build's own output first; the JSON is the last top-level object.
var start = text.LastIndexOf("\n{", StringComparison.Ordinal);
var json = JsonNode.Parse(start >= 0 ? text[(start + 1)..] : text)!;
var props = json["Properties"]!;
var items = json["Items"]!;
string Prop(string name) => props[name]?.GetValue<string>() ?? "";
IEnumerable<string> Paths(string item) => items[item]!.AsArray().Select(i => i!["FullPath"]!.GetValue<string>());

var projectDir = Path.GetFullPath(args[1]);
var languageVersion = LanguageVersionFacts.TryParse(Prop("LangVersion"), out var lv) ? lv : LanguageVersion.Default;
var parse = new CSharpParseOptions(languageVersion, preprocessorSymbols: Prop("DefineConstants").Split(';', StringSplitOptions.RemoveEmptyEntries));
var trees = Paths("Compile").Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parse, path: p)).ToList();
var references = Paths("ReferencePathWithRefAssemblies").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
var options = new CSharpCompilationOptions(
    Prop("OutputType") is "Exe" or "WinExe" ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
    nullableContextOptions: Prop("Nullable") == "enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable,
    allowUnsafe: Prop("AllowUnsafeBlocks") == "true");
var compilation = CSharpCompilation.Create(Prop("AssemblyName"), trees, references, options);
var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
var migration = await compilation.WithAnalyzers([new MigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

string? Relative(Diagnostic d) => d.Location.SourceTree?.FilePath is { } p ? Relative2(p) : null;
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
var result = new JsonObject
{
    ["files"] = files,
    // Errors with no file under the project (a missing reference, a missing Main).
    ["otherErrors"] = Count(errors.Where(d => Relative(d) is null)),
};
Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
return 0;
