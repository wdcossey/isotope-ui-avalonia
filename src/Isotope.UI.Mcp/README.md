# Isotope.UI.Mcp

An [MCP](https://modelcontextprotocol.io) server for coding agents that write
[Isotope.UI.Avalonia](https://www.nuget.org/packages/Isotope.UI.Avalonia) apps or move
[Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor) apps to it. It is a `dotnet tool` with the
command `isotope-mcp`, and it talks MCP over stdio. It helps with writing code and needs no running app.

| Tool | What it does |
|---|---|
| `search_api(query, kind?, limit?)` | Finds factories, modifiers, hooks, element records and types by name or summary, each with its C# signature and summary |
| `get_api(name)` | Every overload of one name (`Button`, `UseState`, `ButtonElement`, `ReactorApp.Run`), with its doc comment and an example from the guides' tested code |
| `map_reactor_name(name)` | Isotope's name for a Reactor type, factory, modifier, member, parameter, namespace or WinUI type, or why it has none and what to use instead |
| `migrate_code(code)` | Runs the Reactor → Isotope migration (`Isotope.UI.Migration`'s analyzer and code fixes) on a C# file in memory, and returns the rewritten code, what is left for a hand fix and the compiler errors |
| `list_guides()`, `get_guide(name)` | The guides (writing components, hooks, platform hooks, theme tokens, text styles, error boundaries, migrating from Reactor, writing controls for a control library), also MCP resources at `isotope://guides/<name>` |

Its answers come from what it ships with: the Isotope DLLs of its own version and their XML docs, read by reflection,
the migration's own tables, and the guides as they were when it was built. Use the version of the tool that matches
your Isotope packages.

## Install

```sh
dotnet tool install --global Isotope.UI.Mcp --prerelease
```

From a local build of the repository instead (`node tools/packaging/pack.mjs` packs into `bin/packages`):
`dotnet tool install --global Isotope.UI.Mcp --version <version> --add-source bin/packages`.

The tool needs the .NET 10 runtime or later. To keep it with a repository instead, use a local tool:
`dotnet new tool-manifest` (once), then `dotnet tool install Isotope.UI.Mcp --prerelease`. It then runs as
`dotnet tool run isotope-mcp`.

## Configure an MCP client

The server is a stdio server whose command is `isotope-mcp`. Claude Code:

```sh
claude mcp add isotope -- isotope-mcp
```

Or a project's `.mcp.json`, which Claude Code and other clients read:

```json
{
  "mcpServers": {
    "isotope": { "command": "isotope-mcp" }
  }
}
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "isotope": { "type": "stdio", "command": "isotope-mcp" }
  }
}
```

For a local tool, the command is `dotnet` with the arguments `["tool", "run", "isotope-mcp"]`. Logs go to stderr,
because stdout carries the protocol.

The package is an MCP server package (package type `McpServer`, with `.mcp/server.json`), the form the
`dotnet new mcpserver` template packs.

> **Preview.** An independent project, not affiliated with or endorsed by Microsoft or the Avalonia project.

## Licence

MIT. See `LICENSE` and `licenses/microsoft-ui-reactor/` in the package. The tool carries third-party assemblies too
(the MCP C# SDK, Roslyn, Microsoft.Extensions, Avalonia): `THIRD-PARTY-NOTICES.md` lists each with its licence.
