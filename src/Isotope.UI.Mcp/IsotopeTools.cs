using System.ComponentModel;
using System.Text;
using Isotope.UI.Mcp.Api;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Isotope.UI.Mcp;

/// <summary>The MCP tools: Isotope's API, Reactor's names, the migration and the guides. Each returns Markdown.</summary>
[McpServerToolType]
public static class IsotopeTools
{
    const int MaxMembersListed = 60;

    [McpServerTool(Name = "search_api", Title = "Search Isotope's API", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Searches the public API of Isotope.UI.Avalonia and its desktop startup: element factories (Button, VStack, TextBox…), " +
        "modifiers (.Margin, .HorizontalAlignment…), hooks (UseState, UseEffect…), element records and other types. Matches the name " +
        "or the doc summary; each result has its C# signature and one-line summary. Use get_api for the full docs of one name.")]
    public static string SearchApi(
        [Description("A name or part of one (\"button\", \"UseSt\"), or words of the summary (\"text style\").")] string query,
        [Description("Only this kind: factory, modifier, hook, element or type. Default: all.")] string? kind = null,
        [Description("At most this many results (default 25, at most 200).")] int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new McpException("search_api needs a query.");
        ApiKind[]? kinds = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            // By name only: Enum.TryParse would take "42" and "factory, hook" too.
            var k = ApiCatalog.Searchable.Where(s => s.ToString().Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (k.Length == 0) throw new McpException($"Unknown kind '{kind}': use factory, modifier, hook, element or type.");
            kinds = k;
        }
        limit = Math.Clamp(limit, 1, 200);
        var results = ApiCatalog.Default.Search(query, kinds);
        if (results.Count == 0)
            return $"Nothing in Isotope's API matches \"{query.Trim()}\". For a Reactor name, try map_reactor_name; list_guides lists the guides.";
        var sb = new StringBuilder();
        sb.AppendLine($"{results.Count} match{(results.Count == 1 ? "" : "es")} for \"{query.Trim()}\"{(results.Count > limit ? $", the first {limit}" : "")}:").AppendLine();
        foreach (var e in results.Take(limit)) sb.AppendLine(Line(e));
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "get_api", Title = "Read the docs of an Isotope API", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("The full documentation of one name in Isotope's API: every overload of a factory, modifier or hook (\"Button\", " +
        "\"Margin\", \"UseState\"), a type with its members (\"ButtonElement\", \"RenderContext\"), or one member (\"ButtonElement.OnClick\", " +
        "\"ReactorApp.Run\"). Each has its C# signature and doc comment, and an example from the guides' tested code when one uses it.")]
    public static string GetApi([Description("The name: Button, UseState, ButtonElement, ButtonElement.OnClick, ReactorApp.Run.")] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("get_api needs a name.");
        var catalog = ApiCatalog.Default;
        var found = catalog.Find(name);
        if (found.Count == 0)
        {
            var near = catalog.Search(name.Split('.')[^1]).Take(8).ToList();
            return $"Isotope's API has no \"{name.Trim()}\"." + (near.Count == 0 ? "" : " Close matches:\n\n" + string.Join("\n", near.Select(Line)))
                + "\n\nFor a Reactor name, try map_reactor_name.";
        }
        var sb = new StringBuilder();
        foreach (var group in found.GroupBy(e => (e.Kind, e.QualifiedName, e.Namespace)))
        {
            var first = group.First();
            var count = group.Count();
            sb.AppendLine($"# {first.QualifiedName} ({Describe(first.Kind)}{(count > 1 ? $", {count} overloads" : "")})").AppendLine();
            sb.AppendLine(Using(first)).AppendLine();
            foreach (var e in group)
            {
                sb.AppendLine("```csharp").AppendLine(e.Signature).AppendLine("```");
                var doc = e.FullDoc;
                sb.AppendLine(doc.Length > 0 ? doc : "(No doc comment.)").AppendLine();
            }
            if (first.IsType)
            {
                var factories = catalog.FactoriesOf(first).ToList();
                if (factories.Count > 0)
                {
                    sb.AppendLine("Built by:").AppendLine();
                    foreach (var f in factories) sb.AppendLine($"- `{f.Signature}`");
                    sb.AppendLine();
                }
                var members = catalog.MembersOf(first).ToList();
                if (members.Count > 0)
                {
                    sb.AppendLine("Members:").AppendLine();
                    foreach (var m in members.Take(MaxMembersListed))
                        sb.AppendLine($"- `{m.Signature}`" + (m.Summary is { Length: > 0 } s ? " — " + s : ""));
                    if (members.Count > MaxMembersListed) sb.AppendLine($"- … {members.Count - MaxMembersListed} more (get_api {first.Owner}.<member>)");
                    sb.AppendLine();
                }
            }
            // A member's example must name its type too (ReactorApp.Run, not any Run); a factory, hook or modifier is
            // called by its name alone.
            var bare = first.IsType ? first.Owner.Split('<')[0] : first.Name;
            var owner = first.Kind == ApiKind.Member ? first.Owner.Split('<')[0] : null;
            if (Guides.Using(bare, owner).FirstOrDefault() is { } example)
                sb.AppendLine($"Example (guide `{example.Guide}`, tested snippet `{example.Name}`):").AppendLine()
                    .AppendLine("```csharp").AppendLine(example.Code).AppendLine("```").AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "map_reactor_name", Title = "Map a Microsoft.UI.Reactor name to Isotope's", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Isotope's name for a Microsoft.UI.Reactor (WinUI) name: a type, factory, modifier, record member (\"TextBlockElement.Content\"), " +
        "factory parameter, Reactor or WinUI namespace, or WinUI type (\"Microsoft.UI.Xaml.Thickness\"), from the rename table and the " +
        "migration's tables; or why it has no counterpart and what to use instead.")]
    public static string MapReactorName([Description("The Reactor name: TabView, HAlign, TextBlockElement.Content, Microsoft.UI.Xaml.Visibility, ProgressRing.")] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("map_reactor_name needs a name.");
        return ReactorNames.Map(name);
    }

    [McpServerTool(Name = "migrate_code", Title = "Migrate Reactor code to Isotope", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Runs Isotope's Reactor → Isotope migration (the Isotope.UI.Migration analyzer and its code fixes, as dotnet format analyzers " +
        "runs them) on one C# file, in memory, compiled against Isotope and Avalonia. Returns the rewritten code, the migration's diagnostics " +
        "that are left for a hand fix, and the compiler errors. Pass a whole file, usings included; nothing is written to disk.")]
    public static async Task<string> MigrateCode(
        [Description("The C# file's text: Microsoft.UI.Reactor code (usings, components, factory calls).")] string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new McpException("migrate_code needs some C# code.");
        if (code.Length > CodeMigration.MaxLength) throw new McpException($"migrate_code takes at most {CodeMigration.MaxLength} characters; split the file.");
        var result = await CodeMigration.RunAsync(code, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine(result.Changed ? "# Migrated code" : "# Migrated code (no fix changed it)").AppendLine();
        sb.AppendLine("```csharp").AppendLine(result.Code.TrimEnd()).AppendLine("```").AppendLine();
        sb.AppendLine($"## Left for a hand fix ({result.Left.Count})").AppendLine();
        sb.AppendLine(result.Left.Count == 0 ? "Nothing: the migration reports nothing more." : string.Join("\n", result.Left.Select(d => "- " + CodeMigration.Describe(d))));
        sb.AppendLine().AppendLine($"## Compiler errors after the migration ({result.Errors.Count})").AppendLine();
        const int maxErrors = 30;
        sb.AppendLine(result.Errors.Count == 0 ? "None." : string.Join("\n", result.Errors.Take(maxErrors).Select(d => "- " + CodeMigration.Describe(d))));
        if (result.Errors.Count > maxErrors) sb.AppendLine($"- … {result.Errors.Count - maxErrors} more");
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "list_guides", Title = "List Isotope's guides", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Lists Isotope's guides (writing components, hooks, platform hooks, theme tokens, text styles, error boundaries, migrating " +
        "from Reactor), each with its name, title and summary. Read one with get_guide; they are also MCP resources (isotope://guides/<name>).")]
    public static string ListGuides()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Guides.All.Count} guides (read one with get_guide):").AppendLine();
        foreach (var g in Guides.All) sb.AppendLine($"- `{g.Name}`: **{g.Title}**. {g.Summary} ({g.Uri})");
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "get_guide", Title = "Read one of Isotope's guides", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("One of Isotope's guides, in Markdown. Its C# blocks are compiled and tested code. list_guides lists the names.")]
    public static string GetGuide([Description("The guide's name (writing-components), file name or title.")] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("get_guide needs a name.");
        return Guides.Find(name)?.Markdown
            ?? throw new McpException($"No guide '{name.Trim()}'. The guides: {string.Join(", ", Guides.All.Select(g => g.Name))}.");
    }

    static string Line(ApiEntry e) =>
        $"- {e.Kind.ToString().ToLowerInvariant()} `{e.Signature}`" + (e.Summary is { Length: > 0 } s ? " — " + Clip(s, 240) : "");

    static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    static string Describe(ApiKind kind) => kind switch
    {
        ApiKind.Factory => "factory",
        ApiKind.Modifier => "modifier",
        ApiKind.Hook => "hook",
        ApiKind.Element => "element record",
        ApiKind.Type => "type",
        _ => "member",
    };

    static string Using(ApiEntry e) => e.Kind switch
    {
        ApiKind.Factory => $"Namespace `{e.Namespace}`: `using static {e.Namespace}.{e.Owner};`",
        ApiKind.Modifier => $"Namespace `{e.Namespace}` (an extension method: `using {e.Namespace};`)",
        _ => $"Namespace `{e.Namespace}`",
    };
}
