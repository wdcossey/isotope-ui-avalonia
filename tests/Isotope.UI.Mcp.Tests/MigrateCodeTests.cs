using ModelContextProtocol;
using Xunit;

namespace Isotope.UI.Mcp.Tests;

/// <summary>migrate_code: the migration analyzer and its code fixes, in memory.</summary>
public class MigrateCodeTests
{
    const string Reactor = """
        using Microsoft.UI.Reactor;
        using Microsoft.UI.Reactor.Core;
        using Microsoft.UI.Xaml;
        using static Microsoft.UI.Reactor.Factories;

        static class C
        {
            public static Element Render(RenderContext ctx)
            {
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    TextBlock($"Clicked {n}").HAlign(HorizontalAlignment.Center),
                    TabView(Tab("One", TextBlock("1"))),
                    ProgressRing(),
                    Button("Add", () => setN(n + 1)));
            }
        }
        """;

    [Fact]
    public async Task Reactor_code_is_rewritten_and_what_is_left_is_reported()
    {
        var text = await IsotopeTools.MigrateCode(Reactor, TestContext.Current.CancellationToken);
        Assert.StartsWith("# Migrated code\n", text.Replace("\r\n", "\n"));
        // The namespaces, a WinUI using, a modifier and two factories, renamed by the migration's fixes.
        Assert.Contains("using Isotope.UI;", text);
        Assert.Contains("using static Isotope.UI.Factories;", text);
        Assert.Contains("using Avalonia.Layout;", text);
        Assert.DoesNotContain("Microsoft.UI", text.Split("## Left")[0]);
        Assert.Contains(".HorizontalAlignment(HorizontalAlignment.Center)", text);
        Assert.Contains("TabControl(TabItem(\"One\", TextBlock(\"1\")))", text);
        // ProgressRing has no counterpart: reported for a hand fix, and the compiler can't find it.
        Assert.Contains("## Left for a hand fix (1)", text);
        Assert.Contains("- ISOMIG011 (warning, line 14, column 13): 'ProgressRing' is a WinUI control with no Isotope counterpart", text);
        Assert.Contains("## Compiler errors after the migration (1)", text);
        Assert.Contains("- CS0103 (error, line 14, column 13): The name 'ProgressRing' does not exist", text);
    }

    [Fact]
    public async Task Isotope_code_is_left_as_it_is()
    {
        const string isotope = """
            using Isotope.UI;
            using Isotope.UI.Core;
            using static Isotope.UI.Factories;

            static class C
            {
                public static Element Render(RenderContext ctx) => TextBlock("Hello");
            }
            """;
        var text = await IsotopeTools.MigrateCode(isotope, TestContext.Current.CancellationToken);
        Assert.Contains("# Migrated code (no fix changed it)", text);
        Assert.Contains(isotope.Replace("\r\n", "\n"), text.Replace("\r\n", "\n"));
        Assert.Contains("## Left for a hand fix (0)", text);
        Assert.Contains("## Compiler errors after the migration (0)", text);
    }

    [Fact]
    public async Task Empty_or_oversized_code_is_refused()
    {
        await Assert.ThrowsAsync<McpException>(() => IsotopeTools.MigrateCode("  ", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpException>(() => IsotopeTools.MigrateCode(new string(' ', CodeMigration.MaxLength) + "class C { }", TestContext.Current.CancellationToken));
    }
}
