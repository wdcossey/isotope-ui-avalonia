# Spike F: upstream's descriptor runtime on Avalonia, and generated descriptors for the first controls

Issue #12, the next step from Spike E (#8). Upstream baseline `14172dce` (UPSTREAM.lock), Avalonia 12.1.6, .NET 11 SDK 11.0.100.
Measured 2026-11-23 by an agent (Opus). Every number says how it was measured. Judgements are marked [J].

- **Half 1, the build errors:** `node spikes/F-descriptors/run-spike.mjs [0|1]`.
- **Everything else:** `node spikes/F-descriptors/measure.mjs` (needs `upstream/`).

## Answer

- **Upstream's descriptor runtime compiles on Avalonia with transforms only.**
  - All 6 runtime files (3,522 lines) are tier 2. Their only changes are 18 rewritten lines and 633 blanked ones: the WinUI `using` lines and 13 member drops.
  - 7 more upstream files they need are vendored too (1,354 lines). 4 files already vendored, but not compiled, now compile (401 lines).
  - Two upstream files are rewritten as tier 3 in 67 lines: `ReactorBindingT.cs` (284 lines upstream) and `ControlEventPayloads.cs` (495).
  - The reconciler gains 17 members under upstream's names, and `ReactorState` gains 5 fields (`Seam/Reconciler.V1.cs`, 211 lines).
- **The wrappers generator is vendored by `tools/sync` and built for Avalonia.** 15 files (4,411 lines) with 20 rules of ours on top of the namespace rename. They add 36 lines and remove 41, in 3 files.
- **The seam's hand-written mounts are gone.** Button, TextBlock, StackPanel, TextBox and Slider mount and update through generated descriptors (219 generated lines).
  - The seam's 48 tests pass unchanged, which is the equivalence gate.
  - 12 new tests cover TextBox, Slider, a derived record and the generator: 60 tests in all.

## Half 1: the runtime port

### Build errors (Spike A's method)

`run-spike.mjs` builds the 6 files with only the repo-wide rules (the namespace and event-provider renames) against the library as it was before this work (`b175b37`, taken from git so the counts don't move). `tools/build-errors.mjs` groups the errors.

| Step | Errors | What they are |
|---|---|---|
| 0: the library's aliases only | 75 | 20 WinUI namespace `using` lines; 54 unresolved types (49 upstream types from outside the 6 files, plus WinUI's `DependencyProperty` 3 and `RoutedEvent` 2); 1 constraint |
| 1: plus `DependencyProperty`→`AvaloniaProperty`, `DependencyObject`→`AvaloniaObject` | 72 | the two aliases remove the 3 `DependencyProperty` errors |

- As in Spike A, these are declaration errors. Roslyn reports no method-body errors while declarations are broken, so this isn't the full picture.
- **The missing names point at the closure:**
  - `ReactorBinding<>` 11, `Optional<>` 9, `TreeViewNodeData` 7, `ElementRef<>` 6;
  - `IV1HandlerEntry` 4, `IElementHandler<,>` 3, `PoolPolicy<>` 3;
  - `IDecoratorElementHandler<>` 2, `RoutedEvent` 2, `IV1SourceTargetResolver` 2, `IKeyedItemSource` 1, `IItemViewSource` 1.
- The full picture came from doing the port: every remaining error was then resolved by a transform, a closure file, or a member on our side.

### Outcome per file

Measured by `tools/measure-transforms.mjs` over the vendored files: line counts are upstream's (`wc -l`); "rewritten" and "blanked" are the lines that differ from upstream.

**The 6 runtime files (all tier 2):**

| File | Lines | Rules | Member drops | Rewritten | Blanked |
|---|---|---|---|---|---|
| `Descriptor/ControlDescriptor.cs` | 550 | 7 | 4: the 3 reference builders, `Immediate` | 2 | 60 |
| `Descriptor/PropEntry.cs` | 1,280 | 8 | 4: their entries | 2 | 240 |
| `ChildrenStrategy.cs` | 810 | 6 | 3: `TemplatedItemsErased`, `TemplatedItems`, `TreeChildren` | 8 | 232 |
| `ControlRegistry.cs` | 454 | 4 | 2: the decorator registrations | 2 | 99 |
| `Descriptor/DescriptorHandler.cs` | 248 | 2 | 0 | 1 | 1 |
| `MountContext.cs` | 180 | 3 | 0 | 3 | 1 |
| **Total** | **3,522** | | **13** | **18** | **633** |

- "Rewritten" includes the namespace line every file has.
- None of the 6 compiles unmodified: each imports `Microsoft.UI.Xaml`. `DescriptorHandler` needs nothing else.

**What they need, vendored (the closure):**

| File | Lines | Tier | Rules beyond the rename |
|---|---|---|---|
| `V1Protocol/V1HandlerAdapter.cs` | 535 | 2 | WinUI usings; a fully qualified `Panel` |
| `V1Protocol/ItemsHostReconciliation.cs` | 388 | 2 | WinUI usings; a CsWinRT assembly attribute dropped |
| `Core/ChangeEchoSuppressor.cs` | 188 | 2 | WinUI usings |
| `V1Protocol/IDecoratorElementHandler.cs` | 99 | 2 | WinUI usings (for `V1UnmountDisposition`) |
| `V1Protocol/IElementHandler.cs` | 94 | 2 | WinUI usings |
| `Core/PoolPolicy.cs` | 37 | 1 | none |
| `V1Protocol/ReportedUnmountFailureException.cs` | 13 | 1 | none |
| *already vendored, now compiled:* `Core/V1HandlerRegistry.cs` 162, `Core/Optional.cs` 116, `Core/ReactorBinding.cs` 76, `Core/Internal/IItemViewSource.cs` 47 | 401 | 1–2 | as before |

**Rewritten for Avalonia (tier 3, ours):**

| Upstream | Lines | Ours | Lines | Why |
|---|---|---|---|---|
| `V1Protocol/ReactorBindingT.cs` | 284 | `Seam/V1Protocol/ReactorBinding.cs` | 56 | Most of it is the typed WinUI input families (pointer, tap, key, focus) and reference edges. The runtime calls only `Control`, `OnCustomEvent` and `WriteSuppressed`, which keep upstream's names. |
| `V1Protocol/ControlEventPayloads.cs` | 495 | `Seam/V1Protocol/ControlEventPayloads.cs` | 11 | Its payloads hold WinUI delegate types. Only TextBox's is needed so far. |
| (the reconciler) | | `Seam/Reconciler.V1.cs` | 211 | The 17 members the runtime calls, plus the dispatch through `ControlRegistry` |
| | | `Seam/V1Protocol/ItemsList.cs` | 43 | The items-host adapter our generator rule emits (see below) |

- **The 17 reconciler members:**
  - tags and binding: `GetElementTag`, `SetElementTagIfNeeded`, `SkipsCallSiteOnlyTags`;
  - event payloads: `AnchorCustomEventTrampoline`, `GetOrCreateControlEventPayload`, `TryGetControlEventPayload`;
  - controls and children: `RentControl`, `ReturnControl`, `ApplySetters`, `ReconcileV1Child`, `ReconcilePanelChildrenInto`;
  - scopes and unmount: `PushContextDisposable`, `PushStaggerScopeDisposable`, `ResourceRefreshActive`, `IsolatesUnmountCleanupFailures`, `ReportIsolatedUnmountFailure`, `SkippingIsolatedTornDown`.
- **The 5 `ReactorState` fields:** the echo counter and scope depth, the value-diff arm, the event payload box, and the items-host state.
- **Stubbed as tier 3 behaviour:**
  - pooling (`RentControl` creates a control and `ReturnControl` drops it);
  - stagger scopes;
  - isolated unmount teardown (a failure propagates).

**Totals:**
- The library now compiles 82 vendored files (21,339 lines); it compiled 65 before.
- 81 of them are transformed: 191 lines rewritten and 1,665 blanked.
- 95 of the 117 rules in `transforms.json` hit them.

## Half 2: the generator and the swap

### The generator, vendored

`tools/sync` vendors `src/Reactor.Wrappers.Generator/*.cs` and `src/Reactor.SourceMap.Generator/SourceMapShared.cs`, which it links.
- 3 files are tier 2 (`WrapperGenerator.cs`, `WrapperGenerator.Controlled.cs`, `WrapElementSlotAnalyzer.cs`); the other 12 are tier 1.
- `src/Isotope.UI.Avalonia.Generators` (netstandard2.0, Microsoft.CodeAnalysis.CSharp 4.14.0) compiles them together with our partial `WrapperGenerator.Avalonia.cs` (90 lines: the helpers the rules call).
- The library references it as an analyzer, so nothing needs `upstream/` to build.

The 20 rules (all `gen-*` in `tools/sync/transforms.json`):

| Kind | Rules | What |
|---|---|---|
| Name table | 10 | target base `FrameworkElement`→`Control` and its message; `UIElement`→`Control` (7 sites in 2 files); member-walk cut-off WinUI `Control`→`TemplatedControl`; full-wrapper cut-offs; `RoutedEventHandler`→`EventHandler<RoutedEventArgs>`; `RoutedEventArgs`; `UIElementCollection`→`Controls`; template and resource exclusions→`IDataTemplate`, `IControlTemplate`, `IResourceDictionary`, `ITemplate<Panel>`; the polymorphic placeholder `TextBlock` |
| Logic hunks | 4 | the `{Prop}Property` lookup is any `AvaloniaProperty`; content discovery reads Avalonia's `[Content]` on the property; the two attached-state sites (grouped controlled bindings, the event owner) register through `AvaloniaProperty.RegisterAttached` |
| Ours | 6 | `[Obsolete]` filter; items host through a non-generic `IList`, plus the `ItemsList.Of` adapter it emits; the missing-counterpart check and its report (`ISOTOPEGEN001`); no `TryRegisterControlAssembly` |

`measure.mjs` diffs each vendored file against upstream, with the rename applied to both sides:
- `WrapperGenerator.cs` +29 −34, `WrapperGenerator.Controlled.cs` +6 −6, `WrapElementSlotAnalyzer.cs` +1 −1.
- 2 tier 1 files show +1 −1 each: the byte-order mark moves above the vendored header line. With them the script prints +38 −43.
- Spike E's hand patch was +36 −33 in 2 files. It had no missing-counterpart, items-host or `[Obsolete]` rule, and no partial file.

### Records and descriptors

- **The records are hand-written, in `Seam/ControlElements.cs` (166 lines).** They carry Reactor's member names, Avalonia's value types, and `[GenerateReactorDescriptor(typeof(Avalonia.Controls.X))]`.
  - TextBlock maps `Content`→`Text` with `[WrapAlias]`.
  - Button maps `Label`→`Content` and keeps `IsEnabled` manual.
  - StackPanel's record is unchanged.
  - TextBox and Slider are new, and mirror upstream's subset that Avalonia has.
- **Generated:** 5 files, 219 lines (Button 74, Slider 66, TextBox 29, StackPanel 27, TextBlock 23).
- **Hand-written descriptor code:** 3 `Customize` bodies, 30 lines.
  - Button's is empty, because the reconciler merges `IsEnabled`.
  - TextBox's has `AcceptsReturn` and `TextWrapping` before the controlled `Value`, as upstream's does. Unlike upstream's, they and the generated nullable members (`ClearValueOnUnset`) go through the ClearValue channel, so setting one back to null restores the control's default.
  - Slider's has Min and Max as `CoercingOneWay`, as upstream's does.
- **The reconciler** dispatches mount, update and unmount to the handler `ControlRegistry` resolves for the element type, or for its nearest base type: a record derived from a control's record mounts as its base, as the seam's type patterns allowed (upstream's registry matches the exact type). `MountButton`, `MountStack` and the inline TextBlock arms are gone (`Reconciler.cs` −36 +31).
- **Tests:** the 48 seam tests pass unchanged (`tests/` diff: 2 new files, the csproj and `expected-total.txt` only). 12 new tests make 60:
  - TextBox: a controlled value written and edits reported; no echo of a write and the next edit still reported; no echo of two writes pending together; nullable members set back to null restore the defaults; an unset value keeps typed text;
  - a record derived from TextBlock's mounts and updates as its base;
  - Slider: value pairing without echo; Min and Max written first, and a coercing bound that doesn't echo;
  - generator, through Roslyn's driver: a member with no Avalonia counterpart is reported; mapped members and callbacks aren't; an obsolete property isn't mapped; ListBox gets the items host and its generated code compiles.

## Leaks: where Avalonia differs

1. **TextBox raises `TextChanged` after the write returns, and does so for a control that isn't in a window yet.**
   - Upstream's TextBox descriptor uses the counter channel (`WriteSuppressed`). Here that reported the mount write as a user edit, and let a stale token swallow a real one.
   - The TextBox record uses upstream's value-diff channel with `armOnMount` instead (upstream uses it for RichEditBox), and its trampoline calls `ShouldSuppressEcho`. Tests pump the dispatcher, as a host's render tick would.
   - The value-diff arm holds one write. Two renders that write before the dispatcher runs leave two events that both read the latest text; the trampoline also drops a text equal to the element's controlled value (review pass 1 found this).
   - Not covered: a write a `Setters` action makes. `ApplySetters`' suppression scope has closed when the posted event arrives. `Setters` is internal and nothing sets it yet.
2. **The panel strategy mounts the children it is given.** Upstream's container factories flatten `GroupElement`s; the seam flattened them in `MountStack`. `StackElement.Children` now flattens on init, so slots and reference equality behave as before.
3. **`IsEnabled` has two sources** (the element's and the modifier). The reconciler already writes them as one value, so Button's descriptor leaves it alone. Upstream folds it into the descriptor.
4. **Slider behaves as upstream.** Coercing `Value` raises `ValueChanged` synchronously, and `CoercingOneWay`'s token drops it unchanged.
5. **Every mounted control is tagged** with its element. Upstream tags only controls with callbacks, keys or extensions, to save an allocation per leaf. `SetElementTagIfNeeded` always writes here.
6. **`GetElementTag(UIElement)` and `GetElementTag(FrameworkElement)` are one method:** both aliases are Avalonia's `Control`.
7. **Dropped runtime surface (tier 3):**
   - reference properties (`ElementRef` edges);
   - `Immediate` (per-instance property observation);
   - templated and virtualised lists;
   - TreeView nodes;
   - decorator handlers (flyouts);
   - pooling;
   - stagger;
   - isolated teardown.
   The drops leave two warnings in vendored files: CS0649 for `s_hasDecoratorRegistrations`, CS0169 for `_referenceSlotCount`.
8. **The member walk's cut-off is `TemplatedControl`,** which TextBlock and StackPanel don't derive from.
   - In descriptor-only mode this is harmless: members are filtered to the record's.
   - In full-wrapper mode they stop at `Control`, as WinUI's TextBlock stops at `FrameworkElement`.
9. **Avalonia's TextBlock marks `Inlines` as its `[Content]`.** It isn't a child slot, and discovery falls back correctly.
10. **The missing-counterpart check ignores delegate-typed members.** Callbacks are paired with events or read by `Customize`, so a callback with no event isn't reported. [J] Checking them needs a convention for which callbacks `Customize` owns.

## Decisions taken (the conservative choice; listed for review)

| What | Options | Chosen | Why |
|---|---|---|---|
| `ReactorBindingT.cs` | 13+ drop rules / tier 3 | **tier 3, 56 lines** | the dropped part is WinUI input, which is rewritten with Avalonia's input vocabulary anyway |
| Decorator registrations, reference and `Immediate` entries, templated and TreeView strategies | seam stubs / drop rules | **drop rules** | they need reconciler machinery that doesn't exist yet; a drop fails loudly if upstream changes the declaration |
| TextBox echo channel | upstream's counter / value-diff with `armOnMount` | **value-diff** | the counter fails on Avalonia (leak 1) |
| Generator tests | new test project and CI job / the library's test project | **library tests** (60 in all) | no CI change (a shared file); the test project references the generator as a plain assembly |
| Records derived from a control's record | exact type, as upstream / nearest registered base | **nearest base** | keeps what the seam's type patterns did |
| TextBox's nullable members | skip the write when null, as upstream / ClearValue channel | **ClearValue** | null then restores the control's default instead of leaving the old value |
| Roslyn for the generator | 5.0.0 (upstream) / 4.14.0 | **4.14.0** | not newer than the pinned SDK's compiler; Spike E's proof used it |
| Spike F's build | the library as it is / as it was before (`b175b37`) | **before** | the counts stay reproducible after the swap |

## For you to decide

1. **TextBox's echo channel.** Keep the value-diff channel (recommended), or make the counter work by subscribing after the first dispatcher pass.
2. **Tagging every control** (leak 5). Keep it (simpler, recommended for now), or port upstream's `NeedsTag` gate when performance work starts.
3. **`ISOTOPEGEN001` severity.** Warning (now) or error. [J] An error would stop a record silently losing members on a future sync.
4. **Pooling.** `RentControl`/`ReturnControl` don't pool. Port upstream's pool, or leave it until measured.
5. **A controlled value equal to the previous render's is skipped,** as upstream's `ShallowEquals` does. A box whose `OnChanged` ignores the user's text keeps that text until the value changes. Keep upstream's behaviour (as now), or decline the skip for a controlled value that has a value.
6. **The generator project targets netstandard2.0,** which Roslyn requires of analyzers, while CLAUDE.md says every project targets `net10.0`. Record the exception (see Proposed for shared files in the report back).

## Not done

- No other controls: the records cover only the 5 in scope.
- Hand-written handlers, decorators and templated lists aren't ported.
- No benchmark against the hand-written mounts.
- Items-host behaviour is proven only by the generated code compiling, not by a running ListBox.
