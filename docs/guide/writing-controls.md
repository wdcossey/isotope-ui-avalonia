# Writing controls for Isotope

An Avalonia control library can be used from Isotope components with no change to its controls. Alongside the
controls you ship one more assembly with an element record per control, its factories and its modifiers, and the
generator in Isotope's package writes the rest. Your users then write your controls as they write Isotope's own,
`RatingBar(stars, onValueChanged: setStars)`, and the controls mount, update in place, move by key and call back as the
built-in ones do.

The worked example is a small library built outside this repository against Isotope's packages alone, as a third
party's would be: a plain control, a templated control and a panel (`spikes/J-third-party/`, with its report in
[Spike J](../spikes/J-third-party-controls.md)). Every C# block on this page is a region of that code. `run.mjs` builds
it and runs its tests against the packed packages on `net9.0`, `net10.0` and `net11.0` (CI's packages job does this on
every push), and `DocSnippetTests` fails if the page and the code drift apart. The names (`Vendor.Controls`,
`RatingBar`…) are made up.

## What you ship, and what your users write

| Project | What it holds |
|---|---|
| `Vendor.Controls` | The controls, an Avalonia library that knows nothing of Isotope: `RatingBar` (a plain control, drawn in `Render`) and `SpreadPanel` (a `Panel`). |
| `Vendor.Controls.Isotope` | What you ship for Isotope: an element record per control, the factories (`VendorFactories`) and the modifiers (`VendorElementExtensions`). A control can be declared here too: `CalloutCard`, a templated `ContentControl`, is. |

A component file imports your factories next to Isotope's:

<!-- snippet: controls-usings -->
```csharp
using Isotope.UI;
using Isotope.UI.Core;
using Vendor.Controls.Isotope;
using static Isotope.UI.Factories;
using static Vendor.Controls.Isotope.VendorFactories;
// With the factory imported, RatingBar names the method, not the control type (CS0119): alias a type you need.
using RatingBarControl = Vendor.Controls.RatingBar;
```

and uses your controls with Isotope's:

<!-- snippet: controls-usage -->
```csharp
// A component that uses the library's controls beside Isotope's own.
public static Element Review(RenderContext ctx)
{
    var (stars, setStars) = ctx.UseState(3);
    var (open, setOpen) = ctx.UseState(true);
    if (!open) return Button("Show the card", () => setOpen(true));
    return CalloutCard("Rate this guide",
            VStack(8,
                RatingBar(stars, onValueChanged: setStars).Fill(Theme.Accent),
                TextBlock($"{stars} of 5")),
            onDismissed: () => setOpen(false))
        .Accent(Theme.Accent)
        .Margin(8);
}
```

The card and the rating are the same control instances from one render to the next. A click on the rating calls
`setStars`, and the next render writes nothing back that the control already has. Isotope's own modifiers
(`Margin`, `Width`, `ToolTip`, `Padding`, `FontSize` and the brushes) work on your elements with no code of yours.

## The project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0;net11.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Isotope.UI.Avalonia" Version="<version>" />
    <ProjectReference Include="..\Vendor.Controls\Vendor.Controls.csproj" />
  </ItemGroup>
</Project>
```

- **The frameworks are Isotope's:** `net9.0`, `net10.0` and `net11.0`. Target the ones your users need; each needs the
  matching runtime to run its tests.
- **No `LangVersion`:** each framework's default (C# 13 on `net9.0`, 14 on `net10.0`, 15 on `net11.0`) builds the
  records, factories and modifiers on this page. An app that calls `ReactorApp.Run` on `net9.0` needs C# 14, but a control
  library doesn't.
- **The generator comes with the package.** `Isotope.UI.Avalonia` carries it as an analyzer (a `netstandard2.0`
  assembly), and it runs in your build on every framework, so there is nothing else to reference. Everything it writes
  uses Isotope's public API: you need no `InternalsVisibleTo`.
- **Check that it ran.** An analyzer that fails to load is only a warning (CS8032), so the example builds with
  `TreatWarningsAsErrors`. The generator writes each record's `Descriptor`, so code that names
  `RatingBarElement.Descriptor` doesn't build when it didn't run (the example's first test does).

## An element record

One record per control, a `partial record` deriving from `Element`, marked with the control it describes:

<!-- snippet: controls-rating-record -->
```csharp
/// <summary>
/// A <see cref="RatingBar"/>. <see cref="Value"/> is controlled: Unset (the default) leaves the control's own value, and a
/// value is written whenever the control drifts from it; <see cref="OnValueChanged"/> reports the user's changes.
/// </summary>
[GenerateReactorDescriptor(typeof(RatingBar))]
[WrapEvent("Cleared", Arg = "OldValue")]
[WrapManual("IsReadOnly")]
public partial record RatingBarElement(
    Optional<int> Value = default,
    int Maximum = 5,
    Action<int>? OnValueChanged = null) : Element
{
    /// <summary>The fill of the chosen squares; null leaves the control's (or a theme token's, <c>.Fill(Theme.Accent)</c>).</summary>
    public IBrush? Fill { get; init; }

    public double ItemSize { get; init; } = 16;

    /// <summary>Called after <c>RatingBar.Clear()</c> with the value it cleared: a plain .NET event, so [WrapEvent].</summary>
    public Action<int>? OnCleared { get; init; }

    /// <summary>A rating the pointer can't change. The control has no such property, so Customize writes it.</summary>
    public bool IsReadOnly { get; init; }

    internal Action<RatingBar>[] Setters { get; init; } = [];

    // A [WrapManual] member is written here; the descriptor adds these entries before the generated ones.
    private static partial ControlDescriptor<RatingBarElement, RatingBar> Customize(ControlDescriptor<RatingBarElement, RatingBar> d)
        => d.OneWay(get: static e => e.IsReadOnly, set: static (c, v) => c.IsHitTestVisible = !v);
}
```

- **`[GenerateReactorDescriptor(typeof(RatingBar))]`** has the generator write the record's descriptor (how the engine
  mounts and updates the control) and register it. The control can come from another assembly, as `RatingBar` does.
- **Members take the control's names** ([the naming rule](../../tools/naming/README.md)): a property keeps its name
  (`Maximum`, `Fill`), a callback is `On` plus the event (`OnCleared`), and a value's callback is `On` plus the property
  plus `Changed` (`OnValueChanged`). The generator maps members by these names, so nothing else needs saying. Put the
  members most calls set in the record's parameters, and the rest in `init` properties.
- **The record is named after its control:** `{Control}Element`.
- **How members are written:**
  - A value-type member is written on mount and whenever it changes. Give it the control's default (`Maximum = 5`,
    `ItemSize = 16`), since the mount writes it.
  - A reference member (`IBrush? Fill`) is written only while it is set. Null leaves the control's own value, or a theme
    token's. With `ClearValueOnUnset = true` on the attribute, a member set back to null clears the control's value
    instead (the card below).
  - The descriptor adds the `Customize` entries first (below), then the one-way members, then the controlled value.
    So `Maximum` is in place before `Value`, and the control's coercion of `Value` sees the new bound.
- **`Setters`,** an `Action<TControl>[]`, holds the escape hatch (`.Set(c => ...)`, below), run after every mount and
  update. It is optional: without it the descriptor reads none.

## Factories and modifiers

Put them in your library's namespace, never in `Isotope.UI`:

<!-- snippet: controls-factories -->
```csharp
/// <summary>
/// The factories, one per control, named after it (tools/naming/README.md), with the members' names in camel case:
/// <c>using static Vendor.Controls.Isotope.VendorFactories;</c> beside <c>using static Isotope.UI.Factories;</c>. The class
/// isn't called <c>Factories</c>, so a file that imports both namespaces can still name either class.
/// </summary>
public static class VendorFactories
{
    /// <summary>A rating of <paramref name="maximum"/> squares: <c>RatingBar(stars, onValueChanged: setStars)</c>.</summary>
    public static RatingBarElement RatingBar(Optional<int> value = default, int maximum = 5, Action<int>? onValueChanged = null,
        Action<int>? onCleared = null) =>
        new(value, maximum, onValueChanged) { OnCleared = onCleared };

    /// <summary>A panel of children side by side: <c>SpreadPanel(RatingBar(1), RatingBar(2))</c>.</summary>
    public static SpreadPanelElement SpreadPanel(params Element[] children) => new(children);

    /// <summary>A card with a header and one child: <c>CalloutCard("Tip", TextBlock("..."))</c>.</summary>
    public static CalloutCardElement CalloutCard(string? header, Element content, Action? onDismissed = null) =>
        new(header, content) { OnDismissed = onDismissed };
}
```

- **A factory is named after its control,** and its parameters are the members' names in camel case, callbacks
  included (`onValueChanged`).
- **Name the factory class after your library** (`VendorFactories`), not `Factories`, so a file that imports both
  namespaces can still name each class. See [Name clashes](#name-clashes) for the factories themselves.

Modifiers are extension methods that return a copy of the element, named after the property each sets:

<!-- snippet: controls-modifiers -->
```csharp
/// <summary>The modifiers of the vendor's elements, named after the property each sets.</summary>
public static partial class VendorElementExtensions
{
    public static RatingBarElement Fill(this RatingBarElement el, IBrush? brush) => el with { Fill = brush };

    public static CalloutCardElement Accent(this CalloutCardElement el, IBrush? brush) => el with { Accent = brush };

    /// <summary>Runs <paramref name="configure"/> on the control after every mount and update (the escape hatch).</summary>
    public static RatingBarElement Set(this RatingBarElement el, Action<RatingBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    /// <summary>Runs <paramref name="configure"/> on the control after every mount and update (the escape hatch).</summary>
    public static CalloutCardElement Set(this CalloutCardElement el, Action<CalloutCard> configure) =>
        el with { Setters = [.. el.Setters, configure] };
}
```

Don't write the ones Isotope has: its layout, text and brush modifiers already take any element. `Set` is the escape
hatch, for a property the record doesn't cover, and it runs on every mount and update.

## Callbacks

- **A routed event** whose args are `RoutedEventArgs`, as `Button.Click`'s are, needs only an `Action? On{Event}` member:
  `CalloutCard.Dismissed` becomes `OnDismissed` with no attribute. The call goes to the latest render's closure, and
  unmounting detaches it.
- **A controlled value** is a property with a `{Property}Changed` event, paired by name: `Optional<int> Value` with
  `Action<int>? OnValueChanged` (`RatingBar.ValueChanged` is a plain `EventHandler<RatingChangedEventArgs>`; the
  pairing reads the value back from the property). It behaves as on Isotope's own controls:
  - A user's change calls back once. The engine's own write doesn't echo.
  - A value equal to the last render's isn't written again, so an edit the callback rejects (by not setting state) stays
    in the control until the element's value changes.
  - `Optional`'s Unset, the default, leaves the control its own value.
- **A plain .NET event** (`EventHandler`, `EventHandler<T>`) needs `[WrapEvent("Name")]` on the record. The callback then
  takes the event's args (`Action<TArgs>`). With `Arg = "Property"` it takes one property of the args instead, and with
  `Args` several. `RatingBar.Cleared` passes `RatingChangedEventArgs`, and `[WrapEvent("Cleared", Arg = "OldValue")]`
  makes `OnCleared` an `Action<int>`:

<!-- snippet: controls-wrap-event -->
```csharp
// OnCleared comes from a plain .NET event, through [WrapEvent("Cleared", Arg = "OldValue")].
public static Element ClearableRating(RenderContext ctx)
{
    var (stars, setStars) = ctx.UseState(4);
    var (cleared, setCleared) = ctx.UseState<int?>(null);
    return VStack(
        RatingBar(stars, onValueChanged: setStars, onCleared: old => setCleared(old)),
        TextBlock(cleared is { } was ? $"Cleared a {was}" : $"{stars} stars"));
}
```

- **`[WrapManual("Member")]`** takes a member over: the generator leaves it alone, and you write it in `Customize`, which
  is then required (`IsReadOnly` in the record above: the control has no such property, so `Customize` sets
  `IsHitTestVisible`). `Customize` adds entries to the `ControlDescriptor` it is given and returns it. On a callback,
  `[WrapManual("OnX")]` says that you wire it yourself in `Customize`, as Isotope's own records do for events that need
  more than a call. The worked example doesn't do that, so use `[WrapEvent]` when it fits.

A callback the descriptor would never call, or one that can't take what its event passes, is a build error at the
member ([below](#the-generators-diagnostics)).

## Content slots and panels

**A content slot** is an `Element` member that the generator maps to the control's content property:

<!-- snippet: controls-card-record -->
```csharp
/// <summary>
/// A <see cref="CalloutCard"/>: a header, one child element in its content slot (reconciled in place across renders,
/// so a component there keeps its state) and an accent stripe. A member set back to null clears the control's value
/// (<c>ClearValueOnUnset</c>, as Isotope's Border has it); without it the generator leaves the last value written.
/// </summary>
[GenerateReactorDescriptor(typeof(CalloutCard), ClearValueOnUnset = true)]
public partial record CalloutCardElement(string? Header, Element Content) : Element
{
    /// <summary>The stripe's brush; null leaves the control's (or a theme token's, <c>.Accent(Theme.Accent)</c>).</summary>
    public IBrush? Accent { get; init; }

    /// <summary>Called when the card's close button is pressed (a routed event: wired with no attribute).</summary>
    public Action? OnDismissed { get; init; }

    internal Action<CalloutCard>[] Setters { get; init; } = [];
}
```

The child is reconciled in place. A component in the card keeps its state while the card's header and accent change.
Another kind of element in the slot replaces the child and keeps the card, and cards nest.

**A panel** is a record over a `Panel` with an `Element[] Children` member:

<!-- snippet: controls-panel-record -->
```csharp
/// <summary>A <see cref="SpreadPanel"/>: child elements side by side, matched by key across renders.</summary>
[GenerateReactorDescriptor(typeof(SpreadPanel))]
public partial record SpreadPanelElement(Element[] Children) : Element
{
    public double Spacing { get; init; }

    internal Action<SpreadPanel>[] Setters { get; init; } = [];
}
```

Its children behave as in Isotope's own panels. Keyed children move without being detached and keep their state,
keyed adds and removes keep the others, an `Empty()` child is left out, and a `Group(...)` mounts its children among
their siblings:

<!-- snippet: controls-panel-usage -->
```csharp
// The library's panel takes keyed children and Isotope's groups, as a StackPanel does.
public static Element Scores(RenderContext ctx)
{
    var (names, setNames) = ctx.UseState<string[]>(["first", "second", "third"]);
    return VStack(
        SpreadPanel(
            TextBlock("Scores"),
            Group([.. names.Select(name => (Element)CalloutCard(name, RatingBar(3)).WithKey(name))]))
            with { Spacing = 8 },
        Button("Reverse", () => setNames([.. Enumerable.Reverse(names)])));
}
```

## When an element is skipped

An element equal to the last render's isn't applied to its control again: that is how a rejected edit stays, and
why an unchanged rating costs nothing on a render of its parent. The generator decides equality for your record. It
writes two members into another part of the record:

- **`HasCallbacks`:** whether any delegate member is set.
- **`ShallowEqualsCore`,** which compares every member the record declares:
  - callbacks (`On{X}`) by whether they are set, since the control's handlers read the latest element anyway;
  - other delegates, elements, arrays, collections (`Children`, `Setters`) and records by reference;
  - everything else by value (`Value`, `Maximum`, `Fill`, `IsReadOnly`).

It writes neither for a generic or nested record, or one whose base record declares either member. It writes no
`ShallowEqualsCore` (only `HasCallbacks`) when a member can't be compared: a struct holding an element
(`Optional<Element>`), a member hidden by a same-named one, or a base record from another assembly. Either way, the
element is applied on every render. A new array or delegate each render (a `.Set(...)` lambda made in the render) is
never equal to the last, so that element is applied each time too, as Isotope's own are.

You can write either member yourself, and the generator then leaves that one to you:

<!-- snippet: controls-own-skip -->
```csharp
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record OwnRatingElement(Optional<int> Value = default, Action<int>? OnValueChanged = null) : Element
{
    protected override bool HasCallbacks => OnValueChanged is not null;

    protected override bool ShallowEqualsCore(Element other)
        => other is OwnRatingElement o && Value.Equals(o.Value) && ReferenceEquals(Setters, o.Setters)
            && (OnValueChanged is null) == (o.OnValueChanged is null);

    internal Action<RatingBar>[] Setters { get; init; } = [];
}
```

**A hand-written `ShallowEqualsCore` must compare every member.** One it ignores is lost when only that member
changes: the element is skipped, and the next update compares against this render's element, which already has the new
value, so the control never gets it.

## Theme tokens on your own brushes

Isotope's token modifiers (`.Background(Theme.CardBackground)`, `.Foreground(...)`, `.BorderBrush(...)`) work on your
controls, and a token for a property the control lacks is skipped (see [Theme tokens](theme-tokens.md)). For a brush
property of your own, write `Element.ThemeBindings`, which maps a property name to a token:

<!-- snippet: controls-token-modifiers -->
```csharp
public static partial class VendorElementExtensions
{
    /// <summary>The fill from a theme token; it follows the control's theme with no re-render, as Isotope's own do.</summary>
    public static RatingBarElement Fill(this RatingBarElement el, ThemeRef token) => WithToken(el, "Fill", token);

    /// <summary>The accent from a theme token; it follows the control's theme with no re-render.</summary>
    public static CalloutCardElement Accent(this CalloutCardElement el, ThemeRef token) => WithToken(el, "Accent", token);

    // Element.ThemeBindings maps a property name to a token, which the engine binds on the control. Isotope's own token
    // modifiers write it too; the dictionary is public, so a library does the same for its own brush properties.
    private static T WithToken<T>(T el, string property, ThemeRef token) where T : Element =>
        el with
        {
            ThemeBindings = el.ThemeBindings is null
                ? new Dictionary<string, ThemeRef> { [property] = token }
                : new Dictionary<string, ThemeRef>(el.ThemeBindings) { [property] = token },
        };
}
```

The engine binds the token on the control, so it follows a switch between light and dark with no re-render. An
element's own brush (`.Accent(Brushes.Red)`) wins while it is set, and the token returns when it goes.

Don't resolve a token in a setter. A setter runs only on a render, so the brush would keep the old theme's colour after
a switch. To read a token's brush once, `ThemeRef.Resolve(Theme.Accent.ResourceKey, isDark)` takes the variant (for a
control, `isDark: control.ActualThemeVariant == ThemeVariant.Dark`). It doesn't see the resources of the control's own
subtree.

## Error boundaries

[Error boundaries](error-boundaries.md) work around and inside your controls:

<!-- snippet: controls-error-boundary -->
```csharp
// A boundary catches what a library's control throws: here, Avalonia's validation of RatingBar.Maximum.
public static Element SafeRating(RenderContext ctx) =>
    ErrorBoundary(RatingBar(1, maximum: 0), error => TextBlock($"The rating failed: {error.Message}"));
```

A boundary catches what your control throws when it mounts or updates (here Avalonia's validation of `Maximum`), a
throw from an escape hatch (`.Set(...)`), and a throw in the content slot's components. A boundary inside a card's
content slot keeps the card and recovers on the next render.

## The generator's diagnostics

Each is an error, reported on your record rather than inside a generated file.

| ID | At | When | What it asks for |
|---|---|---|---|
| `ISOTOPEGEN001` | the record | A public property that isn't a delegate, with no settable property of that name on the control, so the descriptor would never write it. | Give it the control's member name, write it in `Customize` with `[WrapManual]`, or remove it. |
| `ISOTOPEGEN002` | the member | An `On{X}` callback the descriptor never wires: a plain .NET event with no `[WrapEvent]`, a name the control has no event or controlled property for, or a callback declared on a base record. | Name it after a routed event, add `[WrapEvent("X")]`, or mark it `[WrapManual("OnX")]` and wire it in `Customize`. Declare a base record's callback on the record itself. |
| `ISOTOPEGEN003` | the member | A callback whose delegate can't take what its event passes. The event is left unwired. | The delegate the message names: `Action` for a routed event, `Action<TArgs>` for a bare `[WrapEvent]`, or the types its `Arg` or `Args` name. |
| `ISOTOPEGEN004` | the attribute | `[WrapDecorator]` or `[WrapPolymorphic]`, which Isotope doesn't support yet. Nothing is generated. | Map the control's properties with `[GenerateReactorDescriptor]` (the rest in `Setters` or `Customize`), or wrap the control in a component. |

A record without `Setters` needs no diagnostic: it builds, and has no escape hatch. One case still errors inside the
generated file: a controlled value's `On{Property}Changed` with the wrong delegate type. Spike J's probes
(`Probes/probes.json`) pin each diagnostic on every framework.

## Name clashes

- **A factory with the name and parameters of one of Isotope's** is ambiguous in a file that imports both with
  `using static` (CS0121). Isotope already has `Card(Element)`, `Border`, `Button`, `TextBlock` and more. Check your
  factory names against `Isotope.UI.Factories` (the MCP server's `search_api` finds them).
- **A factory named after its control hides the control type** in member access once it is imported:
  `RatingBar.ValueProperty` is then the method group, CS0119. Alias the type where you need it, as the usings above do
  (`using RatingBarControl = Vendor.Controls.RatingBar;`). Isotope's own `Button` hides Avalonia's in the same way.
- **The factory class** is named after the library, as above.

## Not covered yet

The worked example doesn't cover an items control (`Items`), a secondary element slot (`[WrapElementSlot]`), more than
one controlled property on a control, a callback wired by hand in `Customize`, a handler registered with
`ControlRegistry.Register`, or your package consumed as a package (the example's tests reference its projects). The
Reactor migration tool reads only Isotope's own rename table, so a Reactor-based library's own renames aren't applied to
its users' code.
