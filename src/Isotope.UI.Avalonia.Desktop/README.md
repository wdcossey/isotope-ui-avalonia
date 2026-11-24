# Isotope.UI.Avalonia.Desktop

The one-line desktop startup for [Isotope.UI.Avalonia](https://www.nuget.org/packages/Isotope.UI.Avalonia): React-style
function components and hooks for Avalonia 12.

`ReactorApp.Run("Counter", Counter, width: 400, height: 300)` opens a function component in a window, and
`ReactorApp.Run<App>("Title", width: 800, height: 600)` a class component, on Windows, macOS and Linux. Both run over
`ReactorApp.CreateDefaultAppBuilder()`: an Avalonia app with the Fluent theme on the platform Avalonia detects. Pass that
builder, configured further, to `ReactorApp.Run(builder, ...)` to keep the defaults and add to them.

A package of its own, as `Avalonia.Desktop` is, so the library stays free of the desktop backends for other heads.

> **Preview.** The API may still change. An independent project, not affiliated with or endorsed by Microsoft or the
> Avalonia project.

## Licence

MIT. See `LICENSE` and `licenses/microsoft-ui-reactor/` in the package.
