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

- **Vendored bytes need `-text`.** With `core.autocrlf=true` a checkout turns LF into CRLF, so a byte-exact `--check`
  fails on every Windows clone. `tools/sync` writes `src/Upstream/.gitattributes` with `* -text`. (2026-11-23)
- **Squash a branch whose history once held personal data.** An intermediate commit vendored a third-party notice
  naming a person; a later commit removed it. A `--no-ff` merge would have kept it in `main`'s history, so the branch was
  squashed into one commit. (2026-11-23)

- **A test that makes commits needs its own git identity.** A harness test passed on a developer machine and failed on a
  fresh CI runner ("Author identity unknown"): the code under test committed with the machine's identity. Fixture repos
  set `user.name`/`user.email` locally; reproduce with `GIT_CONFIG_GLOBAL=<empty file>` and `GIT_CONFIG_NOSYSTEM=1`.
  (2026-11-23)

- **`Dispatcher.UIThread` can change between Avalonia headless tests.** A cached UI-thread queue kept posting to an
  old dispatcher. Don't cache it; compare before reusing. (2026-11-23)
- **`Task.Run(...).GetAwaiter().GetResult()` can run the task inline on the waiting thread,** so a test of
  off-thread code silently ran on the UI thread. Use a dedicated `Thread`. (2026-11-23)

- **A guard on raw text fights ordered transform rules.** A rule that ate another rule's match made the "matches an
  unlisted file" guard fire. Check the text as transformed so far, and run repo-wide (glob) rules last so they can't hide
  new code from the guard. (2026-11-23)

- **Avalonia raises `TextBox.TextChanged` after the write, even outside a window.** A controlled write's echo arrives
  late, so each write needs its own suppression token, and headless tests pump the dispatcher. (2026-11-23)
- **Rules for newly vendored files must come before broader existing rules.** The "matches an unlisted file" guard sees
  the text as transformed so far, so a broader rule that runs first can consume what a new file-specific rule expects.
  (2026-11-23)
- **A test of an update path needs an element that changes,** or the update is skipped and the test passes with the
  behaviour broken (found while proving a test). (2026-11-23)

- **A regex scan can be quadratic on one long line, and ASCII in UTF-16 is valid UTF-8.** The first personal-data scan
  took 1.1 s on a 40k-character line (an unanchored email pattern), and read UTF-16 text as UTF-8 with NULs, finding
  nothing. Anchor patterns with a lookbehind, test a long line, and treat NUL bytes as binary. (2026-11-23)

- **Avalonia 12 binds `Dispatcher.UIThread` to the first thread that reads it** (reading it,
  `Dispatcher.CurrentDispatcher`, or creating a control). Ask `Dispatcher.FromThread(Thread.CurrentThread)` before
  `Dispatcher.UIThread.CheckAccess()`. A vendored upstream test found it as a 10 s timeout. (2026-11-23)
- **Avalonia clamps `DesiredSize` to the size offered; WinUI doesn't,** so ported code that measures at 0 to find
  min-content reads 0. (2026-11-23)
- **Avalonia's TextBlock lays out nothing at a width of 0** (its text layout is 0 wide, one line), so probe min-content
  at a width of 1 and add the Padding back. (2026-11-23)
- **A reorder test must move the row it checks:** a 3-item reverse leaves the middle row in place, so a positional diff
  passed it. (2026-11-23)
- **Plain `[Fact]` and `[AvaloniaFact]` tests share process-wide engine state** (`ReactorApp.UIDispatcher`); tests that
  set it run in a non-parallel collection. (2026-11-23)

- **An exception escaping an Avalonia dispatcher job also faults the job's task, which nothing observes.** Collected
  later, it failed upstream's `UseResourceThreadingTests`, which count unobserved task exceptions. A test that lets one
  escape forces a collection while it runs alone. (2026-11-23)

- **A deliberate break that doesn't compile proves nothing:** with TreatWarningsAsErrors, `if (false)` fails the build
  and every test with it. Break with a condition the compiler can't fold. (2026-11-24)
- **A temp clone of upstream on Windows needs `git -c core.longpaths=true`;** its tests/ paths are too long otherwise.
  (2026-11-24)

- **A control holds one event payload.** Every hand-coded event and controlled entry on one control must use the same
  payload type, or they replace each other's (a ComboBox lost its selection callback). (2026-11-24)
- **Avalonia's DatePicker sets MinYear/MaxYear in its constructor,** so ClearValue doesn't restore them, and it throws
  when MinYear passes MaxYear: write both, in order. (2026-11-24)
- **Avalonia raises Expander's Expanded/Collapsed after a posted transition;** a controlled entry listens to
  IsExpanded's change instead. (2026-11-24)
- **With `using static Isotope.UI.Factories`, `Grid.GetRow(...)` and `Border.BackgroundProperty` name the factory
  (CS0119).** Use an alias or the full name. (2026-11-24)

- **Yoga doesn't call a measure function for a child it sizes Exactly on both axes** (a basis of 0 with stretch), so
  FlexPanel's own final sweep measures that child, and a fix in the Yoga bridge alone misses it. (2026-11-24)
- **Two parts of a fix that each correct a bug hide each other from break tests.** A test stayed green with one part
  removed because the other corrected the layout one pass later. Prove it with both removed, and test what each part
  alone buys. (2026-11-24)

- **A headless benchmark that runs inside one dispatcher job never pumps the queue.** Closed windows piled up until the
  process reached 8.8 GB. Run `Dispatcher.UIThread.RunJobs()` and render a frame between runs, and print the heap at the
  end. (2026-11-24)
- **The first case timed in a fresh process runs cold.** A mount timed first took about 5 times as long as the same work
  later. Warm the JIT with an unprinted pass of every case before timing. (2026-11-24)

- **Roslyn `drop-member` keeps a `//` comment above the member** (only `///` and attributes go), so the comment
  outlives what it describes. Blank it with a text rule. (2026-11-24)
- **Replacing our type with upstream's can move a check from construction to render.** A setter that clears and then
  converts leaves the control half-built when the conversion throws. Convert first, then clear. (2026-11-24)

- **A keyed-move test must check that each key keeps its own control:** a wrong Move index left the texts in order,
  because the reconcile updated the swapped rows in place with each other's elements. (2026-11-24)

- **Avalonia's `SelectionChanged` and `PaneOpening`/`PaneClosing` bubble,** so a nested control's event reaches the
  outer control's handler: check `Source`. A test of this outside a window proves nothing, since untemplated content has
  no route to its host. (2026-11-24)
- **Avalonia's menu handler toggles a MenuItem before raising `Click`;** raising `ClickEvent` directly skips that. Click
  menus through a headless window. (2026-11-24)
- **A controlled value converted between types** (a double record, a decimal control) must be compared in the control's
  type, or a write that changes nothing leaves an echo token that swallows the next user change. (2026-11-24)
- **TabControl always keeps a tab selected,** so adding or removing tabs moves the selection inside the engine's update.
  (2026-11-24)

- **A break that crashes the test host (a stack overflow) shows as a smaller Total, not as the failing test.** Rerun
  that break filtered to the one test. (2026-11-24)

- **Avalonia's AutoCompleteBox raises `TextChanged` when it is shown with text,** without the text changing: drop a
  change event that leaves the text as it was. (2026-11-24)
- **A control's command disables it only once it is in a logical tree:** test command buttons in a headless window.
  (2026-11-24)
- **A subtree hung on an attached property (a tooltip) is invisible to walks over children and parents:** the dirty path
  and the context check must go through its owner. (2026-11-24)

- **A record member named like the control's content property becomes a generated child slot, even with
  `[WrapManual]`** (which does stop an items host); rule `gen-manual-content-slot` handles it. (2026-11-24)

- **csc doesn't run analyzers when a file has declaration errors, but `dotnet format` does:** count migration
  diagnostics outside the build. (2026-11-24)
- **Roslyn 4.14 can't read C# 14 extension members,** so tests compiling against `Isotope.UI.Avalonia.Desktop` need the
  SDK's compiler version. (2026-11-24)
- **`dotnet build -getItem` without `-t` only evaluates the project;** `dotnet msbuild -t:Build -getItem:…` prints the
  items even when the build fails. (2026-11-24)

- **A break must stop the behaviour, not one path to it:** `if (CheckAccess() && sender is null) Refresh()` still
  refreshed through the `else Dispatcher.Post(Refresh)` branch, so the test stayed green. Empty the handler instead.
  (2026-11-24)
- **`measure-samples.mjs` builds the worktree's library and checks git status at the end:** don't edit the worktree
  while it runs. (2026-11-24)

- **A scripted replace of a multi-line block must match exactly that block:** a pattern for "this bullet plus two
  lines" took the next bullet with it, and the README lost a line on `main` unnoticed. Diff the result before committing.
  (2026-11-24)

- **A skip count can come from a child:** an equal child under an updated parent is skipped too, so a test that a parent
  is skipped watches the parent's handler (a setter that counts), not `DebugElementsSkipped`. (2026-11-24)
- **`git checkout -- <file>` writes CRLF on Windows,** so an LF-anchored scripted edit made after restoring a break no
  longer matches: normalise line endings in the replace. (2026-11-24)

- **A portable PDB stores document paths in parts on its blob heap:** a byte search for a path finds nothing. Decode
  the Document table. (2026-11-24)
- **`dotnet build` keeps MSBuild nodes alive that lock DLLs in a temp `NUGET_PACKAGES` folder:** pass
  `--disable-build-servers`. (2026-11-24)
- **A headless `MouseDown`/`MouseUp` runs the due dispatcher timers first,** so a slow runner turns a click into a hold:
  a RepeatButton repeats, and a hover tooltip opens over the next control and takes its click. Mouse-click helpers that
  can reach a RepeatButton call `TestHost.HoldOffRepeat`, and the test app gives tooltips a one-minute `ShowDelay`. Reproduce with a
  `Thread.Sleep` between press and release (#55). (2026-11-24)
- **A project that references Isotope.UI.Avalonia.Desktop gets Avalonia's native libraries for every platform** (592 MB
  of Debug output for the MCP tool); a project that only reflects over the library drops them with a target after
  ResolvePackageAssets (src/Isotope.UI.Mcp). (2026-11-25)
- **An MCP stdio test that the server breaks can hang rather than fail,** leaving testhost and server processes that lock
  bin/ (MSB3027 on the next build): give stdio tests `[Fact(Timeout = ...)]`. (2026-11-25)
- **A ProjectReference to an Exe copies its runtimeconfig.json and deps.json next to the referencing tests,** so the
  tests can start it with `dotnet <tool>.dll` from their own folder. (2026-11-25)
- **C# 14 on net9.0 binds `array.Reverse()` to the span overload,** `MemoryExtensions.Reverse(Span<T>)`, which returns
  `void` (CS0023 on `.ToArray()`). .NET 10 added `Enumerable.Reverse(T[])`, so only net9.0 breaks. Write
  `Enumerable.Reverse(array)`. (2026-11-25)
- **C# 14 extension members can't be called at a framework's default C# version below 14.** A net9.0 app (C# 13) gets
  CS9260 on `ReactorApp.Run`. Measure a consumer at each target's default `LangVersion`, not the library's. (2026-11-25)
- **A package built in a clone of a local folder has no SourceLink.** Its `origin` is a path, so pack.mjs reports
  every package (no repository URL, no SourceLink). Set `origin` to the GitHub URL in a scratch clone first. (2026-11-25)
- **Avalonia's TextBlock has two contents that clear each other.** Writing `Text` (even `""`) empties `Inlines`, and adding
  an inline to a text block with text turns that text into a run. Write one or the other, and clear the other first. (2026-11-25)
- **A non-panel control's handler owns its child teardown.** Its unmount returns `CollectSelf`, so the generic
  `UnmountChildren` walk never reaches a child slot the handler mounted (a text block's inline UI): tear it down in the
  descriptor's unmount hook, and add the walk only for the isolated-teardown recovery. (2026-11-25)
- **A `using static Factories` method shadows an Avalonia type of the same name in a constant pattern.** `LineBreak =>`
  bound to the `LineBreak()` factory (CS0428); write a declaration pattern, `LineBreak _ =>`. (2026-11-25)
- **Roslyn binds no method body once a declaration has an error.** A probe expecting two errors, one in a declaration and
  one in a body, reports only the first: give each its own project (Spike J). (2026-11-25)
- **Avalonia's build telemetry can hold a DLL in a temporary NUGET_PACKAGES after a build.** Deleting the folder hit
  EPERM in 2 of 10 runs, after every test had passed: retry the delete and warn, as pack.mjs does, and set
  `AVALONIA_TELEMETRY_OPTOUT`. Set `process.exitCode`, not `process.exit()`, or piped output can be lost. (2026-11-25)
- **`dotnet test --no-build` exits 0 and prints nothing once the restore's NUGET_PACKAGES is gone.** The test SDK's
  props and targets aren't imported. Check for a result per framework, never only the exit code. (2026-11-25)
- **In a break-it loop, check the build's exit before reading the tests.** A break that also fails the build (CS0067
  under warnings-as-errors) leaves the previous binaries to pass. (2026-11-25)
- **dotnet format fixes the rules in a different order each run.** It takes the reported IDs from a hash set of strings,
  whose order follows the per-process string hash. Two runs on one project can add usings in another order and take a
  different number of passes. Compare runs by their counts, or with using directives sorted. (2026-11-26)
- **Don't build the worktree while `measure-samples.mjs` runs.** A worker holds DLLs it has loaded, and a build failed to
  copy `Isotope.UI.Avalonia.Generators.dll` over one. (2026-11-26)
- **A CPU-bound machine measures faster with fewer projects at once.** Each msbuild build and compile already uses more
  than one processor; on 4, 2 at once beat 4 by a third. (2026-11-26)

## Git, GitHub and merging

- **The harness can block an agent's write to `FINDINGS.md`:** have agents return the section in their reply. (2026-11-24)

- **`git commit-tree` with the same tree, parent, message and identity in the same second gives the same commit:** a
  crafted "other" commit was the genuine one on a fast runner. Change a file or the date. (2026-11-24)
- **`actions/checkout` stores a token header for github.com in the checkout's git config:** run `git ls-remote` of
  another repo from outside the checkout. (2026-11-24)
- **A first push of a new branch that changes no filtered path doesn't start a `paths:`-filtered workflow:** dispatch it
  instead. (2026-11-24)

- **A merge made on GitHub (its button or `gh pr merge`) takes the author from the GitHub profile,** not from git config:
  the profile's name (the login if none is set) and the primary email unless email privacy is on. One leaked a full name
  and email into `main` and had to be rebuilt. Keep the profile name empty and email privacy on. (2026-11-24)

- **Actions minutes run out fast on a private repo:** one full CI run billed about 26 minutes (Windows jobs count 2x), and a
  day of agent work pushed about 90 runs. The repo went public on 2026-11-24, where hosted runners are free. (2026-11-24)

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
- **A branch push cancels the branch's running CI (`cancel-in-progress`).** With a 16-minute job, push a proof break and
  its revert one after the other, each after its run ends, not together. (2026-11-25)
- **`gh run view --job <id> --log` waits until the whole run ends.** Read a finished job's log early with
  `gh api repos/<owner>/<repo>/actions/jobs/<id>/logs`. (2026-11-25)

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
- **A proof helper that restores with `git checkout --` wipes uncommitted edits to that file.** Commit before proving,
  every time, even for a quick one. (2026-11-25)
- **A conditional's tuple type comes from its typed arm.** `cond ? (20.0, family) : default` is `(double, FontFamily)`,
  so `default` is `(0, null)`, not two nulls: a font size of 0 then threw in unrelated text tests. Cast the arm
  (`((double?)20.0, family)`). (2026-11-26)
- **A layout fit test that measures with `Measure(Size.Infinity)` reports wrapping text at its one-line width,** and
  depends on the system font (ubuntu-latest's is about 13% wider than Windows'). Check what is actually cut off instead: a
  control past its container's right edge, or one-line text narrower than it wants (#96). (2026-11-26)
- **A test that spawns a process must stop it when an assertion fails.** A failing check before the `kill` left the
  child running, and `node --test` waited on it without end (#106). Keep the children in a set and stop them in
  `finally`; give server tests a timeout. (2026-11-26)

## Working with the developer

- **Push back with evidence** before building, then follow the answer.
- **Check a number a decision rests on before offering it.**
- **Put several questions in one dialog** with recommended options, not a wall of text.
- **Report tokens per change,** measured or said to be an estimate.
- **Scripts run on Linux, macOS and Windows:** Node, Python (stdlib) or PowerShell 7, with paths built with
  `path.join`/`Join-Path`.
- **Ask before anything that takes over the machine:** real windows, UI automation, devices.
