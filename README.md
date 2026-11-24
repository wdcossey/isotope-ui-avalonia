# Isotope.UI.Avalonia

React-style function components and hooks for **Avalonia 12**, built on the engine of
[microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) (MIT), with an Avalonia-native API.

> **Status: research and spikes. Nothing is released and no packages are published.**
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

The factories (`using static Isotope.UI.Factories;`) keep Reactor's names; their parameters and the elements' members take
Avalonia's names (`Text`, `Minimum`, `IsChecked`), with Avalonia's value types. See
[Writing components](docs/guide/writing-components.md) for the vocabulary, a todo app and an example of each hook.

This is the premise of Microsoft.UI.Reactor, which offers it on WinUI 3, and it is the premise of Isotope on Avalonia 12:

- **Hooks for state management:** `UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`,
  `UseContext`, plus async data (`UseResource`, `UseMutation`, `UseInfiniteResource`). These come from Reactor's own
  engine, vendored unchanged or through deterministic transforms, so their semantics and fixes track upstream.
- **Platform hooks** keep Reactor's names but are written for Avalonia: theme and colour scheme, high contrast,
  windows, displays and DPI (reduced motion and the tray icon later).
- **An Avalonia-native element vocabulary and layout:** Avalonia's controls and types, and flexbox through Reactor's Yoga
  port.

Avalonia's mainstream model is XAML with MVVM. Its declarative options either use F# (Avalonia.FuncUI) or stay with
classes and bindings in C# (Avalonia.Markup.Declarative). Isotope brings Reactor's C# function components and hooks to
Avalonia, so the same model reaches Windows, macOS, Linux, the browser and mobile.

Package IDs use the `Isotope.` prefix, because `Avalonia.` is reserved on NuGet.

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
| `FINDINGS.md` | The research results, the sync recommendation and the decisions taken |
| `docs/PROCESS.md` | How work runs: issues, branches, self-review, merging |
| `docs/spikes/` | Spike reports: A (engine compile), B (upstream churn), C (Uno and upstream), D (FlexPanel on Avalonia), E (wrappers generator), F (descriptor runtime), G (the vertical slice) |
| `docs/guide/` | Writing components: the element factories and the hooks, from tested code |
| `docs/LESSONS.md`, `docs/agent-brief.md` | Traps already hit; the brief for parallel agents |
| `src/Isotope.UI.Avalonia/` | The library (`Isotope.UI.*`): our Avalonia seam (`Element`, `ElementModifiers`, `Reconciler`, …) plus upstream's engine files, compiled from the vendored `src/Upstream/` (listed in `UpstreamFiles.props`) |
| `src/Isotope.UI.Avalonia.Desktop/` | The one-line startup, `ReactorApp.Run<TRoot>(title, width, height)`, over a default app builder (platform detection, Fluent theme); desktop only |
| `src/Isotope.UI.Migration/` | The Reactor → Isotope migration analyzer and code fixes (#33), run with `dotnet format analyzers` |
| `src/Isotope.UI.Avalonia.Generators/` | Upstream's wrappers generator built for Avalonia (vendored, `gen-*` rules), referenced by the library as an analyzer |
| `samples/Isotope.UI.Samples/` | The sample app: a tab per area (counter and todo, buttons, menus, numbers, split view, error boundary), with a real-window checklist in its README (`dotnet run`) |
| `tests/Isotope.UI.Avalonia.Tests/` | Headless tests of the seam and the hooks core |
| `tests/Isotope.UI.Migration.Tests/` | Tests of the migration's rules, compiled against the library |
| `spikes/A-engine/` | Measures how much of upstream's engine compiles without WinUI |
| `spikes/F-descriptors/` | Spike F: the descriptor runtime's build errors and the port's measurements |
| `spikes/G-slice/` | Spike G: headless timings of the slice |
| `spikes/D-flex/` | `FlexPanel` on Avalonia 12 over upstream's unmodified Yoga, with upstream's Yoga tests |
| `tools/` | Build-error grouping, the import rewrite, and `tools/sync` (vendor and regenerate: `node tools/sync/sync.mjs --help`) |
| `tools/migration/` | Measures the migration on upstream's samples, and lists what is left in a project |
| `src/Upstream/` | Vendored tier 1 and 2 upstream files, generated by `tools/sync`; never edited by hand |
| `docs/sync/` | Sync reports (what changed upstream, and the tier 3 PRs to port) |
| `UPSTREAM.lock` | The upstream commit everything is built and compared against |
| `upstream/` | Git-ignored cache of the upstream repo (you create it, see below) |

## Getting started

Prerequisites:
- the **.NET 11 SDK** (11.0.100 or later, pinned in `global.json`; projects target `net10.0`, the LTS);
- Node.js 20 or later;
- git.

Python 3 is optional, needed only for the Spike B scripts.

```sh
git clone <this repo> isotope && cd isotope
# Upstream cache, full history (Spike B needs it), checked out at the pinned commit:
git clone https://github.com/microsoft/microsoft-ui-reactor upstream
git -C upstream checkout "$(sed -n 's/^baseline=\([0-9a-f]*\).*/\1/p' UPSTREAM.lock)"

dotnet test spikes/D-flex/Reactor.Avalonia.Flex.Tests      # 794 tests: upstream's Yoga tests + FlexPanel
node spikes/A-engine/run-spike.mjs 3                        # Spike A: groups the engine's compile errors (569 at `1f078436`; 567 at `28a87225`)
node spikes/A-engine/run-spike.mjs 4                        # Spike A step 4: + Roslyn rules (502 at `1f078436`)
dotnet test tests/Isotope.UI.Avalonia.Tests                 # the library: seam, hooks, controls, FlexPanel, examples, and 622 of upstream's engine tests (1056 tests; needs no upstream/)
dotnet test tests/Isotope.UI.Migration.Tests  # the migration analyzer (38 tests)
node --test tools/rewrite-upstream.test.mjs                 # Spike A's build-time rewrite tool
node tools/sync/test/all.mjs                                # the sync harness's own tests (needs dotnet: the Roslyn step)
node tools/sync/sync.mjs --check --no-fetch                 # vendored files match upstream + transforms
dotnet test tools/sync/verify/Upstream.Yoga.Tests           # upstream's Yoga tests on the vendored code
```

Run `dotnet` from the repo root, not from inside `upstream/`. Upstream's own `global.json` selects a different test runner.

## Working on it

- **Tasks** are GitHub Issues, with labels for priority, type and `self-drive`/`hands-on`/`blocked`.
- **Process:** [docs/PROCESS.md](docs/PROCESS.md). Agents (Claude Code) also read [CLAUDE.md](CLAUDE.md).
- **No personal data in the repo:** no names, emails, usernames, local paths or upstream contributors' names in files,
  outputs, screenshots or issues.

## Licence

MIT, see [`LICENSE`](LICENSE). Upstream code vendored in `src/Upstream/` stays under upstream's MIT licence and
notices ([`src/Upstream/LICENSE`](src/Upstream/LICENSE), [`ThirdPartyNoticeText.txt`](src/Upstream/src/Reactor/ThirdPartyNoticeText.txt)).
