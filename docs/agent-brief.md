# Brief for an agent working one issue

Give each agent this file, its issue number, its worktree, its own temp folder and an explicit model (`opus`).
Keep this brief current as mistakes are found.

You're building one Isotope.UI.Avalonia issue, possibly while other agents build others. Read `CLAUDE.md`, `README.md`
and `docs/PROCESS.md` first. Then follow PROCESS §3 (build) and §4 (self-review). **You never merge into `main` and never push `main`:** the
coordinator merges (§5).

## Setup

1. Your worktree's absolute path is in your prompt. **Start every shell call with `cd <worktree> &&`** (or `git -C
   <worktree>`), because the working directory resets. Check `git rev-parse --show-toplevel` before each commit.
2. Your worktree has no `upstream/`, because it's git-ignored. Link it to the main clone's `upstream/` with a junction or
   symlink. **Never delete through that link,** and never run git commands that write inside `upstream/`.
3. Read the issue and its comments. If the work is more than a small change, post a plan as an issue comment.

## Rules, because others work at the same time

- **Stage your issue's files by name.** Never `git add -A` or `git add .`.
- **Shared files:** `CLAUDE.md`, `README.md`, `docs/PROCESS.md`, `docs/agent-brief.md`, `docs/LESSONS.md`, `FINDINGS.md`, `UPSTREAM.lock`, `global.json`, `.gitignore`, `.gitattributes`, `Directory.*.props`, CI workflows.
  - Change them only if your issue can't be done without it, and say so.
  - A change you'd merely like goes under **Proposed for shared files** in your report, with the exact text.
- **Temp files go in your own temp folder,** never in the repo.
- **Don't stop processes you didn't start.**
- **Never do anything on the ask-first list** (PROCESS §2). If your issue needs it, stop that part, file a `hands-on`
  follow-up issue, and finish the rest.
- **Don't ask the developer anything.** Take the conservative choice and list it under **For you to decide**.

## Getting it right

- No personal data anywhere (CLAUDE.md, hard rules).
- Never hand-edit a vendored upstream file. Change a transform rule instead.
- Give every `dotnet` call an explicit working directory, and distrust a test summary without counts.
- Numbers you report are measured: test counts, breakage counts, error counts, timings.
- Review findings can be wrong; check each one against the code and upstream.
- Never restore a file over uncommitted work. Commit first, then break things to prove the tests.
- Build before testing. `--no-build` runs stale code.
- Before handing back: `git grep -n -I -E '^(<<<<<<<|>>>>>>>)'` prints nothing.

## Review

Run the `code-review` passes from PROCESS §4 on your branch: `<level> main...<branch> --max-findings all`. Name the
branch; never use a range ending in `HEAD` from a worktree. Record each pass's level and token count.

## Report back

Your report is your reply. Don't write report files, because agents can be blocked from writing them. Keep it short:
- the branch and head commit;
- the test count, and the tests proven to fail;
- the review passes (level, tokens, found, fixed, waived);
- **For you to decide**;
- **Hands-on follow-ups**;
- **Proposed for shared files**.
