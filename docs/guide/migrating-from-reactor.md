# Migrating from Reactor

Isotope keeps Reactor's model and hooks, and takes Avalonia's names where Reactor's differ, so a Reactor app moves over
mostly mechanically. The
migration tool (`src/Isotope.UI.Migration`, #33) is a Roslyn analyzer with code fixes. It renames what has a
counterpart, and reports what has none, saying what to use instead.

What it changes:

- `Microsoft.UI.Reactor.*` namespaces become `Isotope.UI.*`, where Isotope has the namespace or type.
- WinUI usings become the Avalonia namespaces the file needs.
- WinUI types become Avalonia's: `Thickness`, `HorizontalAlignment`, `Colors`, `FontWeights` (Avalonia's
  `FontWeight`), `VirtualKey` (`Key`, with `Number1` as `D1`)…
  - The table is `src/Isotope.UI.Migration/Tables/winui-types.json`.
- Element members and factory parameters take Avalonia's names: `Value` is `Text`, `label:` is `content:`.
  - The table is `tools/naming/renames.json`.
- Factories, element types and modifiers take Avalonia's names: `TabView(Tab(...))` is `TabControl(TabItem(...))`,
  `NumberBox` is `NumericUpDown`, `StackElement` is `StackPanelElement`, `.HAlign(...)` is `.HorizontalAlignment(...)`.
  - The same table; it applies the entries marked `applied`, and leaves the `kept` ones (`VStack`, `HStack`, `MenuBar`)
    alone.
- Reactor's `icon: WindowIcon.FromPath(path)` becomes Isotope's icon factory.
- Reactor's `ScrollView(x)` becomes `(ScrollViewer(x) with { HorizontalScrollBarVisibility = ... Disabled })`. That is a
  ScrollViewer whose content is as wide as the view, as WinUI's ScrollView is. Its `ContentOrientation` is left for you
  to set as scroll bar visibilities.

What it reports, with no fix:

- WinUI-only controls (`TitleBar`, `NavigationView`, `ProgressRing`…), with an alternative.
- Reactor controls Isotope doesn't have yet (`src/Isotope.UI.Migration/Tables/reactor-controls.json`).
- Members and modifiers Isotope lacks.
- Shape changes the table can't apply mechanically (`GridElement.Definition`).
- WinUI startup (an `Application` with `OnLaunched`, `ReactorApp.OpenWindow`, `WindowSpec`).

The analyzer binds to Isotope's types, so it runs in the project *after* its references are switched to Isotope.

## Steps

1. **The project file.**
   - Set the target framework to `net10.0` or `net11.0` (`net9.0` works too, with `<LangVersion>14</LangVersion>`).
   - Remove `UseWinUI`, `WindowsPackageType`, `Platforms`, `TargetPlatformMinVersion` and
     `SupportedOSPlatformVersion`, and the WinUI packages (`Microsoft.WindowsAppSDK`, `CommunityToolkit.WinUI.*`).
   - Remove `App.xaml` and any XAML pages.
   - Reference Isotope instead of Reactor: `src/Isotope.UI.Avalonia`, and `src/Isotope.UI.Avalonia.Desktop` for
     `ReactorApp.Run<TRoot>(title, width, height)`. No packages are published yet, so these are project references.
   - Add the analyzer:

     ```xml
     <ProjectReference Include="path/to/src/Isotope.UI.Migration/Isotope.UI.Migration.csproj"
                       OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
     ```

2. **Apply the fixes.** Run this until it changes nothing:

   ```sh
   dotnet format analyzers MyApp.csproj --severity warn --diagnostics ISOMIG001 ISOMIG003 ISOMIG004 ISOMIG006 ISOMIG008 ISOMIG009 ISOMIG014
   ```

   It usually takes two runs. The member and parameter renames only bind once the namespaces are Isotope's.

3. **See what is left.** A build stops before analyzers while a file has an unresolved `using`, so the build shows
   compile errors only. To list the migration's diagnostics too, run:

   ```sh
   dotnet run --project tools/migration/MigrationReport -- MyApp.csproj
   ```

   An IDE shows them as you edit.

4. **Remove the analyzer** once the app builds.

## Rules

| ID | Fix | What |
|---|---|---|
| ISOMIG001 | yes | A Reactor namespace: `Isotope.UI.*` |
| ISOMIG002 | no | A Reactor namespace or type Isotope doesn't have (Charting, Docking, Advanced) |
| ISOMIG003 | yes | A WinUI using: removed, with the Avalonia namespaces the file's WinUI types need |
| ISOMIG004 | yes | A WinUI type or member with an Avalonia counterpart |
| ISOMIG005 | no | A WinUI type or member with no Avalonia counterpart, with what to use where there is one |
| ISOMIG006 | yes | An element member renamed (`renames.json`), or a shape change where the value fits the new member |
| ISOMIG007 | no | An element member whose shape changed, with the table's note |
| ISOMIG008 | yes | A factory parameter renamed (`renames.json`) |
| ISOMIG009 | yes | A type, factory or modifier renamed, when `renames.json` marks it applied |
| ISOMIG010 | no | A type, factory or modifier Isotope removed, with the table's note |
| ISOMIG011 | no | A WinUI-only control, with an alternative |
| ISOMIG012 | no | A Reactor control Isotope doesn't have yet |
| ISOMIG013 | no | A member or modifier Isotope doesn't have |
| ISOMIG014 | yes | `icon: WindowIcon.FromPath(path)` is `icon: () => new Avalonia.Controls.WindowIcon(path)` |
| ISOMIG015 | no | WinUI startup, or a Reactor startup Isotope doesn't have (several windows, `WindowSpec`) |
| ISOMIG016 | no | A WinUI name the migration's table doesn't list: Avalonia may still have it |

## On upstream's samples

`node tools/migration/measure-samples.mjs --out <empty folder>` measures the migration on upstream's sample projects, at
the commit in `UPSTREAM.lock`.

- It copies each sample out of the upstream cache and writes an Isotope project file.
- It compiles every project first, with the project's source generators, as a build does.
- It runs the fixes until nothing changes, then compiles each project again. The fixes run in process: what
  `dotnet format analyzers` does with the command above, without starting it for each pass (#87).
  `--fixer dotnet-format` runs `dotnet format` itself instead; the counts are the same.
- A file counts as building when the compiler reports no error in it.
- It also prints the commonest diagnostics and errors left.

### The baseline

The counts below are `tools/migration/samples-baseline.json`. CI's `samples migration` job measures again on every push,
and fails when a count differs from it (#71):

- A count that got worse fails the job, and the job names it. Fix the change; if it is intended (a new control can
  bring diagnostics for the members Isotope lacks on it), update the baseline and say why in the PR.
- A count that got better fails the job too, so the same PR updates the baseline, and the next change is measured
  against the new counts.
- Moving `UPSTREAM.lock` changes the samples, so the same PR updates the baseline.
- A branch push whose changes since its merge base with `main` are all docs skips the measurement: every changed
  file is under `docs/` or is a `*.md` file anywhere, and nothing else. The job still runs and reports, and its tests,
  this page's table check among them, still run. `main`, a manual run, and any push the comparison can't be made for
  are always measured.

To update it, measure with `--write-baseline`:

```sh
node tools/migration/measure-samples.mjs --out <empty folder> --write-baseline
```

- It writes the baseline and this page's table, and fails even then, so an update is a deliberate commit: review the
  diff and commit both files.
- `--check` instead runs CI's comparison.
- `--summary <folder>/summary.json` instead of `--out <folder>` checks or writes from an earlier run, without measuring
  again.
- Per-message counts (each message names a member or type) and compile errors per ID aren't checked: they move whenever
  a rename or a control lands, often while the totals get better. The run still prints the commonest.

<!-- samples-baseline: generated from tools/migration/samples-baseline.json by measure-samples.mjs --write-baseline -->
| | Before the fixes | After |
|---|---|---|
| Files that build | 51 of 358 | 108 of 358 |
| Compile errors | 13,699 | 2,936 |
| Projects that build | 1 of 36 | 2 of 36 |
| Files that built before the fixes and not after | | 0 |
| Failed `dotnet format` runs | | 0 |

| Migration diagnostics left | Count |
|---|---|
| ISOMIG002 | 199 |
| ISOMIG005 | 116 |
| ISOMIG007 | 14 |
| ISOMIG010 | 15 |
| ISOMIG011 | 171 |
| ISOMIG012 | 136 |
| ISOMIG013 | 401 |
| ISOMIG015 | 13 |
| ISOMIG016 | 57 |
| All | 1,122 |
<!-- end samples-baseline -->

- The files that build before the fixes use no Reactor or WinUI names (the chat sample's model, for one).
- What is left is mostly what Isotope doesn't have yet: members and modifiers (ISOMIG013), controls (ISOMIG012), and
  upstream's other libraries, Charting most of all (ISOMIG002). Then come WinUI-only controls and types (ISOMIG011,
  ISOMIG005). The run prints the commonest names for each, and most compile errors left are the same names, unresolved.
- The samples' other upstream projects (Charting, Advanced, Devtools…) and their NuGet packages are dropped, and the
  script lists them.
