# Spike J: third-party controls from outside the repo (#75)

A small control library, built the way a third party would build one: against Isotope's **packed packages only**, from
a local folder, with no project reference into `src/` and no `InternalsVisibleTo`. The results and the recommendation
are in `docs/spikes/J-third-party-controls.md`.

| Project | What it is |
|---|---|
| `Vendor.Controls` | An Avalonia library that knows nothing of Isotope: `RatingBar` (a plain, custom-drawn control) and `SpreadPanel` (a panel). |
| `Vendor.Controls.Isotope` | What the vendor ships for Isotope: `CalloutCard` (a templated control, declared here), an element record per control with `[GenerateReactorDescriptor]`, factories (`VendorFactories`) and modifiers (`VendorElementExtensions`). |
| `Vendor.Controls.Tests` Gaps 1 and 6 are closed (#77): their tests now assert parity (`Vendor_a_rejected_edit_stays_...`, `Vendor_an_unchanged_element_is_skipped`, `A_token_resolves_for_a_controls_theme_...`). | Headless checks on net9.0, net10.0 and net11.0, next to Isotope's built-ins. Tests named `Gap_...` pin a difference from the built-ins as it is now, so they fail when it closes; gap 4 (a `Group` in the vendor's panel) is closed (#79), and its test asserts the group's children mount. |
| `Probes/` | Small projects that must fail (or pass) in a given way: `Probes/probes.json` says how. Gaps 2 and 3 are closed (#78): `UnwiredCallback`, `WrapEventMismatch` and `Decorator` expect `ISOTOPEGEN002`, `003` and `004` at the record, with no error in a generated file, and `NoSetters` builds clean. |

It is also the worked example of the guide [Writing controls for Isotope](../../docs/guide/writing-controls.md) (#80): each
C# block there is a `#region` here (`controls-*`), and the library's `DocSnippetTests` fails when the two differ, so change
them together. `Vendor.Controls.Tests/GuideExamples.cs` holds what a library's users write, and its tests.

The folder has its own `Directory.Build.props` and `.targets`, so nothing of the repository's build is imported, and its
`nuget.config` takes `Isotope.*` from the folder in `ISOTOPE_PACKAGES` only, as the package smoke test's does.

## Run it

```sh
node spikes/J-third-party/run.mjs                        # packs this branch into a temporary folder first
node spikes/J-third-party/run.mjs --packages <folder>    # or uses packages already packed by tools/packaging/pack.mjs --out
```

It needs what `tools/packaging/pack.mjs` needs (the .NET 11 SDK, the .NET 9 and 10 runtimes to run the tests on those
frameworks, and an `https://github.com/...` `origin` remote when it packs) and nuget.org. Restores go to a fresh temporary `NUGET_PACKAGES`, deleted afterwards. It prints a line per
framework and per probe, and exits 0 when every test passed on every framework and every probe did what
`probes.json` says.

To build or test one project by hand, set what `run.mjs` sets: `IsotopeVersion` (the packed version),
`ISOTOPE_PACKAGES` (the packed folder) and, to keep the restore apart, `NUGET_PACKAGES`.
