# Spike J: third-party controls in Isotope, from outside the repo

Issue #75. Built and measured 2026-11-25 by an agent (Opus) on Windows: .NET SDK 11.0.100, Avalonia 12.1.6 headless with
the Fluent theme, and Isotope's packages packed from this branch (`0.1.0-preview.0.72`, the library's code as of
`bfa57e1`). Code: `spikes/J-third-party/` (its README says how to run it). Measured numbers say how they were measured;
judgements are marked [J].

**Since the spike:** gaps 1 and 6 are closed by #77 (2026-11-25). A library's element is skipped as a built-in one is,
through `Element.ShallowEqualsCore` and `HasCallbacks`, which the bundled generator writes (gap 1). A token is resolved
for a library's control with no new API (gap 6). The spike's two `Gap_vendor_...` tests became
`Vendor_a_rejected_edit_stays_...` and `Vendor_an_unchanged_element_is_skipped`, and the `InternalOverride` and
`InternalEquality` probes were updated. Gaps 2 and 3 are closed by #78 (2026-11-25): the bundled generator reports a
callback it never wires (`ISOTOPEGEN002`), a callback that can't take what its event passes (`ISOTOPEGEN003`) and
`[WrapDecorator]` / `[WrapPolymorphic]` (`ISOTOPEGEN004`) at the record, with nothing wrong left in a generated file, and
a record without `Setters` builds. The `NoSetters`, `UnwiredCallback`, `WrapEventMismatch` and `Decorator` probes were
updated. Since #80 the spike is the worked example of [Writing controls for Isotope](../guide/writing-controls.md): its
C# blocks are regions of this code, and `RatingBar` gained a plain .NET event (`Cleared`, wired with `[WrapEvent]`) and
its record a `[WrapManual]` member, with 5 tests of the guide's examples (39 on each framework). The measurements below
are the spike's own, from before #77, except where marked.

## Answer

- **Yes: a third party can wrap its own controls using only Isotope's published packages,** and they behave like
  built-in ones in nearly every check. The bundled generator runs in the consumer's build on net9.0, net10.0 and net11.0
  (a `netstandard2.0` assembly, added to `analyzers/` from the library's net10.0 build; the compiler loads it whatever
  the target), and everything it emitted for a plain
  control, a templated control and a panel uses only public API. Nothing needed `InternalsVisibleTo`.
- **One behaviour differs:** a third party's element is never skipped when it equals the last render's, because the
  skip test (`Element.ShallowEquals`) is internal and has an arm only for each built-in record. So a value the handler
  rejects is written back on the next render (a built-in keeps it), and every render of the parent updates the
  control. This needs a change to the library's public surface (Gap 1).
- **The rest is traps a guide or diagnostics can remove:** callbacks that are silently never wired, errors reported
  inside generated files, a `Group` inside a third party's panel (closed by #79), and factory names that clash (Gaps 2
  to 6).
- **Recommendation: (c), then (a).** Public-surface and generator changes first (four follow-up issues), then the
  "Writing controls for Isotope" guide. A compatibility package (b) isn't needed yet: the generator, which is where the
  diagnostics belong, already ships in the library package. See **For you to decide**.

## What was built

- **`Vendor.Controls`**, an Avalonia library with no Isotope reference: `RatingBar` (a plain `Control`, drawn in
  `Render`; `Value` coerced to `Maximum`, which Avalonia validates to 1..50; `ValueChanged` as an
  `EventHandler<RatingChangedEventArgs>`) and `SpreadPanel` (a `Panel`).
- **`Vendor.Controls.Isotope`**, what the vendor ships for Isotope: `CalloutCard` (a templated `ContentControl` with
  `Header`, `Accent` and a routed `Dismissed` event, declared in this assembly), and a partial element record per
  control with `[GenerateReactorDescriptor]`, following `tools/naming/README.md`. So one record wraps a control from
  metadata (another assembly) and one a control from source. Factories are in `VendorFactories`, and modifiers in
  `VendorElementExtensions` (`.Fill`, `.Accent`, each taking a brush or a theme token, and `.Set`, the escape hatch).
- **`Vendor.Controls.Tests`**: 34 headless tests on each framework, in a shown headless window so templates apply,
  next to Isotope's built-ins. Tests named `Gap_...` pin a difference as it is now, so they fail when it closes.
- **`Probes/`**: 8 projects that must fail (or pass) in a given way, listed in `Probes/probes.json`.
- **`run.mjs`**: packs the branch (or takes `--packages`), restores into a fresh `NUGET_PACKAGES` through a
  `nuget.config` whose `packageSourceMapping` takes `Isotope.*` from the packed folder only, builds and runs the tests on
  each framework, builds each probe and checks its diagnostics per framework. The folder's own `Directory.Build.props`
  stops the repo's build files from applying, as the package smoke test's does; warnings are errors (so an analyzer that
  can't load, CS8032, would fail the build).

The projects set no `LangVersion`: each framework's default (C# 13 on net9.0, 14 on net10.0, 15 on net11.0) [measured:
all build]. Unlike an app calling `ReactorApp.Run` (a C# 14 extension member), a control library on net9.0 needs no
`LangVersion 14`.

## Measured

`node spikes/J-third-party/run.mjs --packages <packed folder>`, 3 invocations in a row on the same machine, on the final
spike: 55 to 56 s each (`dotnet build` with `--no-incremental`, then `dotnet test`, then 8 probe builds), all three
exit 0 with the same results. A fresh clone of the branch, packing its own packages (`run.mjs` with no `--packages`,
version `0.1.0-preview.0.75`), took 65 s with the same results:

| | net9.0 | net10.0 | net11.0 |
|---|---|---|---|
| Vendor library and tests build (warnings as errors) | yes | yes | yes |
| Tests | 34 of 34 | 34 of 34 | 34 of 34 |
| Probes as expected (`probes.json`) | 8 of 8 | 8 of 8 | 8 of 8 |

### What works as on a built-in control (tests pass, no change needed)

| Check | Tests |
|---|---|
| **Generator:** both descriptors are written and registered (`Descriptor` exists only if the generator ran in the vendor's build); the record wrapping a control from another assembly works as well as the one declared beside its control | `The_bundled_generator_wrote_and_registered_both_descriptors` |
| **Mount:** the plain control gets its values, Isotope's modifiers (`Margin`, `Width`, `ToolTip`) and the escape hatch; the templated one its header, content and template (its parts found in the visual tree), `Padding` and `FontSize` | `A_plain_control_...`, `A_templated_control_...` |
| **Update in place:** a state change updates both controls, same instances, one render | `A_state_change_updates_both_controls_in_place` |
| **Reference members back to null** clear the control's value, with `ClearValueOnUnset = true` on the record (as Isotope's Border) | `A_member_set_back_to_null_clears_the_controls_value` |
| **Controlled value:** `Value` paired with `ValueChanged` by name; a user change calls back once, and the engine's own write doesn't echo; a new value is written; an Unset value leaves the control its own | `A_user_change_calls_back_...`, `Vendor_a_new_controlled_value_...`, `Vendor_an_unset_value_...` |
| **Events:** a routed event becomes `Action? OnDismissed` and calls the latest render's closure, from code or from the template's button; unmounting detaches both callbacks | `A_routed_event_...`, `Unmounting_detaches_the_callbacks` |
| **Keyed reorder:** a reverse of 5 keyed ratings in a `VStack` keeps the instances in the new order with 0 `DetachedFromVisualTree`; a reverse of 3 keyed cards keeps each card's content state, and a focused button inside one card's template keeps focus | `A_keyed_reverse_moves_...`, `A_keyed_reverse_of_cards_...` |
| **Keyed reorder in the vendor's own panel** (the generated `Panel` children strategy): 4 cards reversed with 0 detaches and their state kept; keyed adds and removes keep the others; an `Empty()` child is left out; a `Group`'s children mount, unkeyed and keyed, and keyed ones in a group reverse with the same controls (#79) | 4 of `PanelTests`' 5 |
| **Content slot:** a component in the card keeps its state (2 clicks) while the card's header and accent change, same button instance; another kind of content replaces the child and keeps the card; cards nest | `A_component_in_the_content_slot_...`, `Another_kind_of_content_...`, `Cards_nest_...` |
| **Error boundaries:** a boundary around a card catches its content's throw; a boundary inside the content slot keeps the card and recovers on the next render; the vendor control's own throw (Avalonia's `ArgumentException` for `Maximum` 0) is caught on mount and on update; a throw in the escape hatch is caught | `ErrorBoundaryTests` (5) |
| **Theme tokens:** `.Background`, `.Foreground` and `.BorderBrush` with a token reach a templated control and follow a light-to-dark switch with no re-render; a token on the vendor's own brush properties (`Accent`, `Fill`) does too, through the public `Element.ThemeBindings`; the element's own brush wins and the token returns when it goes; a token for a property the control lacks is skipped | `ThemeTokenTests` (6) |

Proven to fail with the behaviour broken (on the committed spike, net10.0, each break reverted after):

| Break | Tests that failed |
|---|---|
| `RatingBar` raises no `ValueChanged` | 2: `A_user_change_calls_back_...`, `Gap_vendor_a_rejected_edit_...` |
| `ClearValueOnUnset` removed from the card's record | 2: `A_member_set_back_to_null_...`, `The_elements_own_brush_wins_...` |
| `.Accent(token)` binds a misspelt property | 2: `A_token_on_the_vendors_own_brush_property_...`, `The_elements_own_brush_wins_...` |
| The keyed ratings lose their keys | 1: `A_keyed_reverse_moves_...` |
| The card's content keyed by its header (so it remounts when the header changes) | 2: `A_component_in_the_content_slot_...`, `A_state_change_updates_...` |
| `SpreadPanel(...)` flattens a group itself (before #79) | 1: `Gap_a_group_element_inside_the_vendors_panel_throws` (since renamed by #79) |
| The generated strategy doesn't flatten (#79's rule reverted; each framework) | 1: `A_group_element_inside_the_vendors_panel_...` |
| The render returns the same rating instance each time (so it is skipped) | 1: `Gap_vendor_an_unchanged_element_...` (1 update, not 11) |

### Probes (each framework)

| Probe | What it is | Result |
|---|---|---|
| `Gen001` | A record member with no counterpart on the control | **ISOTOPEGEN001**, an error at the record, as inside the repo |
| `NoSetters` | A record with no `Setters` member | CS1061 **inside the generated file**: the descriptor reads `e.Setters` unconditionally. **Since #78:** builds clean, and the descriptor has no `GetSetters` |
| `UnwiredCallback` | `Action? OnClosed` for a plain `EventHandler Closed` event | **builds clean**, and the generated descriptor never wires `OnClosed`: the callback is silently dropped (since #77 the generated skip members name it, so the probe looks for the descriptor's wiring, `el.OnClosed`). **Since #78:** **ISOTOPEGEN002**, an error at `OnClosed` |
| `WrapEventMismatch` | The same with `[WrapEvent("Closed")]` | CS1501 inside the generated file: `[WrapEvent]` passes the args, so the callback must be `Action<EventArgs>`. **Since #78:** **ISOTOPEGEN003** at `OnClosed`, saying so, and the event isn't wired |
| `Decorator` | `[WrapDecorator]` (a control made and changed by the author's own methods) | CS0117 inside the generated file: `Reconciler.SetElementTag`, `DetachReactorState` and `ControlRegistry.RegisterDecorator` don't exist (decorators were left out in Spike F, tier 3). **Since #78:** **ISOTOPEGEN004** at the attribute, and nothing is generated |
| `InternalOverride` | `override HasCallbacks`, as built-in records do | CS0115: internal. **Since #77:** the record overrides the protected `HasCallbacks` and `ShallowEqualsCore` and builds clean, and the generator writes neither |
| `InternalEquality` | Calling `Element.ShallowEquals` | CS0117: internal. **Since #77:** still CS0117, and CS0122 for a call to the protected `ShallowEqualsCore` from outside the record |
| `NameClash` | A second `Card(Element)` factory beside Isotope's, and `RatingBar.ValueProperty` with the factory imported | CS0121 (ambiguous call) and CS0119 (the factory hides the control type) |

`InternalOverride` and `InternalEquality` are two probes because Roslyn binds no method body once a declaration has an
error: in one project only CS0115 was reported.

## Gaps, each with the change it needs

1. **Closed by #77.** **No skip for a third party's element (behaviour).** `Element.ShallowEquals` ends in `_ => false`, and its arms are
   internal, one per built-in record. Measured: a rating whose handler rejects the user's 4 is back at 2 after an
   unrelated render, where a built-in slider in the same tree keeps the rejected 30; an unchanged rating element is
   updated on all 11 of 11 renders. Counted by its `.Set`, made once outside the render, so each render's element is a
   new instance equal to the last, with the same `Setters` array, as a built-in arm compares it; returning the very same
   instance is skipped (1 of 11), since `ShallowEquals` checks reference equality first. Upstream's rule (FINDINGS, decisions after Spike F, 3)
   holds for built-ins only.
   - **Change:** a public way for a record to take part: for example a `protected virtual bool ShallowEqualsCore(Element
     other)` on `Element` that `ShallowEquals` calls for a type it has no arm for, and `HasCallbacks` made protected,
     with the generator emitting both in descriptor-only mode (the record's mapped members, `Setters` by reference, and
     which callbacks are present). Library: `Seam/Element.cs`; generator: `WrapperGenerator.Avalonia.cs`. [J: the
     generator already knows the mapped members, so a record author writes nothing.]
2. **Closed by #78.** **A callback silently never wired.** In descriptor-only mode an `On{Event}` member is wired only for a routed event
   (`EventHandler<RoutedEventArgs>`), a `[WrapEvent]`, or a controlled value's change event. A plain `EventHandler` or
   another delegate gets nothing, and `ISOTOPEGEN001` skips delegate members (`UnwiredCallback`).
   - **Change:** `ISOTOPEGEN001` (or a new ID) also reports an `On{X}` delegate member that the descriptor doesn't wire,
     saying to use a routed event or `[WrapEvent]`. Our partial, `WrapperGenerator.Avalonia.cs`.
   - **#78:** `ISOTOPEGEN002`, an error at the member. A callback the author wires (`[WrapManual]` on it, or its event or
     value `[WrapManual]` or `Exclude`d, as Isotope's own records do) isn't reported.
3. **Closed by #78.** **Errors reported inside generated files** (`NoSetters`, `WrapEventMismatch`, `Decorator`): the author sees a
   CS error in a `.g.cs` they didn't write.
   - **Change:** diagnostics at the record: a missing `Setters` (or emit `GetSetters` only when the member exists, as
     the polymorphic path already checks), a callback type that doesn't match the event, and `[WrapDecorator]` /
     `[WrapPolymorphic]` not supported on Isotope yet. Our partial.
   - **#78:** the descriptor reads `Setters` only when the record declares them (no diagnostic: `Setters` is optional, as
     on the polymorphic path); `ISOTOPEGEN003` at a callback whose delegate can't take what its event passes, which is
     then left unwired; `ISOTOPEGEN004` at `[WrapDecorator]` or `[WrapPolymorphic]`, with nothing generated.
4. **Closed by #79: the generated `Panel` children strategy flattens groups** (`PanelChildren.Of`, a
   public helper beside `ItemsList`; `Reconciler.Flatten` stays internal), and the test now asserts the children mount.
   As found: **a `Group` inside a third party's panel throws** `NotSupportedException` ("A GroupElement is flattened into its
   container's children"), caught by a boundary in the test. Isotope's own panel records flatten their children with
   `Reconciler.Flatten`, which is internal; `VStack(..., Group(...))` mounts 3 ratings. The workaround then, measured (the
   break above), is no longer needed since #79: the library flattened `GroupElement.Children` itself.
   - **Change:** flatten in the generated `Panel` children strategy, so no record needs to, or make `Reconciler.Flatten`
     public. [J: the first, since a library can't forget it.]
5. **Name clashes** (`NameClash`): a library factory with the name and parameters of one of Isotope's is ambiguous
   under both `using static` (CS0121), and a factory named after its control hides the type in member access (CS0119),
   as Isotope's own `Button` does.
   - **Change:** docs: check a library's factory names against Isotope's, and alias the control type
     (`using RatingBarControl = Vendor.Controls.RatingBar;`). The spike names its factory class `VendorFactories`, not
     `Factories`, so a file importing both namespaces can still name each class [J: CS0104 not probed].
6. **Closed by #77, with no new API.** **Resolving a token for one control is internal.** `ThemeRef.Resolve(string, Control)` is internal; the public
   overload resolves for the app's light or dark theme only. Binding a token to a brush property needs neither (gap-free,
   above); a library resolving a token itself, in a setter, does.
   - **Change:** make that overload public [J: small]. Not needed for anything the spike's controls do.
   - **#77:** no new API. A library binds a token to its control's own brush property (`.Accent(Theme.Accent)`, through
     the public `Element.ThemeBindings`), which follows a theme switch with no render. A setter runs only on a render, so a
     token resolved there would keep the old theme's brush after a switch. To read a token's brush once, the public
     `ThemeRef.Resolve(key, isDark)` takes the control's variant: `A_token_resolves_for_a_controls_theme_...` checks that it
     gives the bound brush. It doesn't see the resources of the control's subtree or a host's stand-in colour values,
     which the internal overload does. That case waits until a library needs it.

Not gaps, but traps for the guide: reference members are written only when set unless the record sets
`ClearValueOnUnset = true` (the break above shows the difference); and the generated descriptor writes the one-way
members before the controlled one (read from the generated source: `Fill`, `ItemSize`, `Maximum`, then `Value`), so a
coercing bound lands first.

## The rename table for a library porting from Reactor

The migration analyzer reads one table, `renames.json`, embedded in its assembly (`Tables.cs`), and the MCP server's
`map_reactor_name` and `migrate_code` use the same tables. Neither takes another table. A Reactor-based library
porting to Isotope would need its own record, member and factory renames applied to its users' code.

- **It can wait** [J]: nothing in the spike needs it, and no such library has asked. When one does: the analyzer could
  read `AdditionalFiles` named `*.renames.json` (Roslyn's standard way to give an analyzer data), in the same format,
  merged after Isotope's; the MCP server could take the same files with an option.

## Recommendation

**(c), then (a):**

1. Public surface and generator first, as four follow-up issues:
   - Gap 1, the skip for a third party's element: `Element` and the generator;
   - Gaps 2 and 3, diagnostics at the record: the generator partial only (done, #78);
   - Gap 4, flattening in the generated panel strategy (done, #79);
   - Gap 6, the public `ThemeRef.Resolve` overload (or fold it into the guide's issue if you'd rather not widen the API).
2. Then the guide, "Writing controls for Isotope", with the spike's library as its worked example: the record rules
   (`Setters`, member names, `ClearValueOnUnset`, routed events), factory and modifier naming, theme tokens on a
   library's own brushes, and Gap 5.
3. Run the spike in CI meanwhile, so the outside view is checked on every push. Done: the `packages` job runs it on
   Ubuntu and Windows (2026-11-25, after #75; the step took about 2 minutes on Ubuntu).

Why not (b) now [J]: the generator, where every diagnostic above belongs, already ships in the library package, so a
second package would add a dependency for consumers with nothing in it yet but a `dotnet new` template. A template can
come with the guide if a library asks for one.

## Limits

- **A run once failed after passing.** Before `run.mjs` retried its clean-up, 2 of its 10 invocations exited 1 after every
  test had passed (the two folders it left behind confirm the count): deleting the temporary `NUGET_PACKAGES` hit EPERM on
  `Avalonia.BuildServices.Collector.dll`, which Avalonia's build telemetry still held. It now sets
  `AVALONIA_TELEMETRY_OPTOUT`, retries the delete, and warns instead of failing, as `pack.mjs` does.
- **Headless.** Measured on Windows for this report; CI's `packages` job now runs it on Ubuntu and Windows too, with
  the same results.
- **Not covered:** an items control (`Items`), secondary element slots (`[WrapElementSlot]`), more than one controlled
  property on a control, a hand-written handler registered with `ControlRegistry.Register`, and the migration tool on a
  library's code.
- **One version of the packages**, packed from this branch; a published version wasn't used (none exists yet).
