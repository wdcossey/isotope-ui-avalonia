# Releasing

How the packages are built, checked and published (#52). **Publishing is the developer's call** (CLAUDE.md, ask first):
nothing is published except by pushing a `v*` tag, and agents never push tags.

## The packages

| Package | What | Holds |
|---|---|---|
| `Isotope.UI.Avalonia` | The library | `lib/net10.0` (DLL and XML docs), and the wrappers generator in `analyzers/dotnet/cs`, so a consumer's own `[GenerateReactorDescriptor]` records get descriptors as the library's do |
| `Isotope.UI.Avalonia.Desktop` | The one-line desktop startup | `lib/net10.0`; depends on the library at the same version, declaring every asset (`include="All"`, the generator included) |
| `Isotope.UI.Migration` | The Reactor → Isotope migration analyzer and code fixes | `analyzers/dotnet/cs` only; a development dependency with no dependencies |
| `Isotope.UI.Mcp` | The MCP server for coding agents (#60), a `dotnet tool` with the command `isotope-mcp` | `tools/net10.0/any`: the tool and everything it runs, the library's and desktop startup's DLLs and XML docs (which it reads) and the migration among them; no dependencies, and no native libraries (it never runs Avalonia) |

Each also carries its README (next to its project file), this repository's `LICENSE`, and upstream's licence and
third-party notices under `licenses/microsoft-ui-reactor/`, since the packages ship upstream's code (Yoga included).
The library and desktop packages have `.snupkg` symbol packages; the migration analyzer's PDB is embedded in its DLL,
and the tool's own PDB in its DLL, with the library's and desktop startup's PDBs next to their DLLs in the tool.

Metadata lives in `src/Directory.Build.props` and `src/Directory.Build.targets` (only the projects under `src/`):
`Authors` is "The Isotope.UI.Avalonia authors", the licence is the `MIT` expression, and the repository URL and
commit come from git when the package is built (SourceLink), so no file names the repository or a person.

## Versions

From git tags, by [MinVer](https://github.com/adamralph/minver) (a NuGet build package), with the tag prefix `v`:

- on a tag `v0.1.0-preview.1`: `0.1.0-preview.1`;
- on the commits after it: `0.1.0-preview.1.<height>` (the number of commits since the tag);
- before the first tag: `0.1.0-preview.0.<height>`.

Only tagged versions are published. CI packs the in-between versions to check them, and throws them away.

## Building the packages locally

```sh
node tools/packaging/pack.mjs                 # packs into bin/packages, checks, runs the smoke test
node tools/packaging/pack.mjs --no-smoke      # packs and checks only
node tools/packaging/pack.mjs --out <empty dir> --expect-version 0.1.0-preview.1
node --test tools/packaging/test/*.test.mjs   # the checks' own tests (no dotnet needed)
```

The script packs with `ContinuousIntegrationBuild`, which maps source paths to `/_/`, so no local path reaches a DLL
or PDB; a plain `dotnet pack` of a package fails on purpose and says so. Then it checks each package (layout,
metadata, the Isotope dependencies, SourceLink in every PDB at the package's commit, personal data in the nuspecs and
READMEs, local paths in every DLL, every PDB's source documents under the deterministic `/_/` root, and the tool package's type,
command, contents and lack of native libraries), and builds and runs `tests/Isotope.UI.PackageSmoke` against the packed
packages alone: a fresh packages folder, and a `nuget.config` that takes `Isotope.UI.*` from the packed folder only (never
nuget.org's copy of the same version):

- `App`: a headless counter (mount, click, re-render), a consumer-written `[GenerateReactorDescriptor]` element that
  only compiles if the bundled generator ran, and the desktop package's default app builder, set up headless. It
  references only the desktop package and builds with the consumer's default C# version and warnings as errors.
- `Migration`: `dotnet format analyzers` must find `ISOMIG001` and offer its fix, from the analyzer package.
- The MCP tool: installed from its package into a temporary `--tool-path` (never globally), started over stdio, it must
  list its six tools, answer `get_api UseState` with the hook's doc comment, and list the guides as resources.

The default output folder is emptied of earlier builds of these packages first. Another `--out` must be empty or
new, or given `--clean` to delete earlier builds of these packages in it; a folder holding anything else is refused,
and nothing else is ever deleted. The package READMEs' C# blocks are checked by the library tests
(`DocSnippetTests`), as the README's and the guide's are.

To try the local packages in an app, add the folder as a source:
`dotnet add package Isotope.UI.Avalonia --version <version> --source bin/packages`.

CI's `packages` job (`.github/workflows/ci.yml`, Ubuntu and Windows) runs the same script on every push.

## One-time setup (the developer, before the first release)

1. **nuget.org account.** Sign in to nuget.org with the account that will own the packages.
2. **Reserve the `Isotope.UI.` prefix** for that account, so no one else can publish `Isotope.UI.*` packages:
   [ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation) explains the request and
   its criteria. The first release doesn't need it, but a reservation only covers packages pushed after it is granted.
3. **Trusted publishing policy** ([NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)):
   on nuget.org, *Trusted Publishing*, add a GitHub Actions policy with this repository's owner and name, the workflow
   file `release.yml`, and the environment `nuget`. The workflow then gets a short-lived key per run; no API key is
   created or stored.
4. **GitHub environment `nuget`** (repository *Settings → Environments*):
   - add the secret `NUGET_USER`, the nuget.org *profile name* (not the email) of the account that created the policy.
     An environment secret is only given to the publish job;
   - add yourself as a **required reviewer**, so every publish waits for your approval. This is the gate that holds:
     the "tag on main" check lives in the tagged commit's own `release.yml`, so a tag on another commit could bring a
     copy without it, and nuget.org's policy only names the file and the environment;
   - limit its deployment branches and tags to the tag pattern `v*`.
5. **A tag ruleset** for `v*` (*Settings → Rules*), so only you can create, update or delete release tags.

The release workflow references the secret and the environment; this repository doesn't create them (CLAUDE.md: no
secrets or variables without the developer).

## Cutting a release

1. Pick the version, e.g. `0.1.0-preview.1`. Every preview so far is `0.1.0-preview.<n>`. One `v*` tag per commit:
   MinVer takes a commit's highest tag, so the workflow refuses a commit with two.
2. Check `main` is green, including the `packages` jobs, and on the commit to release run
   `node tools/packaging/pack.mjs` locally.
3. Tag that commit of `main` and push the tag:

   ```sh
   git fetch origin
   git tag -a v0.1.0-preview.1 origin/main -m "0.1.0-preview.1"
   git push origin v0.1.0-preview.1
   ```

4. The `release` workflow runs:
   - `pack and check` fails unless the tag is `v<major>.<minor>.<patch>[-<pre-release>]`, the only `v*` tag of a
     commit of `main`, and unless MinVer's version equals the tag's. It packs, checks, runs the smoke test, and uploads the packages
     (kept 30 days as the run's `packages` artifact).
   - `publish to nuget.org` (after your approval, if the environment requires it) exchanges the run's OIDC token for a
     key and pushes the three packages, each with its symbols. A re-run skips what is already there.
5. nuget.org validates and indexes the packages, which takes a few minutes. Check each package page: version, README,
   licence, the source repository link, and the symbols.

**If it fails before publishing,** delete the tag (`git push origin :refs/tags/v0.1.0-preview.1` and
`git tag -d v0.1.0-preview.1`), fix `main`, and tag again. **Once published,** a version can't be replaced: unlist it
on nuget.org if it must not be used, and release the next one.

## Not done yet

- No package icon (nuget.org shows its default).
- `release.yml` still counts three packages, so a release fails at its count check now that `pack.mjs` packs four
  (the MCP tool, #60): whether the tool is published with the others is the developer's call, and the workflow changes
  with it.
- No GitHub Release is created for a tag; the packages are the release.
- The first real run of `release.yml` is the first release: the token exchange and the symbol push have only been
  reviewed, not run.
