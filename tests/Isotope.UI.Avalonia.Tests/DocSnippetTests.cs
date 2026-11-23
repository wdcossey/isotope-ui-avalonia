using System.Text.RegularExpressions;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The docs show only compiled, tested code (#17): every C# block in the README and the guide follows a
/// <c>&lt;!-- snippet: name --&gt;</c> marker, and its text equals the <c>#region name</c> of that name in this test
/// project (Examples, GuideExamples), which the tests compile and run.
/// </summary>
public class DocSnippetTests
{
    // The README and every page of the guide.
    public static TheoryData<string> Docs()
    {
        var data = new TheoryData<string> { "README.md" };
        foreach (var page in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "docs", "guide"), "*.md").Order(StringComparer.Ordinal))
            data.Add("docs/guide/" + Path.GetFileName(page));
        return data;
    }

    [Theory]
    [MemberData(nameof(Docs))]
    public void Every_csharp_block_is_a_tested_region(string doc)
    {
        var root = RepoRoot();
        var regions = Regions(Path.Combine(root, "tests", "Isotope.UI.Avalonia.Tests"));
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
                problems.Add($"snippet '{name}': no #region {name} in the test project");
            else if (code != m.Groups["code"].Value)
                problems.Add($"snippet '{name}' differs from its region:\n--- region\n{code}\n--- doc\n{m.Groups["code"].Value}");
        }
        return problems;
    }

    // #region name … #endregion in the test project's own sources, dedented.
    private static Dictionary<string, string> Regions(string dir)
    {
        var regions = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var lines = Normalize(File.ReadAllText(file)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var start = Regex.Match(lines[i], @"^\s*#region (?<name>[\w-]+)\s*$");
                if (!start.Success) continue;
                int end = Array.FindIndex(lines, i + 1, l => l.Trim() == "#endregion");
                if (end < 0) throw new InvalidOperationException($"{file}: #region {start.Groups["name"].Value} has no #endregion");
                regions.Add(start.Groups["name"].Value, Dedent(lines[(i + 1)..end]));
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
