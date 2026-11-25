# Isotope.UI.Avalonia

React-style function components and hooks for **Avalonia 12**, built on the engine of
[microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) (MIT), with an Avalonia-native API.

> **Preview.** The API may still change. An independent project, not affiliated with or endorsed by Microsoft or the
> Avalonia project.

**Your UI is a function of state.** No XAML, no data binding, no view models. You write C# functions that return a
description of the UI; hooks hold the state, and Isotope keeps the controls in step.

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

With `using Isotope.UI.Core;` and `using static Isotope.UI.Factories;`. Host a component in any Avalonia app with
`new ReactorHostControl(Counter)` (namespace `Isotope.UI.Hosting`), or open it in a window with one line from the
**Isotope.UI.Avalonia.Desktop** package: `ReactorApp.Run("Counter", Counter, width: 400, height: 300)`.

## What is in the package

- **Hooks:** `UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`, `UseContext`, async data
  (`UseResource`, `UseMutation`, `UseInfiniteResource`), and platform hooks (theme, windows, displays).
- **Controls** with Avalonia's member names, keyed lists, function and class components, and error boundaries.
- **The wrappers generator**, as an analyzer: mark your own `partial record` with `[GenerateReactorDescriptor(typeof(SomeControl))]`
  (namespace `Isotope.UI.Wrappers`) and it is wired to that Avalonia control like the built-in ones.

Targets `net9.0`, `net10.0` and `net11.0`. On `net9.0`, C# 14 binds `array.Reverse()` to the span overload, which
returns nothing: write `Enumerable.Reverse(array)`. The project's source repository (linked on this page) has the guide, the samples and the licences.

## Licence

MIT. The package includes code from microsoft/microsoft-ui-reactor under its MIT licence, and Yoga under Meta's MIT
licence: see `LICENSE` and `licenses/microsoft-ui-reactor/` in the package.
