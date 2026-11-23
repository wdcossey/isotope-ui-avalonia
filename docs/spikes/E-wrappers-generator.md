# Spike E: can upstream's wrappers generator build our element/handler layer from Avalonia metadata?

Issue #8. Upstream baseline `14172dce` (UPSTREAM.lock), Avalonia 12.1.6. Research by an agent (Opus), 2026-11-23. Measured numbers say how they were measured; judgements are marked [J].

## Answer

**Yes, in descriptor-only mode** (the mode upstream now uses for **74** of its own built-in elements). The generator emits the descriptor and handler registration against a hand-written element record that keeps upstream's member names.

- A copy patched for Avalonia (36 lines added, 33 removed, after the `Isotope.UI` rename) ran against Avalonia 12.1.6 metadata and produced the same kind of output for Button, TextBox and Slider.
- **Still hand-written:** 46 of upstream's 74 records keep hand-coded entries (`[WrapManual]`, a `Customize` method, or both), and those stay hand ports for us.
- **Missing runtime:** the generated code targets upstream's `ControlDescriptor` runtime, which isn't ported yet. Porting it is the next step.

## What the generator reads and emits

`src/Reactor.Wrappers.Generator` is an `IIncrementalGenerator` (netstandard2.0, Roslyn only, no WinUI assembly reference).
- **Size:** 14 `.cs` files, 3,573 lines (`wc -l`), plus `Reactor.SourceMap.Generator/SourceMapShared.cs` (837 lines).
- **Attributes:** in `src/Reactor.Wrappers.Abstractions/WrapperAttributes.cs` (347 lines), which we already vendor.

**What it reads** (Roslyn symbols of the control named in the attribute; the member walk stops at WinUI `Control`):
- public settable instance properties (string/object, bool/int/double/enum, struct, `Nullable<T>`, reference);
- the static `{Prop}Property` field, used for ClearValue on Unset;
- public events and their delegate types;
- `[ContentProperty]`, or a property named `Content`;
- `Children` typed `UIElementCollection` (the panel strategy);
- `Items` implementing `IList<object>` (the items-host strategy);
- in descriptor-only mode, also the record's own declared members.

**Two triggers:**
- **`[GenerateReactorWrapper(typeof(C))]` (full wrapper):** record properties, controlled `Optional<T>` properties with `On{P}Changed`, the descriptor, registration and a factory, all named after the control's members.
- **`[GenerateReactorDescriptor(typeof(C))]` (descriptor-only, spec 058 §15 "P5"):** only the `ControlDescriptor<E,C>`, plus a static constructor that registers a `DescriptorHandler<E,C>`.
  - Each member the record declares maps to the control **by name**.
  - The entries it emits are `OneWay`, `OneWayConditional`, `Controlled` (`{P}` paired with a `{P}Changed` event), `HandCodedEvent`, `ImperativeBridged`, and `SingleContent`/`Panel`/`ItemsHost` children.
  - `[WrapManual]` members go to a partial `Customize(d)` that the author writes.

**Inference rules:**
- A two-way property is found by its `{P}Changed` event name, or declared with `[WrapControlled]`.
- `[WrapAlias]` renames a member, and `[WrapConvert]` maps a scalar onto a struct.
- `[WrapOneWay]`, `Exclude` and `Include` select members.

**Upstream's own use** (measured by splitting the 4 source files that carry the attribute at each `[GenerateReactorDescriptor]`):

| | Records |
|---|---|
| Built-in records with a generated descriptor | 74 |
| … fully generated (no `[WrapManual]`, no `Customize`) | 28 |
| … with at least one hand-coded entry | 46 (42 of them with a `Customize` body) |

- **Still fully hand-written upstream:** 9 files under `Descriptors/` and 9 under `Handlers/`, 1,749 lines in all (templated, virtualised and composite lists, Expander, overlays, navigation).
- **The records themselves** are hand-written, in `Core/Element.cs` (8,141 lines).

## How WinUI-bound it is (measured)

Method: grep over the 15 generator and abstractions files for WinUI identifiers (`Microsoft.UI.Xaml`, `Windows.Foundation`, `DependencyProperty`, `UIElement`, `FrameworkElement`, `RoutedEventHandler`, `TypedEventHandler`, `UIElementCollection`, `ContentPropertyAttribute`, `IXamlMetadataProvider`, …).

| File | Lines | Lines naming WinUI | of which code |
|---|---|---|---|
| WrapperGenerator.cs | 1,924 | 83 | 42 |
| WrapperGenerator.Controlled.cs | 198 | 5 | 5 |
| WrapperGenerator.Metadata.cs | 77 | 3 | 3 |
| WrapElementSlotAnalyzer.cs | 134 | 4 | 2 |
| WrapperAttributes.cs | 347 | 8 | 0 (doc comments) |
| the other 10 files | 1,240 | 0 | 0 |
| **Total** | **3,920** | **103** | **52** |

- 45 of the 52 code lines name WinUI only inside string literals: type names it compares against, or code it emits.
- No WinUI `using` appears anywhere, and the project references no WinUI assembly.
- The coupling is a table of names plus three pieces of logic.

## What an Avalonia version needs

**Inputs:**
- Roslyn can see everything the generator needs: CLR wrapper properties, static `StyledProperty<T>`/`DirectProperty<,>` fields, CLR events, `[Content]` on properties, `Controls` children, and `ItemCollection` items.
- Roslyn cannot see Avalonia's **runtime** metadata, such as the default binding mode. That is built in static initialisers and can only be read by reflection.

**Generator changes** (all done in the proof except the four marked *not done*):

| Kind | Change |
|---|---|
| Rename (existing rule) | `Microsoft.UI.Reactor` → `Isotope.UI` (66 lines in 5 files) |
| Name table, 14 one-line swaps | `UIElement`→`Control`; validity base `FrameworkElement`→`Control`; member-walk cut-off WinUI `Control`→`TemplatedControl`; full-wrapper cut-offs →`Control`/`Presenters.ContentPresenter`/`Panel`; `UIElementCollection`→`Controls`; `RoutedEventHandler`→`EventHandler<RoutedEventArgs>`; `RoutedEventArgs`; `TextBlock`; the template and resource exclusions → `IDataTemplate`, `IControlTemplate`, `IResourceDictionary`, `ITemplate<Panel>` |
| Logic hunk 1 | dependency-property lookup → any static field whose type derives from `AvaloniaProperty` (a 6-line helper). The emitted `c.ClearValue(XProperty)` is already valid Avalonia |
| Logic hunk 2 | content discovery: WinUI `[ContentProperty(Name=…)]` on the class → Avalonia `[Content]` on the property |
| Logic hunk 3 | attached state for the controlled and event-owner paths: `DependencyProperty.RegisterAttached` → `AvaloniaProperty.RegisterAttached<,,>` |
| *Not done:* items host | Avalonia `ItemCollection` implements `IList` and `IReadOnlyList<object>`, not `IList<object>` (checked by reflection), so ListBox, ComboBox, TabControl, Carousel and TreeView got no items strategy (0 of 5) |
| *Not done:* missing-counterpart diagnostic | descriptor-only mode **silently drops** record members with no same-name Avalonia property (see the proof) |
| *Not done:* `[Obsolete]` filter | full-wrapper TextBox surfaced obsolete `Watermark`, `WatermarkForeground` and `UseFloatingWatermark` |
| *Not done:* WinUI registration call | full-wrapper mode emits `ReactorApp.TryRegisterControlAssembly(...)`; drop it |

**Outputs:** our element records with upstream's member names and Avalonia value types, plus generated descriptors that write the Avalonia control's members.
- Name gaps are bridged with `[WrapAlias]` (Label→Content, Value→Text) or `[WrapManual]` (Min/Max).

**The runtime the generated code needs:**
- Six files, 3,522 lines: `ControlDescriptor`, `PropEntry`, `ChildrenStrategy`, `ControlRegistry`, `DescriptorHandler` and `MountContext`.
- Plus `Reconciler.GetElementTag` and `ReactorBinding.ShouldSuppressEcho`.
- **WinUI identifiers in those files:** 140 by grep. 113 are names our global usings already alias. The other 27: `DependencyProperty` 8, `ContentControl` 8, `RoutedEventHandler` 4, `ItemCollection` 3, `RoutedEventArgs` 2, `DependencyObject` 1, `ItemsControl` 1.
- [J] This runtime is a **tier 2** candidate, but compiling it hasn't been tested.

## Avalonia metadata a generator can use (measured)

Method: reflection over `Avalonia.Controls` 12.1.6 with the headless platform initialised.

**Totals:**
- 129 concrete public controls with a parameterless constructor.
- 1,238 public settable properties; 1,201 (97%) back onto a registered `AvaloniaProperty`.
- 72 properties default to `TwoWay` binding.
- 245 public events.

**Two-way pairing by event name:**
- **Found by the `{P}Changed` convention:** `Text`, `Value`, `IsChecked`, `SelectedDate`, `SelectedTime`, `DisplayDate`.
- **Missed:** `SelectedIndex`/`SelectedItem`/`SelectedValue` (the event is `SelectionChanged`), `Expander.IsExpanded`, `PipsPager.SelectedPageIndex`, `TreeView.SelectedItem`, `ListBoxItem.IsSelected`. These need `[WrapControlled]`, as on WinUI.
- **Over-paired:** `ProgressBar.Value` needs `[WrapOneWay]`.

**Mapping upstream's controls** (the 69 WinUI controls among the 74 generated records):

| | Controls |
|---|---|
| Avalonia control with the same name | 34 |
| Close Avalonia equivalent [J on equivalence] | 11: RichTextBlock→SelectableTextBlock, PasswordBox→TextBox, NumberBox→NumericUpDown, AutoSuggestBox→AutoCompleteBox, VariableSizedWrapGrid→WrapPanel, ScrollView→ScrollViewer, TabView and Pivot→TabControl, FlipView→Carousel, CalendarView→Calendar, IconElement→PathIcon |
| Nothing in Avalonia core | 24: RadioButtons, RatingControl, ColorPicker (separate package), ProgressRing, PersonPicture, WebView2, RichEditBox, NavigationView, TitleBar, BreadcrumbBar, TeachingTip, InfoBar, InfoBadge, MediaPlayerElement, AnimatedVisualPlayer, SemanticZoom, SelectorBar, AnnotatedScrollBar, SwipeControl, AnimatedIcon, ParallaxView, MapControl, Frame, ItemContainer |

Of the 45 with an Avalonia counterpart, upstream fully generates 17 and has hand-coded entries for 28.

## Proof (measured)

**Method:**
- Upstream's generator sources were copied into a throwaway project outside both repos and patched by a script with the changes above (36 added and 33 removed lines in 2 files, after the rename).
- The copy ran in process through `CSharpGeneratorDriver` (Microsoft.CodeAnalysis.CSharp 4.14.0; upstream pins 5.0.0), referencing net10.0 and Avalonia 12.1.6.
- Inputs: upstream's `WrapperAttributes.cs` (renamed), stand-ins for `Element` and `Optional<T>`, and annotated records.

**Results:**
1. **Descriptor-only mode** on records shaped like upstream's: 0 generator diagnostics.
   - Generated Button (70 lines), TextBox (64) and Slider (66).
   - Label was aliased onto Content.
   - TextBox `Value` was aliased onto `Text` and paired with `TextChanged`.
   - Slider `Value` was paired with `RangeBase.ValueChanged`, and Min/Max went to `Customize`, as upstream does.
   - **Silently dropped:** TextBox `Header` and `IsSpellCheckEnabled`, Slider `StepFrequency` and `IsThumbToolTipEnabled`. These are WinUI members with no Avalonia property of that name.
2. **Full-wrapper mode** over every distinct mapped Avalonia type: 42 of 42 generated, with 0 diagnostics.
   - Content slots found: 16. Panel strategies: 6. Controlled properties inferred: 17.
   - Items strategies: 0, because of the `IList<object>` gap.
3. **Compiling the output:** no descriptor runtime exists on Avalonia yet.
   - The 3-control run had 50 errors, all from the missing runtime namespace, its lambdas, or the author-written `Customize`.
   - The 42-control run had 392 errors, all of the same kinds.
   - Every Avalonia member the output names resolved.
   - **Runtime behaviour, including echo suppression, is not proven.**

Excerpt (descriptor-only Slider, trimmed):

```csharp
public static readonly ControlDescriptor<SliderElement, global::Avalonia.Controls.Slider> Descriptor =
    Customize(new ControlDescriptor<SliderElement, global::Avalonia.Controls.Slider> { GetSetters = static e => e.Setters })
    .OneWay<global::Avalonia.Layout.Orientation>(static e => e.Orientation, static (c, v) => c.Orientation = v)
    .OneWay<double>(static e => e.TickFrequency, static (c, v) => c.TickFrequency = v)
    .OneWay<global::Avalonia.Controls.TickPlacement>(static e => e.TickPlacement, static (c, v) => c.TickPlacement = v)
    .Controlled<double, global::Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs>(
        get: static e => e.Value, set: static (c, v) => c.Value = v,
        subscribe: static (c, h) => { /* owner-gated */ ((global::Avalonia.Controls.Slider)c).ValueChanged += (_, a) => h(c, a); },
        unsubscribe: static (fe, h) => { }, callback: static e => e.OnValueChanged, readBack: static c => c.Value)
    .ImperativeBridged(/* event-owner activation on mount/unmount */);
// + private static partial ... Customize(...)   (Min/Max: CoercingOneWay, hand-written, as upstream)
// + static SliderElement() => ControlRegistry.Register<SliderElement, Slider>(() => new DescriptorHandler<...>(Descriptor));
```

## Coverage of about 60 controls [J]

- **Generated descriptor plumbing:** the 45 upstream controls with an Avalonia counterpart. Items controls also need the `IList` fix.
- **Fully generated, no hand code:** about 17 to 20 of the 45.
- **Generated plus a hand-written `Customize`:** about 25 to 28. They carry the bespoke behaviour upstream also hand-codes: echo channels, write order, content/label polymorphism, commands, coercion.
- **Not covered:** the 24 with no Avalonia counterpart, and the templated, virtualised and composite controls upstream still hand-writes (1,749 lines).
- **Bonus:** full-wrapper mode gives cheap first wrappers for Avalonia-only controls (DockPanel, UniformGrid, Menu, Label, GridSplitter).

## What it means for sync

- **The generator becomes vendored tier 2:** the existing rename rule, about 14 one-line name rules and 3 multi-line hunk rules. Upstream's changes arrive by regeneration, and a hunk that stops matching fails loudly.
  - Upstream churn on it: 6 commits, +903/−104 lines in the 13 weeks to 2026-11-23, and 14 commits since it was added on 2026-06-11 (`git log --numstat`).
- **Descriptors stop being hand ports.** A new upstream record member, or a new generator capability, reaches us without a hand port when Avalonia has the member under that name; otherwise the proposed missing-counterpart diagnostic flags it.
- **The watched tier 3 surface narrows** from "74 descriptors and handlers" to about 42 `Customize` bodies, the hand-written list and composite handlers, and the records.
- **The records are the open question.** If they're hand-written for Avalonia, new upstream members need a hand edit. If they're projected from upstream's `Element.cs` through a WinUI→Avalonia type map, that needs Roslyn (#4).
- [J] It also moves our Reconciler onto upstream's `ControlRegistry`/`IElementHandler` protocol, which should make later engine ports easier.

## Recommendation

1. **Adopt descriptor-only generation,** with a vendored, transformed copy of upstream's generator, for every element with an Avalonia counterpart. `Customize` bodies stay tier 3 hand ports.
2. **Don't use full-wrapper mode for the built-in vocabulary,** since it exposes Avalonia's raw member set. Keep it for Avalonia-only or third-party controls later.
3. **Add four small generator rules:** the items-host `IList` fix, a missing-counterpart diagnostic, an `[Obsolete]` filter, and dropping `TryRegisterControlAssembly`.

**Next step:** a spike, "descriptor runtime on Avalonia" (#12), in two halves:
- **Measure the port:** link the 6 runtime files into the library with the existing aliases plus `DependencyProperty`→`AvaloniaProperty` and `DependencyObject`→`AvaloniaObject`, and count build errors as Spike A did.
- **Swap the seam over:** vendor the generator through `tools/sync` with these transforms, and replace the seam's `MountButton`/`MountStack` with generated descriptors for Button, TextBlock, StackPanel, TextBox and Slider. The seam's 48 tests are the equivalence gate.

## For you to decide

1. **Generated descriptors**, recommended, or keep hand-writing tier 3 descriptors and handlers.
2. **Where element records come from:** hand-written Avalonia records now (recommended for the first slice), or projected from upstream's `Element.cs` (needs #4).
3. **Member names where Avalonia's differ** (Label/Content, Value/Text, Min/Minimum, OnChanged/OnTextChanged). Recommended: upstream's names (through `[WrapAlias]`) on records shared with upstream, so upstream's `Customize` and factory code port nearly verbatim, and Avalonia's names on Avalonia-only controls. This touches the "Avalonia-native public API" decision.
4. **How the generator patch is carried:** as transform rules (recommended; hunks fail loudly), or as a tier 3 fork of `WrapperGenerator.cs` (1,924 lines).
5. **The 24 controls with no Avalonia counterpart:** drop them or plan new controls.

## Not done

- The generated output wasn't compiled against a runtime, because none exists on Avalonia yet.
- The runtime's portability is a grep count, not a build.
- The shares of generated versus hand-written *lines* weren't measured.
