# Lessons

Traps already hit, with the fix. Some come from earlier projects run the same way, and some from this one. Add one
when a new trap is hit, with the date. Never name people, other projects or clients.

## This project

- **The working directory drifts into `upstream/`, and its `global.json` changes the toolchain.** `dotnet test` then
  ran under Microsoft.Testing.Platform: it printed "Passed!" with no counts, and later rejected `--filter` and
  `--logger`. Give every build and test call an explicit working directory, and distrust a summary without counts.
  (2026-11-22)
- **A review finding the reviewer only "recalls" must be proven before it is fixed.** One review said "alignment changes
  don't re-measure in Avalonia". A test written for it stayed green with the fix removed, so the fix was dropped and the
  test kept as a guard. (2026-11-22)
- **A predicted bug can be real with a different cause.** A finding about a nested FlexPanel was reproduced by a test,
  but the cause was another one: Avalonia re-measures a child at its old exact slot. The fix followed the test, not the
  prediction. (2026-11-22)
- **Merging a branch that untracked a file deletes the working copy.** `git rm --cached` kept HANDOVER.md, but merging
  that change into a branch where the file was still tracked removed it from disk. Restore it from the previous commit
  (`git show HEAD~1:<file>`) once it is ignored. (2026-11-23)
- **A junction inside a worktree makes recursive deletes dangerous.** `git worktree remove --force` stopped at the
  `upstream` junction with "permission denied". Delete the link itself first (PowerShell `(Get-Item <link>).Delete()`),
  then the folder. Check `upstream/` is intact afterwards. (2026-11-22)
- **Research agents may be blocked from writing report files,** and their transcript file can come back empty. Brief
  them to return the whole report in their reply. (2026-11-22)
- **Branch names in merge subjects carry usernames.** "Merge pull request #N from org/<user>/…" brought contributors'
  usernames into script output. Strip the source branch when collecting history. (2026-11-23)

## Git, GitHub and merging

- **Stage files by name.** `git add -A` has committed a screenshot that should never have left the machine, and would
  sweep up untracked planning folders.
- **Never `git checkout -- <file>` or `git restore` over uncommitted work** to undo a deliberate break: it once threw
  away a new class. Commit first.
- **Strict branch protection makes merges serial.** Merge one PR, run `gh pr update-branch <next>`, wait for checks on
  the new head, then merge. The merge queue isn't available for personal-account private repos.
- **Counters bumped by two PRs merge "cleanly" but wrong.** Both raise the same value from the same base, so git sees
  identical lines, but the true total is the sum. Re-bump when updating the second.
- **Force-push may be blocked by the auto-mode classifier.** Update PR branches by merging the base in.
- **Squash merges strand stacked branches** and lose late pushes. Check `gh pr view <n> --json state` before pushing
  to a PR.
- **`gh pr checks` can report an old run.** Check the run for the exact head SHA: `gh run list --commit <sha>`. Check
  the jobs too: GitHub sometimes creates none, and the run looks done.
- **New PRs can open as drafts** without `--draft`. Check `isDraft`; run `gh pr ready`.
- **`gh pr merge --auto` stays armed through `gh pr update-branch`.** It merges before any local-only re-run.
- **Conflict markers can reach `main` through a green build** when they sit in a doc CI doesn't build. After any merge
  or rebase, `git grep -n -I -E '^(<<<<<<<|>>>>>>>)'` must print nothing.

## Parallel agents

- **Give agents an explicit model** (`opus`). Agents that inherit a capped model get cut off mid-task.
- **One temp folder per agent.** Shared ones get overwritten.
- **Agents never run git in the main clone,** and start every shell call with `cd <worktree> &&`.
- **`git worktree add -b x origin/main` makes `x` track `origin/main`.** Use `--no-track`, then `git push -u origin
  HEAD`.
- **Worktrees don't have git-ignored data** (here: `upstream/`). Provide it, and check the tests that need it didn't
  silently skip.
- **Shared files cause the conflicts.** Agents propose changes to them in their report instead of making them.
- **Agents don't stop processes they didn't start.**

## Reviews and tests

- **Never give `code-review` a range ending in `HEAD` from a worktree.** The review runs in the main clone. Name the
  branch (`<level> main...<branch>`) and check the files it lists are the branch's.
- **The default `code-review` cap is 10 findings.** Use `--max-findings all`.
- **Review passes are most of a change's token cost.** Set the level by what the pass checks (PROCESS §4) and always
  write it in the command.
- **A breakage list goes stale under review fixes.** Rerun the breaks on the final head. A break that no longer
  applies, or no longer builds, is still owed.
- **Prove the tests can fail.** Break each behaviour; a test that stays green is weak. Here, a margin test passed with
  margins ignored, because symmetric margins plus centring hid the bug.
- **A real run finds what tests miss.** Keep one real-window run per batch; it is ask-first.
- **`--no-build` runs stale binaries.**
- **Test in the layouts an app really uses,** not only a fixed-size box.
- **A custom Avalonia automation peer built on `ContentControlAutomationPeer` is invisible to screen readers.** That
  base class reports `IsControlElement` and `IsContentElement` as false. Override both to true, as
  `ToggleButtonAutomationPeer` does. Headless tests pass regardless.
- **PowerShell unrolls a one-item array a function returns.** Return it with `Write-Output -NoEnumerate`, and test the
  one-item case.

## Working with the developer

- **Push back with evidence** before building, then follow the answer.
- **Check a number a decision rests on before offering it.**
- **Put several questions in one dialog** with recommended options, not a wall of text.
- **Report tokens per change,** measured or said to be an estimate.
- **Scripts run on Linux, macOS and Windows:** Node, Python (stdlib) or PowerShell 7, with paths built with
  `path.join`/`Join-Path`.
- **Ask before anything that takes over the machine:** real windows, UI automation, devices.
