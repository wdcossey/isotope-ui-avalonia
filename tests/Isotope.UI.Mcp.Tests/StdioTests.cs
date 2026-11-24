using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Isotope.UI.Mcp.Tests;

/// <summary>
/// The built tool (its DLL, runtimeconfig and deps file, copied next to these tests), started as an MCP client starts
/// it: a process speaking the protocol over stdin and stdout, here driven by the SDK's own client.
/// </summary>
public class StdioTests
{
    static async Task<McpClient> StartAsync(CancellationToken ct)
    {
        var tool = Path.Combine(AppContext.BaseDirectory, "Isotope.UI.Mcp.dll");
        Assert.True(File.Exists(tool), tool + " is missing");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "isotope-mcp",
            Command = "dotnet",
            Arguments = [tool],
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    [Fact]
    public async Task The_tool_lists_its_tools_and_answers_a_call_over_stdio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = await StartAsync(ct);
        Assert.Equal("isotope-mcp", client.ServerInfo.Name);
        Assert.Contains("RenderContext", client.ServerInstructions ?? "");

        var tools = await client.ListToolsAsync(cancellationToken: ct);
        Assert.Equal(["get_api", "get_guide", "list_guides", "map_reactor_name", "migrate_code", "search_api"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint, t.Name + " is not read-only"));

        var result = await client.CallToolAsync("get_api", new Dictionary<string, object?> { ["name"] = "UseState" }, cancellationToken: ct);
        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("(T Value, Action<T> Set) RenderContext.UseState<T>(T initialValue, bool threadSafe = false)", text);
        Assert.Contains("Declares a piece of state.", text);
    }

    [Fact]
    public async Task The_guides_are_resources_over_stdio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = await StartAsync(ct);
        var resources = await client.ListResourcesAsync(cancellationToken: ct);
        Assert.Equal(Guides.All.Select(g => g.Uri), resources.Select(r => r.Uri));

        var read = await client.ReadResourceAsync("isotope://guides/writing-components", cancellationToken: ct);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        Assert.Equal(Guides.Find("writing-components")!.Markdown, contents.Text);
    }

    [Fact]
    public async Task A_failing_call_is_an_error_result_and_the_server_keeps_running()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = await StartAsync(ct);
        var failed = await client.CallToolAsync("get_guide", new Dictionary<string, object?> { ["name"] = "no-such-guide" }, cancellationToken: ct);
        Assert.True(failed.IsError);
        Assert.Contains("writing-components", Assert.IsType<TextContentBlock>(Assert.Single(failed.Content)).Text);
        var ok = await client.CallToolAsync("map_reactor_name", new Dictionary<string, object?> { ["name"] = "TabView" }, cancellationToken: ct);
        Assert.Contains("`TabControl`", Assert.IsType<TextContentBlock>(Assert.Single(ok.Content)).Text);
    }
}
