# Isotope.UI.Avalonia

React-style function components and hooks for **Avalonia 12**, built on the engine of
[microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) (MIT), with an Avalonia-native API.

> **Status: early development.** Nothing is released and no packages are published yet, and the API may still change.
> An independent project, not affiliated with or endorsed by Microsoft or the Avalonia project.

## What it is

**Your UI is a function of state.** No XAML, no data binding, no view models. You write C# functions that return a
description of the UI. Hooks hold the state, and Isotope keeps the screen in sync on every platform Avalonia runs on.

<!-- snippet: counter -->
```csharp
public static Element Counter(RenderContext ctx)
{
    var (count, setCount) = ctx.UseState(0);
    return VStack(
        TextBlock($"Clicked {count} times"),
        Button("Click", () => setCount(count + 1)));
}
```

`ReactorApp.Run("Counter", Counter, width: 400, height: 300)` opens it in a window (`Isotope.UI.Avalonia.Desktop`).
The factories (`using static Isotope.UI.Factories;`), elements and modifiers take Avalonia's names (`TabControl`,
`NumericUpDown`, `.HorizontalAlignment(...)`), and so do their parameters and the elements' members (`Text`, `Minimum`,
`IsChecked`), with Avalonia's value types. `VStack`, `HStack` and names with no Avalonia counterpart keep Reactor's. See
[Writing components](docs/guide/writing-components.md) for the vocabulary, a todo app and an example of each hook.

This is the premise of Microsoft.UI.Reactor, which offers it on WinUI 3, and it is the premise of Isotope on Avalonia 12.
Avalonia's mainstream model is XAML with MVVM. Its declarative options either use F# (Avalonia.FuncUI) or stay with
classes and bindings in C# (Avalonia.Markup.Declarative). Isotope brings Reactor's C# function components and hooks to
Avalonia, so the same model reaches Windows, macOS, Linux, the browser and mobile.

## What works today

- **Hooks:** `UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`, `UseContext`, and async data
  (`UseResource`, `UseMutation`, `UseInfiniteResource`). They come from Reactor's own engine, vendored unchanged or
  through deterministic transforms, so their semantics and fixes track upstream.
- **Platform hooks**, with Reactor's names, written for Avalonia: theme and colour scheme, high contrast, windows,
  displays and DPI ([guide](docs/guide/platform-hooks.md)), Reactor's theme tokens (`Theme.*`, [guide](docs/guide/theme-tokens.md)), and its named text styles (`Title`, `Body`,
  `Caption`…, [guide](docs/guide/text-styles.md)).
- **32 controls:** text and image; button, toggle, repeat and hyperlink buttons; check box, radio button, toggle
  switch; text box, numeric up-down, auto-complete box, combo box, list box, slider, progress; date and time pickers; tabs,
  menus, context menus and tooltips; split view, expander, border, scroll viewer, separator, tree view, viewbox; and
  stack, grid, wrap and flexbox layouts (`FlexPanel`, over Reactor's port of Yoga).
- **Function and class components**, keyed lists (reorders move controls instead of rebuilding them), and **error
  boundaries** ([guide](docs/guide/error-boundaries.md)).
- **A one-line app startup**, `ReactorApp.Run<TRoot>(title, width, height)`, and a sample app
  ([`samples/Isotope.UI.Samples`](samples/Isotope.UI.Samples)).
- **A Reactor → Isotope migration tool:** a Roslyn analyzer with code fixes, run with `dotnet format analyzers`
  ([guide](docs/guide/migrating-from-reactor.md)).
- **An MCP server for coding agents** (`Isotope.UI.Mcp`, a `dotnet tool` with the command `isotope-mcp`): search and read
  the API with its docs, map Reactor names to Isotope's, run the migration on a C# file, and read the guides
  ([its README](src/Isotope.UI.Mcp/README.md)).
- **Tests:** 1,192 headless library tests, 622 of them upstream's own engine tests, plus upstream's 794 Yoga tests.

Not there yet: more controls (rich text, icons, list views…), tracked from what the migration tool finds missing in
upstream's samples (#35); animation; and published packages (they build and are checked on every push;
[releasing](docs/releasing.md)).

## Try it

You need the **.NET 11 SDK** (11.0.100 or later, pinned in `global.json`; the packages target `net9.0`, `net10.0` and `net11.0`; the samples and tools `net10.0`, the LTS) and git. The library's tests run on each of the three frameworks, so they
also need the .NET 9 and 10 runtimes; with the SDK alone, add `-f net11.0`.

```sh
git clone <this repository> isotope && cd isotope
dotnet run --project samples/Isotope.UI.Samples       # the sample app: a tab per area
dotnet test tests/Isotope.UI.Avalonia.Tests           # the library's tests (headless)
```

The library builds from the vendored upstream files in `src/Upstream/`; it needs no copy of upstream.

## MCP server for coding agents

`Isotope.UI.Mcp` is an MCP server, a `dotnet tool` with the command `isotope-mcp`, that helps coding agents write
Isotope apps and migrate Reactor ones: `search_api`, `get_api`, `map_reactor_name`, `migrate_code`, `list_guides` and
`get_guide`, with the guides also as MCP resources. It needs no running app.

```sh
node tools/packaging/pack.mjs --no-smoke      # packs into bin/packages (nothing is published yet)
dotnet tool install --global Isotope.UI.Mcp --version <version> --add-source bin/packages
claude mcp add isotope -- isotope-mcp          # Claude Code; other clients: src/Isotope.UI.Mcp/README.md
```

## How it stays in step with upstream

Upstream gets several commits a day. The approach, measured in four spikes (see [FINDINGS.md](FINDINGS.md)):

- **Three tiers of upstream code.**
  - **Tier 1, verbatim:** files that compile unmodified, such as Yoga and most of the data layer (only the repo-wide
    namespace rename applies).
  - **Tier 2, verbatim plus deterministic transforms:** most of the engine, including hooks, child diffing and validation.
  - **Tier 3, rewritten for Avalonia and watched:** elements, the reconciler, control handlers and hosting.
- **Vendor and regenerate.** `tools/sync` copies tier 1 and 2 files from a git-ignored cache of upstream, at the commit
  pinned in [`UPSTREAM.lock`](UPSTREAM.lock), and applies the transform rules.
  - Vendored files are never edited by hand, and CI fails if they drift.
  - A sync is a regeneration, not a merge. Tier 3 gets a report of the upstream PRs to port by hand.
- **Upstream is treated as read-only.** We change nothing there and depend on no upstream changes.

## Repository layout

| Path | What |
|---|---|
| `src/Isotope.UI.Avalonia/` | The library (`Isotope.UI.*`): our Avalonia seam (`Element`, `ElementModifiers`, `Reconciler`, …) plus upstream's engine files, compiled from the vendored `src/Upstream/` (listed in `UpstreamFiles.props`) |
| `src/Isotope.UI.Avalonia.Desktop/` | The one-line startup, `ReactorApp.Run<TRoot>(title, width, height)`, over a default app builder (platform detection, Fluent theme); desktop only |
| `src/Isotope.UI.Avalonia.Generators/` | Upstream's wrappers generator built for Avalonia (vendored, `gen-*` rules), referenced by the library as an analyzer |
| `src/Isotope.UI.Migration/` | The Reactor → Isotope migration analyzer and code fixes, run with `dotnet format analyzers` |
| `src/Isotope.UI.Mcp/` | The MCP server for coding agents, a `dotnet tool` (`isotope-mcp`) over stdio: the API by reflection over the library and its XML docs, Reactor's names from the migration's tables, the migration itself, the guides |
| `src/Upstream/` | Vendored tier 1 and 2 upstream files, generated by `tools/sync`; never edited by hand |
| `samples/Isotope.UI.Samples/` | The sample app: a tab per area (counter and todo, buttons, menus, numbers, split view, error boundary), with a real-window checklist in its README |
| `tests/Isotope.UI.Avalonia.Tests/` | Headless tests of the library, upstream's engine tests, and every C# block in the docs |
| `tests/Isotope.UI.Migration.Tests/` | Tests of the migration's rules, compiled against the library |
| `tests/Isotope.UI.Mcp.Tests/` | Tests of each MCP tool in process, and of the built tool over stdio with the SDK's client |
| `docs/guide/` | Writing components, platform hooks, theme tokens, text styles, error boundaries, migrating from Reactor: all from tested code |
| `FINDINGS.md`, `docs/spikes/`, `spikes/` | Research notes and the spikes behind the design |
| `docs/sync/` | Sync reports (what changed upstream, and the tier 3 PRs to port) |
| `tools/sync/` | Vendor and regenerate (`node tools/sync/sync.mjs --help`), and the weekly sync job's glue |
| `tools/migration/` | Measures the migration on upstream's samples, and lists what is left in a project |
| `tools/packaging/`, `tests/Isotope.UI.PackageSmoke/` | Pack and check the NuGet packages, and a consumer app built against them alone ([releasing](docs/releasing.md)) |
| `UPSTREAM.lock` | The upstream commit everything is built and compared against |
| `upstream/` | Git-ignored cache of the upstream repo, needed only for `tools/sync` (see below) |

## Developing

Besides the .NET SDK and git: Node.js 20 or later for `tools/`.

```sh
# Upstream cache (for tools/sync), full history, checked out at the pinned commit:
git clone https://github.com/microsoft/microsoft-ui-reactor upstream
git -C upstream checkout "$(sed -n 's/^baseline=\([0-9a-f]*\).*/\1/p' UPSTREAM.lock)"

dotnet test tests/Isotope.UI.Avalonia.Tests              # the library (1192 tests on each of net9.0, net10.0, net11.0; no upstream/)
dotnet test tests/Isotope.UI.Migration.Tests             # the migration analyzer (46 tests)
dotnet test tests/Isotope.UI.Mcp.Tests                   # the MCP server (47 tests)
node tools/sync/sync.mjs --check --no-fetch              # vendored files match upstream + transforms
node tools/sync/test/all.mjs                             # the sync harness's own tests (needs dotnet: the Roslyn step)
dotnet test tools/sync/verify/Upstream.Yoga.Tests        # upstream's Yoga tests on the vendored code
```

Run `dotnet` from the repo root, not from inside `upstream/`: upstream's own `global.json` selects a different test
runner.

## Licence

MIT, see [`LICENSE`](LICENSE). Upstream code vendored in `src/Upstream/` stays under upstream's MIT licence and
notices ([`src/Upstream/LICENSE`](src/Upstream/LICENSE), [`ThirdPartyNoticeText.txt`](src/Upstream/src/Reactor/ThirdPartyNoticeText.txt)).
