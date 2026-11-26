---
name: real-window-run
description: Run the sample app's real-window checklist with the developer - build and launch the sample app, serve the checklist page, follow the answers live, then record the run (README, FINDINGS, a PR) and file an issue per failure. Use when the developer asks to run, check or test the sample app in a real window, or names a real-window issue (a Linux, macOS or Windows run, or a page's hands-on check).
---

# Real-window run of the sample app

A real window is ask-first (`docs/PROCESS.md` §2): run this when the developer asks for it, never on your own. The
developer clicks through the app; you launch it, serve the checklist and record what they answer.

The checklist is `samples/Isotope.UI.Samples/README.md`; the page is `tools/real-window/checklist.mjs` (its README
section "How to run the checklist" says what it does). Work from the repository root, on an up-to-date `main` (or the
branch the developer names).

## 1. Prepare

- `git pull` (or check out the branch), then note the commit: `git rev-parse --short HEAD`.
- Give every command an explicit working directory, the repository root (`cd <repo> && …` or `.cwd(<repo>)`): the shell's
  directory drifts into `upstream/` (CLAUDE.md).
- Build first, so a build error isn't mistaken for a failed check: `dotnet build samples/Isotope.UI.Samples -nologo -v:q`.
- Ask for the system in one line if you don't know it (for example "Fedora 43, Wayland, GNOME" or "Windows 11"), and
  which issue the run is for (`#61` Linux, `#62` macOS, or a page's hands-on issue). The page asks for the system too.

## 2. Serve the checklist and launch the app

Start both in the background (each as its own shell command that moves to the background), checklist first:

- **The checklist page:** `node tools/real-window/checklist.mjs samples/Isotope.UI.Samples/README.md --clear`.
  If `samples/Isotope.UI.Samples/README.md.results.json` exists, an earlier run is unfinished: ask the developer
  whether to resume it (leave out `--clear`) or start again (delete the file, then use `--clear`). Its answers may
  be from another commit; the `ready` line's `saved` count says how many there are. Its first stdout line is a `{"event":"ready","url":...}` JSON line: give the developer that
  URL.
- **The app:** if the `device-lease` skill is available, launch through its `run desktop` command (that skill gives
  the script's path) so no other agent takes the screen, with `--who Isotope`, `--for "sample app at <commit>,
  real-window run"`, `--wait-minutes 0` (don't wait for a busy desktop: exit 75 means another agent holds it; say who,
  from its `status`, and stop) and `--max-minutes 240` (a run with breaks can pass the default two hours), then `--
  dotnet run --no-build --project samples/Isotope.UI.Samples`. Without that skill (another machine), run the `dotnet
  run` directly. Don't pass `--idle-seconds`: the developer is at the machine and waiting. Check the window opened
  (the launch command is still running and printed no error) before saying so. Don't drive the window: no clicks,
  keys or screenshots.

Tell the developer the page's URL, that the app is open, and that you're following their answers.

## 3. Follow the answers

Every answer is one JSON line on the checklist tool's stdout. Watch its output file with the Monitor tool
(`tail -n +1 -f <output file>`, with the longest timeout, re-armed when it expires) rather than polling. Don't reply
to each answer; a short note every few is enough. When a line fails, say you've got it and carry on. The run ends with
a `{"event":"finish",...}` line (the developer clicked Finish), or when the developer says they're done.

If they close the app or stop partway, the answers are saved; the next run of the page resumes them (no `--clear`).

## 4. Record the run

After Finish, the checklist file has the ticks and notes. The stdout `finish` line carries the suggested "Last run"
sentence (`sentence`) and the failed lines (`failed`); use those (the human-readable summary goes to stderr). Then:

1. **Branch:** `docs/<issue>-real-window-<system-slug>` from `origin/main` (carry the changed README over).
2. **README:** rewrite the "Last run" paragraph from the suggestion: date, system, commit, issue, what passed and what
   failed, and what wasn't run (skipped lines, or pages added since). Keep earlier runs' facts that still hold.
3. **FINDINGS:** update the "Sample app" bullet (the real-window sentence) with this run's date, system, commit and
   result.
4. **Failures:** file one issue per failed line (`bug`, P2 unless the developer says otherwise, `self-drive` if it can
   be fixed headless, otherwise `hands-on`), quoting the line, the developer's note, the system and the commit. Link
   them from the README note.
5. **PR:** `Closes` the run's issue only if every line it covers passed: a failed, skipped or unanswered line means the
   PR links the issue instead. Follow PROCESS for the
   rest: one `code-review` pass at `medium main...<branch> --max-findings all`, the required checks, a squash merge
   with the review and checks in the message.
6. Stop the checklist tool, and ask the developer to close the app window if it is still open: stopping the
   background launch command may leave the app running (its process tree isn't always killed). Then check the desktop
   lease is released (the device-lease skill's `status`).

## Rules

- No personal data in what you write: the system line is what the developer types; no usernames, machine names, local
  paths or URLs containing the owner's name (use issue and run numbers).
- Write only what the developer answered. A line they didn't answer stays as it was; don't tick it from memory or from
  headless tests.
- Questions go in chat, not on GitHub.
