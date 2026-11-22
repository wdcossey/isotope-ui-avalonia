# Reactor.Avalonia

Read `docs/PROCESS.md` first: it is how work runs here. Then `HANDOVER.md` and `FINDINGS.md`.

- Local git only: no GitHub remote, no issues or PRs. One branch per spike or unit of work, self-reviewed, merged into `main` locally.
- `upstream/` is a read-only reference clone of microsoft/microsoft-ui-reactor, git-ignored, pinned in `UPSTREAM.lock`. Only read-only git commands run in it (log, show, diff, grep). Never edit, check out or commit there.
- `global.json` pins the .NET 10 SDK for our projects (user's choice, 2026-11-22). Upstream files that need C# 15 (`closed` types) don't compile under it and count as "needs edits".
- Use absolute paths from `<repo root>`: the harness tends to move the working directory into `upstream`.
