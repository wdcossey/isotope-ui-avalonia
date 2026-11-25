using ModelContextProtocol;
using Xunit;

namespace Isotope.UI.Mcp.Tests;

/// <summary>list_guides and get_guide, and the guides as resources: docs/guide, embedded when the tool is built.</summary>
public class GuideTests
{
    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "UPSTREAM.lock"))) return dir.FullName;
        throw new InvalidOperationException("no UPSTREAM.lock above the test's folder");
    }

    static string[] GuideFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepoRoot(), "docs", "guide"), "*.md").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)];

    [Fact]
    public void Every_page_of_the_guide_is_embedded_and_listed()
    {
        Assert.Equal(GuideFiles(), Guides.All.Select(g => g.Name));
        var text = IsotopeTools.ListGuides();
        foreach (var g in Guides.All)
            Assert.Contains($"- `{g.Name}`: **{g.Title}**. {g.Summary} (isotope://guides/{g.Name})", text);
        Assert.Contains("**Writing components**", text);
    }

    [Fact]
    public void A_guide_reads_as_its_file()
    {
        foreach (var name in GuideFiles())
        {
            var file = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "guide", name + ".md")).Replace("\r\n", "\n");
            Assert.Equal(file, IsotopeTools.GetGuide(name));
        }
        Assert.Equal(IsotopeTools.GetGuide("writing-components"), IsotopeTools.GetGuide("Writing components"));
        Assert.Equal(IsotopeTools.GetGuide("writing-components"), IsotopeTools.GetGuide("docs/guide/writing-components.md"));
        Assert.Equal(IsotopeTools.GetGuide("writing-components"), IsotopeTools.GetGuide("docs\\guide\\writing-components.md"));
        var missing = Assert.Throws<McpException>(() => IsotopeTools.GetGuide("no-such-guide"));
        Assert.Contains("writing-components", missing.Message);
    }

    [Fact]
    public void The_guides_tested_snippets_are_read_from_their_markers()
    {
        Assert.Contains(Guides.Snippets, s => s.Guide == "writing-components" && s.Name == "use-state" && s.Code.Contains("ctx.UseState(false)"));
        Assert.True(Guides.Snippets.Count >= 20, Guides.Snippets.Count + " snippets");
        Assert.Equal("use-state", Guides.Using("UseState").First().Name);
    }

    [Fact]
    public void Each_guide_is_a_resource()
    {
        var resources = Guides.Resources().ToList();
        Assert.Equal(Guides.All.Select(g => "isotope://guides/" + g.Name), resources.Select(r => r.ProtocolResource!.Uri));
        Assert.All(resources, r => Assert.Equal("text/markdown", r.ProtocolResource!.MimeType));
    }
}
