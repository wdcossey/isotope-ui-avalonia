using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Isotope.UI.Mcp;

/// <summary>A page of the guide (<c>docs/guide/*.md</c>, embedded in this assembly when it is built).</summary>
internal sealed record Guide(string Name, string Title, string Summary, string Markdown)
{
    /// <summary>The page as an MCP resource.</summary>
    public string Uri => Guides.UriPrefix + Name;
}

/// <summary>A tested C# block of a guide: the code after a <c>&lt;!-- snippet: name --&gt;</c> marker.</summary>
internal sealed record Snippet(string Guide, string Name, string Code);

/// <summary>The guides, read from this assembly's resources, and their tested code blocks.</summary>
internal static partial class Guides
{
    public const string UriPrefix = "isotope://guides/";
    const string ResourcePrefix = "Isotope.UI.Mcp.guides.";

    static readonly Lazy<IReadOnlyList<Guide>> Shared = new(Load);
    static readonly Lazy<IReadOnlyList<Snippet>> SharedSnippets = new(() =>
        [.. All.SelectMany(g => SnippetBlock().Matches(g.Markdown).Select(m => new Snippet(g.Name, m.Groups["name"].Value, m.Groups["code"].Value)))]);

    /// <summary>Every guide, by name (the file name without <c>.md</c>).</summary>
    public static IReadOnlyList<Guide> All => Shared.Value;

    /// <summary>Every tested C# block of the guides, in page order.</summary>
    public static IReadOnlyList<Snippet> Snippets => SharedSnippets.Value;

    static IReadOnlyList<Guide> Load()
    {
        var assembly = typeof(Guides).Assembly;
        var guides = new List<Guide>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            // A Windows checkout has CRLF line endings; the pages read the same on every OS.
            var markdown = reader.ReadToEnd().Replace("\r\n", "\n");
            var name = resource[ResourcePrefix.Length..^".md".Length];
            guides.Add(new Guide(name, Title(markdown, name), Summary(markdown), markdown));
        }
        return guides;
    }

    /// <summary>A guide by its name, its file name or its title, in any case.</summary>
    public static Guide? Find(string name)
    {
        var n = name.Trim().Replace('\\', '/');
        if (n.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase)) n = n[UriPrefix.Length..];
        if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) n = n[..^3];
        n = n.Replace("docs/guide/", "", StringComparison.OrdinalIgnoreCase);
        return All.FirstOrDefault(g => g.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(g => g.Title.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(g => g.Name.Equals(n.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase));
    }

    static string Title(string markdown, string fallback) =>
        markdown.Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal)) is { } h ? h[2..].Trim() : fallback;

    // The first paragraph after the title, on one line.
    static string Summary(string markdown)
    {
        var lines = markdown.Split('\n').SkipWhile(l => !l.StartsWith("# ", StringComparison.Ordinal)).Skip(1)
            .SkipWhile(l => l.Trim().Length == 0)
            .TakeWhile(l => l.Trim().Length > 0 && !l.StartsWith('#'));
        return string.Join(" ", lines.Select(l => l.Trim()));
    }

    // Guides whose snippets use a made-up control library (writing-controls: spike J's RatingBar, CalloutCard…), which
    // an app doesn't have, so they aren't examples of Isotope's own API (#80).
    static readonly HashSet<string> NotApiExamples = new(StringComparer.Ordinal) { "writing-controls" };

    /// <summary>
    /// The tested snippets that use a name, best first: the one named after it (<c>use-state</c> for <c>UseState</c>),
    /// then those that call it or name it. With an owner, a snippet must name the owner too (a member's type). The
    /// guides whose code uses a made-up library (<see cref="NotApiExamples"/>) are left out.
    /// </summary>
    public static IEnumerable<Snippet> Using(string name, string? owner = null)
    {
        var kebab = Kebab().Replace(name, "$1-$2").ToLowerInvariant();
        var word = Word(name);
        var ownerWord = owner is null ? null : Word(owner);
        return Snippets.Where(s => !NotApiExamples.Contains(s.Guide) && word.IsMatch(s.Code) && (ownerWord is null || ownerWord.IsMatch(s.Code))).OrderBy(s => s.Name == kebab ? 0 : 1);

        static Regex Word(string w) => new(@"(?<![\w])" + Regex.Escape(w) + @"(?![\w])");
    }

    /// <summary>Each guide as an MCP resource, <c>isotope://guides/&lt;name&gt;</c>, in Markdown.</summary>
    public static IEnumerable<McpServerResource> Resources() =>
        All.Select(g => McpServerResource.Create(() => g.Markdown, new McpServerResourceCreateOptions
        {
            UriTemplate = g.Uri,
            Name = g.Name,
            Title = g.Title,
            Description = g.Summary,
            MimeType = "text/markdown",
        }));

    // As the library's DocSnippetTests read them.
    [GeneratedRegex(@"<!-- snippet: (?<name>[\w-]+) -->\n```(?:csharp|cs|c\#)[ \t]*\n(?<code>.*?)\n```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SnippetBlock();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Kebab();
}
