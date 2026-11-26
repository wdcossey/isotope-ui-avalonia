using System.Text.RegularExpressions;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The docs show only compiled, tested code (#17): every C# block in the README, the guide and the package READMEs follows a
/// <c>&lt;!-- snippet: name --&gt;</c> marker, and its text equals the <c>#region name</c> of that name in this test
/// project (Examples, GuideExamples), which the tests compile and run, or in spike J (#80: the worked example of the
/// guide to writing controls, a library built outside the repo, which <c>spikes/J-third-party/run.mjs</c> builds and
/// tests against the packed packages on each framework).
/// </summary>
public class DocSnippetTests
{
    // The README, every page of the guide, and each package's README (src/Isotope.*/README.md, #52), which nuget.org shows.
    public static TheoryData<string> Docs()
    {
        var data = new TheoryData<string> { "README.md" };
        foreach (var page in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "docs", "guide"), "*.md").Order(StringComparer.Ordinal))
            data.Add("docs/guide/" + Path.GetFileName(page));
        foreach (var project in Directory.EnumerateDirectories(Path.Combine(RepoRoot(), "src"), "Isotope.*").Order(StringComparer.Ordinal))
            if (File.Exists(Path.Combine(project, "README.md")))
                data.Add("src/" + Path.GetFileName(project) + "/README.md");
        return data;
    }

    [Theory]
    [MemberData(nameof(Docs))]
    public void Every_csharp_block_is_a_tested_region(string doc)
    {
        var root = RepoRoot();
        var regions = Regions(SnippetSources(root));
        var problems = Check(Normalize(File.ReadAllText(Path.Combine(root, doc))), regions);
        Assert.True(problems.Count == 0, doc + ":\n" + string.Join("\n", problems));
    }

    [Fact]
    public void A_changed_or_unmarked_block_is_reported()
    {
        var regions = new Dictionary<string, string> { ["a"] = "int x = 1;" };
        Assert.Empty(Check("<!-- snippet: a -->\n```csharp\nint x = 1;\n```\n", regions));
        Assert.Single(Check("<!-- snippet: a -->\n```csharp\nint x = 2;\n```\n", regions));
        Assert.Single(Check("```csharp\nint x = 1;\n```\n", regions));
        Assert.Single(Check("```C# \nint x = 1;\n```\n", regions));
        Assert.Single(Check("<!-- snippet: b -->\n```csharp\nint x = 1;\n```\n", regions));
    }

    // Each fenced C# block, with the marker line just before it (if any).
    private static readonly Regex Block = new(@"(?:<!-- snippet: (?<name>[\w-]+) -->\n)?```(?:csharp|cs|c\#)[ \t]*\n(?<code>.*?)\n```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static List<string> Check(string markdown, IReadOnlyDictionary<string, string> regions)
    {
        var problems = new List<string>();
        foreach (Match m in Block.Matches(markdown))
        {
            var name = m.Groups["name"].Value;
            if (name.Length == 0)
                problems.Add("a C# block with no snippet marker: " + FirstLine(m.Groups["code"].Value));
            else if (!regions.TryGetValue(name, out var code))
                problems.Add($"snippet '{name}': no #region {name} in the test project or spike J");
            else if (code != m.Groups["code"].Value)
                problems.Add($"snippet '{name}' differs from its region:\n--- region\n{code}\n--- doc\n{m.Groups["code"].Value}");
        }
        return problems;
    }

    // The sources whose regions the docs show: this test project's own, and spike J's (its build output left out).
    private static IEnumerable<string> SnippetSources(string root)
    {
        var spike = Path.Combine(root, "spikes", "J-third-party");
        return Directory.EnumerateFiles(Path.Combine(root, "tests", "Isotope.UI.Avalonia.Tests"), "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(spike, "*.cs", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(spike, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part is "bin" or "obj")));
    }

    // #region name … #endregion in those sources, dedented. A name is used once across them all.
    private static Dictionary<string, string> Regions(IEnumerable<string> files)
    {
        var regions = new Dictionary<string, string>();
        foreach (var file in files)
        {
            var lines = Normalize(File.ReadAllText(file)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var start = Regex.Match(lines[i], @"^\s*#region (?<name>[\w-]+)\s*$");
                if (!start.Success) continue;
                int end = Array.FindIndex(lines, i + 1, l => l.Trim() == "#endregion");
                if (end < 0) throw new InvalidOperationException($"{file}: #region {start.Groups["name"].Value} has no #endregion");
                if (!regions.TryAdd(start.Groups["name"].Value, Dedent(lines[(i + 1)..end])))
                    throw new InvalidOperationException($"{file}: a second #region {start.Groups["name"].Value}");
                i = end;
            }
        }
        return regions;
    }

    private static string Dedent(string[] lines)
    {
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Trim().Length == 0 ? "" : l[indent..].TrimEnd()));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string FirstLine(string code) => code.Split('\n')[0];

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "UPSTREAM.lock"))) return dir.FullName;
        throw new InvalidOperationException("no UPSTREAM.lock above " + AppContext.BaseDirectory);
    }
}
