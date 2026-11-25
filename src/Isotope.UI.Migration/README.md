# Isotope.UI.Migration

Moves a [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor) (WinUI) app to
[Isotope.UI.Avalonia](https://www.nuget.org/packages/Isotope.UI.Avalonia): a Roslyn analyzer with code fixes. It renames
what has a counterpart (namespaces, WinUI types, renamed members and factory parameters) and reports what has none,
saying what to use instead.

1. Switch the project to `net10.0` or `net11.0` (`net9.0` works too, with `<LangVersion>14</LangVersion>`) and to the Isotope packages, and add this one for the migration:

   ```xml
   <PackageReference Include="Isotope.UI.Migration" Version="..." PrivateAssets="all" />
   ```

2. Run the fixes until they change nothing (usually twice):

   ```sh
   dotnet format analyzers MyApp.csproj --severity warn --diagnostics ISOMIG001 ISOMIG003 ISOMIG004 ISOMIG006 ISOMIG008 ISOMIG009 ISOMIG014
   ```

3. Build, fix what is reported by hand, and remove the package reference.

The analyzer binds to Isotope's types, so it runs once the project references Isotope. The source repository (linked on
this page) has the full guide, `docs/guide/migrating-from-reactor.md`.

> **Preview.** An independent project, not affiliated with or endorsed by Microsoft or the Avalonia project.

## Licence

MIT. See `LICENSE` and `licenses/microsoft-ui-reactor/` in the package.
