# Isotope.UI.Avalonia: notes for Claude

An Avalonia 12 version of microsoft/microsoft-ui-reactor, with an Avalonia-native public API. **The premise, and the
main draw:** UI as a function of state, written as C# function components with React-style hooks (`UseState`,
`UseReducer`, `UseEffect`…), with no XAML, no bindings and no view models. Judge work by how well it serves that. Read in this order: `README.md`, `docs/PROCESS.md` (how work runs), `FINDINGS.md` (results
and decisions), then the open GitHub Issues (`gh issue list`): they are the task list. This file holds the rules that
must hold on any machine; machine-specific notes go in a git-ignored `CLAUDE.local.md`.

## Hard rules

- **No personal data in the repo**, in files, generated outputs, screenshots, issues, PRs or commit messages:
  - no names, emails or usernames;
  - no local paths (drive letters, user folders, temp or scratch folders);
  - no upstream contributors' names or usernames, including inside branch names in merge subjects.
  Scripts write hashes or counts instead. Session briefs (`HANDOVER.md`) and `CLAUDE.local.md` are git-ignored.
  Scan files and commit messages before pushing:
  `git grep -n -I -i -E '<your username>|\b[A-Z]:[\\/]|Users[\\/]' HEAD -- . ':!src/Upstream'` and
  `git log origin/main..HEAD --format=%B | grep -i -E '<your username>|\b[A-Z]:[\\/]|Users[\\/]'` (both must print nothing).
  Vendored files (`src/Upstream/`) are left out of that scan: `tools/sync` checks every generated file itself on
  each sync and `--check` (emails, user-folder and drive-letter paths, and the sync report), genuine upstream
  examples are allowlisted by line hash in `tools/sync/personal-data.json`, and personal data in vendored files is
  removed by a transform rule in `tools/sync/transforms.json`, never by hand. The check can't recognise people's
  names or bare usernames, so after a sync also run `git grep -n -I -i -E '<your username>' -- src/Upstream`
  (must print nothing) and read the sync report's list of new and changed vendored files for names.
  Commits use the developer's GitHub noreply address (history rewritten to it on 2026-11-24, before the repo went
  public): set it per machine with `git config user.email` (see `CLAUDE.local.md`), and never add other names or emails.
- **The repo is public: workflows and Actions settings follow these rules** (2026-11-24).
  - **GitHub-hosted runners only.** Never add a self-hosted label or register a runner on this repo, and never set
    `vars.LINUX_RUNNER`: a fork PR's workflow could reach the machine. If it is ever set, treat it as a security issue
    and tell the developer. The sync job has no runner variable at all (#36).
  - **Triggers:** `push`, `pull_request`, `workflow_dispatch`, `schedule` only. Never `pull_request_target`,
    `issue_comment` or `workflow_run`: they run with this repo's token but a stranger can start them.
  - **Least privilege:** keep a top-level `permissions:` block (`contents: read` or `{}`); grant more only on the job
    that needs it, with a comment saying why. Upstream code never runs in a job that holds a write token (#36).
  - **Actions:** GitHub-owned `actions/*` only (the repo allows nothing else). A third-party action needs the
    developer's OK and is pinned by full commit SHA.
  - **No secrets, no variables, no collaborators** without the developer: collaborators skip the fork-PR approval gate.
  - **Approving a fork PR's workflow run is the developer's call,** never an agent's. Don't approve runs or ask to.
  - **Repo settings** (`gh api -X PUT …`) are ask-first; read-only checks are free.
  - **Never mention the developer's own infrastructure** (machines, networks, hostnames, IPs, self-hosted services) in
    files, commits, issues or PRs.
- **Upstream is read-only.** Never edit, commit or build inside `upstream/`. Read-only git, `git fetch`, and checking
  out the commit in `UPSTREAM.lock` (setup, or `tools/sync`) are the only writes allowed. Send
  nothing upstream: no issues, PRs or comments. Plans must not depend on upstream changing.
- **Vendored upstream files are never edited by hand.** Differences come only from transform rules applied by
  `tools/sync`, and CI regenerates them and fails on drift (on Ubuntu and Windows). If a file can't be expressed that way, it moves to tier 3.
- **Ask first** for anything in `docs/PROCESS.md` §2. Examples: real windows or UI automation (screenshots are rendered
  headless), installs, repo settings or branch protection, force-pushes or history rewrites, new remotes, and
  publishing packages.

## Decisions already made (don't reopen)

- **Public API:** Avalonia-native types and vocabulary. On the seam the engine calls, we keep upstream's *member
  names*, with Avalonia *value types*.
- **Upstream model:** three tiers, vendor and regenerate. No fork and no submodule. `UPSTREAM.lock` pins the commit;
  `upstream/` is only a cache.
- **Toolchain:** .NET 11 SDK (11.0.100, `global.json`) for the C# 15 compiler only. Every project targets `net10.0`
  (LTS), except Roslyn analyzer/generator projects (`src/Isotope.UI.Avalonia.Generators`), which target `netstandard2.0`
  as Roslyn requires. Projects that compile upstream files set `LangVersion 15` and link upstream's
  `Core/Polyfills/ClosedTypeAttributes.cs` (the generator project sets `LangVersion 15` but needs no polyfill: it has no
  `closed` hierarchies). Retargeting to `net11.0` needs the developer's go-ahead.
- **Name:** Isotope.UI.Avalonia, with package prefix `Isotope.`. **Namespaces are `Isotope.UI.*`** (2026-11-23):
  upstream's `Microsoft.UI.Reactor.*` is renamed by one transform rule (and its event-provider names
  `Microsoft-UI-Reactor(-Perf)` by another), so package and namespaces match and nothing reads as Microsoft's. Upstream's *member* names stay.
- **`RenderContext` is made `partial` by a transform rule** (2026-11-23), so our Avalonia versions of the tier 3 hooks
  (theme, windows, displays…) are written as our own partial part, with access to its state.
- **`Component` is made `partial` the same way** (#21), so class components get the platform hooks from our own part.
- **Control layer:** descriptors are generated by a vendored, transform-patched copy of upstream's wrappers generator
  (`gen-*` rules; our helpers in `WrapperGenerator.Avalonia.cs`); the descriptor runtime is tier 2 (13 member drops);
  element records are hand-written for now. Records, factory parameters and modifiers use **Avalonia's member names**
  (#27 answer 1, 2026-11-24; #31): a property or event of the control, `On{Event}` or `On{Property}Changed`.
  `tools/naming/renames.json` maps Reactor's names, and the migration tool (#33) reads it. Type and factory names stay
  Reactor's until #27's follow-up is answered.
- **Controls behave as upstream's:** a controlled value equal to the last render's isn't re-applied; `ISOTOPEGEN001`
  (a record member with no Avalonia counterpart) is a build error; no control pooling until #14 measures it.
- **Element factories and modifiers** are our own tier 3 version of upstream's `Dsl.cs`/`ElementExtensions.cs` (#17),
  with upstream's factory names (parameters take the members' Avalonia names); add a factory when its control arrives. Doc C# blocks must be `#region`s of the test project
  (`DocSnippetTests`).
- **Member drops stay text-based** (the lexer in `tools/sync/lib/csharp.mjs`; #4, 2026-11-24). A `roslyn` rule kind
  (drop-member, retarget-type, drop-attribute; `tools/sync/roslyn`, run before the text rules) complements them for new
  rules, and `syntaxCheck` makes every C# file a rule changes still parse. `tools/sync` therefore needs dotnet.

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
- **Branch** `<type>/<issue>-<slug>` (types: feat, fix, chore, docs, research, spike) and use a worktree for parallel work. Commit small, and stage files by name.
- **Self-review** with the `code-review` skill. Always name the branch and the level:
  - behaviour changes get two passes: `high main...<branch> --max-findings all`, then `medium` (same form). A small
    one-area fix may use `medium` for pass 1;
  - docs and chores get one pass: `medium main...<branch> --max-findings all`.
  - Check each finding before acting; findings can be wrong.
- **Prove each new test fails** with its behaviour broken (on committed work), then run clean from a fresh clone.
- **Merge** through a PR (ruleset `main`: no direct pushes; six CI checks required, branch up to date). Merge with
  `gh pr merge --merge`; the message records the review levels, tokens, findings fixed or waived, and `Closes #n`.
- **Report numbers that were measured, never estimated.** Say how each was measured.
- **Lessons:** `docs/LESSONS.md`. Add one when a new trap is hit.
- **Parallel agents** get `docs/agent-brief.md` and an explicit model.
