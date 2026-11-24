# How work runs

Adapted from `~/.claude/ways-of-working/PROCESS.md`. The repo is public on GitHub (`origin`, since 2026-11-24). Every
change reaches `main` through a pull request that passes CI (#7); the loop is otherwise the same.

```
 TASK → BRANCH/WORKTREE → BUILD → SELF-REVIEW ×2 → PROVE THE TESTS → CLEAN RUN → MERGE TO main → TIDY
```

## 1. The task list

**GitHub Issues are the task list** (since 2026-11-23). Labels:

| Label | Meaning |
|---|---|
| `P0`…`P3` | priority |
| `feature` / `bug` / `chore` / `research` | type |
| `self-drive` | can be taken from issue to merge without a hands-on step |
| `hands-on` | needs the developer; the reason is in an issue comment |
| `blocked` | waiting on another issue ("Blocked by #n" in the body) or an answer |

The developer approves the triage. **Pick:** the highest-priority `self-drive` issue whose blockers are closed. Reference the
issue in the branch name (`feat/<n>-<slug>`) and in the merge commit (`Closes #n`, which closes it when `main` is pushed).
Issue text follows the no-personal-data rule (CLAUDE.md).

### Upstream code

Upstream files we take are **vendored and regenerated**: `tools/sync` copies them from the git-ignored `upstream/` cache at the
commit in `UPSTREAM.lock`, applying the transform rules (see #3).
- Run `node tools/sync/sync.mjs [<commit>]` (default: `main` of the cache's upstream remote). `--dry-run` writes
  nothing; `--check` regenerates from `UPSTREAM.lock` and fails on any difference. The cache needs full history.
- **Never hand-edit a vendored file.** Change a transform rule, or move the file to tier 3.
- The sync and `--check` fail on personal data in a generated file: fix it with a transform rule, or allowlist a genuine
  example in `tools/sync/personal-data.json` (CLAUDE.md).
- CI's `tools/sync --check` fails on any difference.
- Upstream itself is treated as locked: nothing is sent upstream.

## 2. Ask first, always

Never without the developer's go-ahead in the conversation:

- **Anything that takes over the machine:** launching an Avalonia app in a real window, UI automation, screen
  capture of real windows (use the `device-lease` skill). Screenshots come from Avalonia.Headless rendering instead.
- **Tools that need a human:** debugger MCPs (x64dbg/x32dbg), Rider/WebStorm MCPs driving the IDE.
- **Anything that leaves this folder beyond pushing `main` and branches to `origin`:** other remotes, publishing packages, contacting upstream
  maintainers, opening issues or PRs anywhere.
- **Commands that change the system:** installing SDKs or workloads (the .NET 11 SDK is the developer's to install),
  global tools, registry, stopping processes Claude didn't start.
- **Changes inside `upstream/`:** none, beyond keeping the cache current. Read-only git (log, show, diff, grep,
  rev-parse), `git fetch`, and checking out the commit in `UPSTREAM.lock` (setup, or `tools/sync`) are fine. No other
  checkouts, no resets, no edits, no builds there.
- **Repo and Actions settings:** branch rules, visibility, Actions permissions, secrets, variables, runners,
  collaborators, a third-party action, and approving a fork PR's workflow run (never an agent's; see `CLAUDE.md`).
- **Decisions that aren't Claude's:** those under "Decisions already made (don't reopen)" in `CLAUDE.md` are not reopened.

## 3. Build

1. **Branch** from `main`: `<type>/<issue>-<slug>`, where the type is `feat`, `fix`, `chore`, `docs`, `research` or
   `spike` (for example `feat/3-sync-harness`). Leave out `<issue>-` only for work with no issue. Parallel work that changes code
   gets its own worktree under `../<repo folder>.worktrees/<slug>` (a sibling, so nothing scans it):
   `git worktree add ../<repo folder>.worktrees/<slug> -b <branch> main`.
2. **Build** in small commits. Before every commit: `dotnet build` and `dotnet test` for the projects touched.
   Stage files by name, never `git add -A`.
3. **Measure, don't estimate:** every number in FINDINGS.md says how it was measured (command or script, which is
   committed under `tools/` or the spike's folder).
4. **Document** in the same branch: FINDINGS.md section for the spike.

## 4. Self-review

1. **Pass 1:** `code-review high main...<branch> --max-findings all` (`medium` for a small fix). Name the branch;
   never a range ending in `HEAD` from a worktree. Fix each finding or waive it with a reason; findings can be wrong.
2. **Pass 2:** `code-review medium main...<branch> --max-findings all` on the fixed branch.
   Docs-only and chore branches get one pass at `medium`.
3. **Prove the tests can fail:** on committed work, break each behaviour a new test covers, see it go red, restore.
4. **Clean run:** fresh clone of the branch (`git clone -b <branch> <repo root> <temp>`), with
   `upstream` provided by path (it's ignored, so a clone lacks it): build and full test suite.
5. Before merging: `git grep -n -I -E '^(<<<<<<<|>>>>>>>)'` prints nothing.

## 5. Merging

Claude opens a PR for each branch and merges it once the build, tests, both review passes and the clean run hold, and
the PR's six CI checks pass. Merge with `gh pr merge <n> --merge --subject <subject> --body-file <file>`: the merge
commit message records each pass's level, tokens ("not measured" if not visible), what it
found, fixed and waived, tests proven to fail, **For you to decide** and **Hands-on follow-ups**. Then delete the
branch and its worktree.
- **Ruleset `main`:** no deletion, no force-push, a PR for every change (no direct push, no bypass), the six CI checks
  required and the branch up to date with `main`. Merge commits or squash.
- **Identity:** a merge made on GitHub takes its author from the developer's GitHub profile (the name falls back to the
  login, and email privacy gives the noreply address). Check the first merge after any profile change.
- **Up to date:** merge `main` into the branch, re-run the suites, push, wait for CI, then merge.

## 6. Batches

Two or three agents at once, each on **Opus set explicitly**, briefed with `docs/agent-brief.md`, each with its own
temp folder. Agents never merge or push `main`. Research agents (no code) **return their report in their reply**, since
they can be blocked from writing report files; the coordinator commits it under `docs/` and folds the results into
FINDINGS.md. **Shared files** agents change only when their issue can't be done without it (and say so):
`CLAUDE.md`, `README.md`, `docs/PROCESS.md`, `docs/agent-brief.md`, `docs/LESSONS.md`, `FINDINGS.md`, `UPSTREAM.lock`, `global.json`, `.gitignore`, `.gitattributes`, `Directory.*.props`, CI workflows.

## 7. End of session

Update the project memory with what's in flight (branches, worktrees, agents) and what's next. Check
`git branch` and `git worktree list` before trusting a note.

## Decisions this project made once

| Decision | Choice |
|---|---|
| Task list | GitHub Issues, labels `P0`…`P3`, type, `self-drive`/`hands-on`/`blocked` (2026-11-23) |
| Who merges | Claude, through a PR after the gate in §4 and green CI; the developer can hold any PR (#7, 2026-11-24) |
| Batch size | 2 or 3 agents, each on an explicit model (`opus`), briefed with `docs/agent-brief.md` |
| Screenshots | Rendered headless; a real window is ask-first |
| Away mode | Only when the developer says so; decisions are logged in FINDINGS or the issue |
| Review depth | Behaviour changes: `high` then `medium`. Docs and chores: one `medium` pass. Never `max`/`ultra` unless asked |
| Upstream | Read-only; vendor and regenerate (#3); nothing sent upstream |
| Personal data | None in the repo, issues or PRs (CLAUDE.md, hard rules) |
