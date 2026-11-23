# Reactor.Avalonia

Read `docs/PROCESS.md` first: it is how work runs here. Then `HANDOVER.md` and `FINDINGS.md`.

- Local git only: no GitHub remote, no issues or PRs. One branch per spike or unit of work, self-reviewed, merged into `main` locally.
- `upstream/` is a read-only reference clone of microsoft/microsoft-ui-reactor, git-ignored, pinned in `UPSTREAM.lock`. Only read-only git commands run in it (log, show, diff, grep). Never edit, check out or commit there.
- `global.json` pins the .NET 11 SDK (11.0.100, installed 2026-11-22) for the C# 15 compiler only; projects target `net10.0` because .NET 10 is the LTS release (user's decision, 2026-11-23). Move a target to `net11.0` only if something needs the .NET 11 runtime, and ask first. Projects that compile upstream files use `LangVersion 15` (upstream's `closed` hierarchies) and link upstream's `Core/Polyfills/ClosedTypeAttributes.cs`.
- Use absolute paths from `<repo root>`: the harness tends to move the working directory into `upstream`.
