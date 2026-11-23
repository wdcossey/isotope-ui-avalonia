# Isotope.UI.Avalonia

An Avalonia 12 version of microsoft/microsoft-ui-reactor. Repo: the private GitHub repo (private). Package prefix: `Isotope.` (`Avalonia.` is reserved on NuGet). The local folder is still `<repo root>`.

Read `docs/PROCESS.md` first: it is how work runs here. Then `HANDOVER.md` and `FINDINGS.md`.

- Private GitHub remote `origin` since 2026-11-23. Until the PR-based process is adopted: one branch per spike or unit of work, self-reviewed, merged into `main` locally, then `main` pushed. No issues, PRs, CI or branch protection yet.
- `upstream/` is a read-only reference clone of microsoft/microsoft-ui-reactor, git-ignored, pinned in `UPSTREAM.lock`. Only read-only git commands run in it (log, show, diff, grep). Never edit, check out or commit there.
- `global.json` pins the .NET 11 SDK (11.0.100, installed 2026-11-22) for the C# 15 compiler only; projects target `net10.0` because .NET 10 is the LTS release (user's decision, 2026-11-23). Move a target to `net11.0` only if something needs the .NET 11 runtime, and ask first. Projects that compile upstream files use `LangVersion 15` (upstream's `closed` hierarchies) and link upstream's `Core/Polyfills/ClosedTypeAttributes.cs`.
- Use absolute paths from `<repo root>`: the harness tends to move the working directory into `upstream`.
