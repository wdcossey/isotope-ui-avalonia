# Migrating from Reactor

Isotope keeps Reactor's model, hooks and most of its names, so a Reactor app moves over mostly mechanically. The
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
  - Type, factory and modifier renames apply once the table marks them `applied`.
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
   - Set the target framework to `net10.0`.
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

`node tools/migration/measure-samples.mjs --out <empty folder>` measures the migration on upstream's 36 sample
projects (358 C# files), at the commit in `UPSTREAM.lock`.

- It copies each sample out of the upstream cache and writes an Isotope project file.
- It compiles every project first, with the project's source generators, as a build does.
- It runs the fixes until nothing changes, then compiles each project again.
- A file counts as building when the compiler reports no error in it.
- It also prints the commonest diagnostics and errors left.

Measured on 2026-11-24:

| | Before the fixes | After |
|---|---|---|
| Files that build | 51 of 358 | 64 of 358 |
| Compile errors | 13,699 | 4,202 |
| Projects that build | 1 of 36 | 2 of 36 |

- No file that built before the fixes fails after them, and no `dotnet format` run failed.
- The 51 files that built before use no Reactor or WinUI names, such as the chat sample's model.
- The two projects that build after are the hello-world app and the chat sample's model.
- Diagnostics left: 2,624.
  - 1,192 ISOMIG013: members and modifiers Isotope lacks. The commonest are `FontSize` (193) and `SemiBold` (154) on
    `TextBlockElement`, and `Padding` (88) on `StackElement`.
  - 877 ISOMIG012: controls not ported yet. The commonest are `Caption` (221), `SubHeading` (149) and
    `ScrollView` (139).
  - 199 ISOMIG002: Charting (159), Advanced (11), Docking (8), Controls.Validation (8), Animation (7), and 6 in
    Markdown, Diagnostics, Interop and Devtools.
  - 171 ISOMIG011 and 113 ISOMIG005: WinUI-only controls and types.
  - 59 ISOMIG016: WinUI names the table doesn't list.
  - 13 ISOMIG015: startup.
- Most compile errors left are names Isotope doesn't have yet. The largest group is 443 members `TextBlockElement`
  lacks (CS1061). Reactor's `Theme` tokens (830 errors before #41) now migrate as they are.
- The samples' other upstream projects (Charting, Advanced, Devtools…) and their NuGet packages are dropped, and the
  script lists them.
