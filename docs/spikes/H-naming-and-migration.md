# Spike H: Avalonia-named API, shorthand factories and a Reactor migration tool

Issue #13. Upstream baseline `14172dce` (UPSTREAM.lock), Avalonia 12.1.6. Research by an agent (Opus), 2026-11-23. Measured numbers say how they were measured; judgements are marked [J].

## 1. Which names differ (measured)

**Method:**
- A throwaway program (not committed) parsed upstream's `src/Reactor` with Roslyn (syntax only). It took the 74 records carrying `[GenerateReactorDescriptor]`, merging partial parts, and listed each record's constructor parameters and public `init` properties.
- Each WinUI control was mapped to its Avalonia counterpart: the same name, or one of Spike E's 11 close equivalents.
- Member names were compared by reflection over Avalonia 12.1.6 (public instance properties and events).
- For each difference a likely Avalonia name was written by hand, and reflection checked that it exists.

| | Members |
|---|---|
| Records with an Avalonia control | 46 records, 341 members |
| … same name as an Avalonia property or event | 158 |
| … callback `On{E}` where Avalonia has event `E` | 16 |
| … different name, Avalonia counterpart exists [J on equivalence] | **87, in 35 records** (18 of them callbacks) |
| … no Avalonia counterpart (`Header` ×10, ScrollView zoom/anchor, TabView drag/tear-off…) | 80 |
| Records with no Avalonia control (the 24 WinUI-only, FlexPanel, SemanticPanel, XamlPage, XamlHost) | 28 records, 177 members |

- **Commonest renames:**
  - `ContentElement`→`Content` ×9, `Label`→`Content` ×8;
  - `OnSelectedIndexChanged`→`SelectionChanged` ×5;
  - `Date`/`MinDate`/`MaxDate`→`SelectedDate`/`DisplayDateStart`/`DisplayDateEnd`;
  - `Min`/`Max`→`Minimum`/`Maximum`, `Value`→`Text`, `IsOn`→`IsChecked`.
- **Shape changes, not renames:** 35 of the 87 [J].
  - `Label` and `ContentElement` collapse into one `Content`.
  - `X1`/`Y1`/`X2`/`Y2` become `StartPoint`/`EndPoint`.
  - Three stroke caps become one `StrokeLineCap`.
  - `CheckedState` becomes `bool? IsChecked`.
  - `OnIsExpandedChanged` becomes `Expanded` plus `Collapsed`.
  - Some visibility enums become bools.
- **`[WrapAlias]` is not yet the list.**
  - Upstream uses it 3 times, all for WinUI names. We use it twice, plus 3 `[WrapManual]` name gaps, across our 5 records.
  - It becomes the complete list as records are ported, because `ISOTOPEGEN001` rejects a record member with no Avalonia counterpart unless it is aliased or manual.
  - `[WrapAlias]` plus `[WrapManual]` is therefore the rename table from here on, readable by a program.
- **Factories:**
  - Upstream's `Dsl.cs` has 206 factory methods with 141 distinct names.
  - Parameter names mirror record members (`label`, `value`, `min`, `max`, `onChanged`).
  - In upstream's 358 sample files, at most 107 named arguments and 24 `with` initialisers use a renamed or WinUI-only name. That is a regex upper bound. For scale, there are 2,178 calls to the 14 most-used control factories.
  - Positional calls never see parameter names, so renaming parameters breaks little [J].

## 2. Options for an Avalonia-named API

**A. Switch once at a version boundary.**
- **For:** one vocabulary; the generator maps by name and needs no aliases; simplest docs.
- **Against:** upstream code stops porting verbatim. Every tier 3 port of a record or `Customize` body goes through the rename table, which is mechanical if the table is data. The migration tool must ship at the same release.

**B. Both name sets, with `[Obsolete]` forwarding.**
- Works only for `init` properties.
- Factory parameters can't carry two names (overloads differing only in parameter names are illegal), and shape changes can't forward.
- The generator would map both members, so every alias needs an `Exclude`, and IntelliSense shows both names.
- [J] The costliest option for the weakest result.

**C. A separate namespace, for example `Isotope.UI.Native.Factories`.**
- Same records, with Avalonia-named factory parameters; could be generated from the rename table.
- **For:** cheap and opt-in.
- **Against:** `with { … }` and modifiers keep Reactor's names, so it is only half an Avalonia API, and docs would carry two dialects.

**Sync cost** [J]: A costs something on each upstream port, B on each member forever, C one generated file.

## 3. Shorthand factories (design sketch)

- **Placement:** `public static class Shorthand` in `Isotope.UI`, opt-in with `using static Isotope.UI.Shorthand;` next to `Factories`.
- **Content:** only names `Factories` lacks, each a one-line forwarder returning the same record (`Text(...)` → `TextBlock(...)`, `Stack(...)`, `Toggle(...)`). A same-name, same-signature method in both classes would be ambiguous (CS0121).
- **Avoid type names:**
  - Upstream's `Dsl.cs` records the trap: the `ScrollViewer` factory hides the type (CS0119), and the shape factory is `Path2D` rather than `Path`.
  - `Label`, `Panel`, `Image`, `Grid`, `Dock` and `Spinner` are `Avalonia.Controls` types; `Path` is in both `Avalonia.Controls.Shapes` and `System.IO`; `List` is `System.Collections.Generic.List<T>`.
  - Free: `Text`, `Stack`, `Toggle`, `Switch`, `Check`, `Row`, `Column`, `Spacer`, `Icon`, `Link`, `Tabs`.
- **Docs and samples stay explicit-only.**
- **Alternative:** a did-you-mean analyzer suggesting `TextBlock` for an unknown `Text(`. Upstream's `FuzzyFactoryNameAnalyzer` (REACTOR_DYM_003) deliberately skips `Text`→`TextBlock`, so this would be a small analyzer of our own with a table.

## 4. What a Reactor → Isotope migration needs

1. **Namespaces:** 33 distinct `Microsoft.UI.Reactor*` usings in 283 of upstream's 358 sample files. One regex, like the existing `namespace-isotope` rule.
2. **WinUI types:**
   - 26 distinct WinUI/`Windows.` usings in 215 files, plus 432 fully qualified WinUI names.
   - Most used: `VerticalAlignment` 149, `HorizontalAlignment` 146, `TextWrapping` 87, `Color.FromArgb` 40, `VirtualKey` 40, `new SolidColorBrush` 26.
   - This needs a new WinUI→Avalonia type table; only 5 of the rules in `tools/sync/transforms.json` apply to user code.
3. **Renamed members:** only with option A or C; 87 table entries, and the 35 shape changes need hand-written fixes [J].
4. **WinUI-only:** the 24 controls and 80 members without counterparts each get a diagnostic with no fix, naming an alternative.
5. **Hosting:**
   - **Upstream samples:** 30 call `ReactorApp.Run<T>(title, w, h)`, 7 call `Run(...)`, 7 call `OpenWindow`.
   - **Ours:** `ReactorApp.Run(AppBuilder, title, render, args, w, h)`. With no overload taking a component type, these can't be rewritten.
   - [J] Add `Run<TRoot>` and a default app builder, so hosting migrates by namespace alone.
6. **Project file:** swap the Windows App SDK for the Avalonia packages, change the TFM from `net10.0-windows…` to `net10.0`, and remove `UseWinUI` and `App.xaml`. These aren't C# edits.

**Forms:**
- **Roslyn analyzer + code fixes (`Isotope.UI.Migration`):**
  - Added to the Reactor project before switching packages. Fixes bind to Reactor's symbols, which still compile there, so renames are exact, not textual.
  - Runs in the IDE (Fix All) and from the command line with `dotnet format analyzers --diagnostics …`.
  - Models: upstream's `FuzzyFactoryNameAnalyzer` (257 lines), its code fix (55) and tests (367).
- **A `dotnet` tool (MSBuildWorkspace):** could also handle the project file, but needs the MSBuild locator and has to load WinUI projects on Windows: more code for the same C# edits [J].
- **Both need Windows,** because the source project only compiles there.

**Cheapest viable form [J]:** the analyzer + code fixes, about 1,500 lines plus the type and rename tables, and about 1,000 lines of tests; the project-file step as a documented checklist or a script of about 100 lines.

## Recommendation

1. Keep Reactor's member names (the current decision). Don't do B.
2. Turn the rename table into data now: have the generator, or a test, emit `[WrapAlias]`/`[WrapManual]` as a table. Every later option needs it, and `ISOTOPEGEN001` already forces it to be complete.
3. If Avalonia names become a goal, switch once (A) at a version boundary, with the analyzer in that release. C is the cheap trial first.
4. Build the migration tool as an analyzer with code fixes through `dotnet format`. Start with namespaces, the WinUI type table and the WinUI-only diagnostics, which are worth having even with Reactor's names. Add `ReactorApp.Run<TRoot>` and a default app builder.
5. Defer shorthand. If it comes, use a `Shorthand` class with free names only, or a did-you-mean analyzer instead.

## For you to decide

- Are Avalonia member names ever a goal? If not, the tool covers only namespaces, types, WinUI-only controls and hosting.
- If they are: which version boundary, and whether to trial option C first.
- Should `ReactorApp.Run<TRoot>` and a default app builder be added?
- Shorthand: a `Shorthand` class, a did-you-mean analyzer, or neither?
- Is the migration tool in scope before 1.0?
