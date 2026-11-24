using System.Reflection;
using Isotope.UI.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// isotope-mcp: an MCP server over stdio. stdout carries the protocol, so every log goes to stderr.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var version = typeof(IsotopeTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "isotope-mcp", Title = "Isotope.UI.Avalonia", Version = version };
        o.ServerInstructions = Server.Instructions;
    })
    .WithStdioServerTransport()
    .WithTools([typeof(IsotopeTools)])
    .WithResources(Guides.Resources());

await builder.Build().RunAsync();

namespace Isotope.UI.Mcp
{
    static class Server
    {
        public const string Instructions =
            "Isotope.UI.Avalonia is React-style UI for Avalonia 12 in C#: function components that take a RenderContext, read state " +
            "through hooks (ctx.UseState, UseEffect…) and return an Element built by factories (using static Isotope.UI.Factories; " +
            "VStack, TextBlock, Button…) and modifiers (.Margin, .HorizontalAlignment…). No XAML, no bindings, no view models. " +
            "Names are Avalonia's (TabControl, NumericUpDown, IsChecked, OnClick), except where Avalonia has none. " +
            "Use search_api and get_api for signatures and docs, list_guides and get_guide for how to write components, " +
            "map_reactor_name for a Microsoft.UI.Reactor name's Isotope counterpart, and migrate_code to port Reactor code.";
    }
}
