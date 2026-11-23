# Isotope.UI.Avalonia: notes for Claude

An Avalonia 12 version of microsoft/microsoft-ui-reactor (React-style hooks and function components for C#), with an
Avalonia-native public API. Read in this order: `README.md`, `docs/PROCESS.md` (how work runs), `FINDINGS.md` (results
and decisions), then the open GitHub Issues (`gh issue list`): they are the task list. This file holds the rules that
must hold on any machine; machine-specific notes go in a git-ignored `CLAUDE.local.md`.

## Hard rules

- **No personal data in the repo**, in files, generated outputs, screenshots, issues, PRs or commit messages:
  - no names, emails or usernames;
  - no local paths (drive letters, user folders, temp or scratch folders);
  - no upstream contributors' names or usernames, including inside branch names in merge subjects.
  Scripts write hashes or counts instead. Session briefs (`HANDOVER.md`) and `CLAUDE.local.md` are git-ignored. Scan
  before pushing: `git grep -n -I -i -E '<your username>|\b[A-Z]:[\\/]|Users[\\/]' HEAD`.
- **Upstream is read-only.** Never edit, check out, commit or build inside `upstream/` (read-only git is fine). Send
  nothing upstream: no issues, PRs or comments. Plans must not depend on upstream changing.
- **Vendored upstream files are never edited by hand.** Differences come only from transform rules applied by
  `tools/sync`; CI regenerates them and fails on drift. If a file can't be expressed that way, it moves to tier 3.
- **Ask first** for anything in `docs/PROCESS.md` §2. Examples: real windows or UI automation (screenshots are rendered
  headless), installs, repo settings or branch protection, force-pushes or history rewrites, new remotes, and
  publishing packages.

## Decisions already made (don't reopen)

- **Public API:** Avalonia-native types and vocabulary. On the seam the engine calls, we keep upstream's *member
  names*, with Avalonia *value types*.
- **Upstream model:** three tiers, vendor and regenerate. No fork and no submodule. `UPSTREAM.lock` pins the commit;
  `upstream/` is only a cache.
- **Toolchain:** .NET 11 SDK (11.0.100, `global.json`) for the C# 15 compiler only. Every project targets `net10.0`
  (LTS). Projects that compile upstream files set `LangVersion 15` and link upstream's
  `Core/Polyfills/ClosedTypeAttributes.cs`. Retargeting to `net11.0` needs the developer's go-ahead.
- **Name:** Isotope.UI.Avalonia, with package prefix `Isotope.`.

## Environment traps

- **The working directory drifts into `upstream/`.** Use absolute paths, and give every `dotnet` call an explicit
  working directory (`.cwd()` / `cd <repo> &&`). Upstream's `global.json` switches `dotnet test` to
  Microsoft.Testing.Platform, which prints "Passed!" with no counts and rejects `--filter`/`--logger`.
- **A Windows checkout turns line endings into CRLF.** Scripted find/replace on checked-out files must allow for `\r\n`.
- **A git worktree or temp clone needs `upstream/`.** Link it with a directory junction or symlink, and **delete the
  link itself before deleting the folder.** A recursive delete can follow the link into the real upstream.
- **Avalonia 12 lays out on the render tick.** Headless tests pump with `Dispatcher.UIThread.RunJobs()`.

## How work runs (summary; details in docs/PROCESS.md)

- **Pick** the highest-priority `self-drive` issue whose blockers are closed.
- **Branch** `<type>/<issue>-<slug>` and use a worktree for parallel work. Commit small, and stage files by name.
- **Self-review** with the `code-review` skill. Always name the branch and the level:
  - behaviour changes get two passes, `high main...<branch> --max-findings all` then `medium`;
  - docs and chores get one `medium` pass.
  - Check each finding before acting; findings can be wrong.
- **Prove each new test fails** with its behaviour broken (on committed work), then run clean from a fresh clone.
- **Merge** into `main` (`--no-ff`; the message records the review levels, tokens, findings fixed or waived, and
  `Closes #n`). Check there are no conflict markers, then push `main`. This becomes PR-based once issue #7 lands.
- **Report numbers that were measured, never estimated.** Say how each was measured.
- **Lessons:** `docs/LESSONS.md`. Add one when a new trap is hit.
- **Parallel agents** get `docs/agent-brief.md` and an explicit model.
