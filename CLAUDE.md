# Isotope.UI.Avalonia

An Avalonia 12 version of microsoft/microsoft-ui-reactor. Private GitHub repo (`origin`). Package prefix: `Isotope.` (`Avalonia.` is reserved on NuGet). Machine-specific notes live in `CLAUDE.local.md` (git-ignored).

Read `docs/PROCESS.md` first: it is how work runs here. Then `FINDINGS.md` (and a local `HANDOVER.md` if there is one; it is never committed).

- Private GitHub remote `origin` since 2026-11-23. Until the PR-based process is adopted: one branch per spike or unit of work, self-reviewed, merged into `main` locally, then `main` pushed. No issues, PRs, CI or branch protection yet.
- `upstream/` is a read-only reference clone of microsoft/microsoft-ui-reactor, git-ignored, pinned in `UPSTREAM.lock`. Only read-only git commands run in it (log, show, diff, grep). Never edit, check out or commit there.
- `global.json` pins the .NET 11 SDK (11.0.100, installed 2026-11-22) for the C# 15 compiler only; projects target `net10.0` because .NET 10 is the LTS release (user's decision, 2026-11-23). Move a target to `net11.0` only if something needs the .NET 11 runtime, and ask first. Projects that compile upstream files use `LangVersion 15` (upstream's `closed` hierarchies) and link upstream's `Core/Polyfills/ClosedTypeAttributes.cs`.
- Use absolute paths from the repo root and give every build/test command an explicit working directory: the harness tends to move the shell into `upstream/`, whose `global.json` changes the SDK and the test runner.
- **No personal data in the repo:** no names, emails, usernames, machine paths or upstream contributors' names in files, outputs or screenshots. Scripts write hashes or counts instead. Commit author identity: see `CLAUDE.local.md`.
