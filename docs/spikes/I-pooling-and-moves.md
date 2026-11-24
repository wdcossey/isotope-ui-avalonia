# Spike I: control pooling and keyed moves on Avalonia (issue #14)

Measured 2026-11-24 on the development machine: Avalonia 12.1.6 headless (no OS window), Skia and the Fluent theme as
the library's tests use them, .NET SDK 11.0.100 targeting `net10.0`, Release. Program: `spikes/I-pooling/Measure`.

## Answer

- **Pooling: don't port it. Close #14 with these numbers.**
  - Creating controls is cheap on Avalonia: 4,000 controls (1,000 rows) in 2.3 to 3.9 ms.
  - With upstream's pool policy (at most 32 controls per type), re-keying 1,000 rows in a window gains nothing
    measurable over creating them.
  - Reuse only pays with a pool big enough to hold every row: in a window, 16% on a full re-key and 52% on a mount. That
    saving comes from Button templates that a reused control keeps. It costs a reset for each control type, keeps every
    unmounted control alive, and doesn't help small edits.
- **Keyed moves: use Avalonia's `Controls.Move`.** This is the change that pays.
  - Upstream moves a child with `RemoveAt` then `Insert`, which on Avalonia takes the row out of the visual and
    logical trees and puts it back. Reversing 1,000 keyed rows in a window costs 141 to 149 ms.
  - With `Controls.Move` it costs **6.4 to 7.7 ms** through the engine, about 20 times less. No row leaves the tree, and
    the focused control of a moved row keeps focus (with upstream's `RemoveAt` and `Insert` it lost focus in 7 of 7
    runs).
  - **No transform rule is needed.** Upstream's `PanelChildCollection` is built in exactly one place, our tier 3 seam
    (`Seam/Reconciler.V1.cs`), so the seam can pass its own `IChildCollection` and no vendored file changes. The
    change is `spikes/I-pooling/move-collection.patch` (one new 50-line class and one changed line). All 811 library
    tests pass with it applied. Porting it, with tests, is a follow-up.

## Method

`spikes/I-pooling/Measure/Program.cs`, a console app. Rows are shaped like Spike G's: a memoized function component with
`UseState`, rendering a horizontal StackPanel of a TextBlock and a Button. The reconciler wraps each component in a
Border, so a row is 4 controls.

- **Runs.** First an unprinted pass of every case, so the JIT has tiered up. Then each case runs 7 times after its own
  warm-up, on a fresh tree, with the dispatcher pumped, a frame rendered and a full GC before each timed part. The
  program prints medians of:
  - the change alone;
  - the change, a layout pass (`UpdateLayout`) and the jobs the change posted to the dispatcher (`RunJobs`: Avalonia
    raises `Loaded` and `Unloaded` that way);
  - KB allocated on the UI thread over all of that (`GC.GetAllocatedBytesForCurrentThread`);
  - gen0 collections.
- **Two hostings for every case.** *Detached* has no window, like Spike G's numbers. *Window* is a shown headless 800×600
  window, where attaching, styling, templates and layout happen. The window numbers are the ones an app pays.
- **Checks on every run, warm-ups included:**
  - the rows end up in key order;
  - every surviving key keeps its row control;
  - in a window, the expected rows are detached: each removed row, and each moved row with `RemoveAt` + `Insert`;
    none with `Controls.Move`;
  - after a replayed move, the visual and logical children are in the new order and the rows are arranged top to bottom.

  The program exits non-zero if a check fails. Every run below passed all of them. Kept, detached and focus counts in
  the notes cover the 7 timed runs, not the warm-up.
- **Sections of the program:**
  1. **Engine.** Churn and reorders through the library's reconciler and upstream's ChildReconciler.
  2. **Fresh against reused controls, at the Avalonia layer.** The same 4 controls per row are created fresh, or rented
     from a per-type pool of used controls. Returning a control takes it apart and `ClearValue`s what was set, as
     upstream's `ElementPool.CleanElement` does; a pooled Button keeps its click handler, as upstream's pool does. The
     pool comes in two sizes: unbounded, and capped at upstream's 32 per type. This models what a pool behind
     `RentControl` could save without changing the library; the engine's own work is the same with or without a pool.
  3. **Moves.** The moves the engine makes for each reorder are recorded from the panel's collection events: the engine
     only moves for these, 999 for a reverse and 941 for the seeded shuffle. They are replayed on built rows, as
     `RemoveAt` + `Insert` (vendored `PanelChildCollection.Move`) and as `Controls.Move`. A FlexPanel is also
     reversed with `Controls.Move` and checked to lay out in the new order.
  4. **The engine with Move.** `git apply spikes/I-pooling/move-collection.patch`, rebuild, run section 1 again, then
     `git apply -R` to undo it. The patch was never committed to the library.
- **What the tables show:** the lowest and highest median over 3 invocations of
  `dotnet run -c Release --project spikes/I-pooling/Measure` (1,000 rows, 7 runs), in ms of the change plus layout and
  posted jobs. The change alone is in brackets where it matters.
- **A floor of about 5 ms** shows in every window case that changes the tree, even a one-row move (5.0 to 6.9 ms). It is
  the layout pass and the jobs a change posts, whatever its size; a re-render that changes nothing doesn't pay it.

## Results

### 1. Engine: churn and reorders (1,000 rows)

| Case | Detached | Window | Allocated (window) | Notes (window) |
|---|---|---|---|---|
| mount | 4.5 to 4.8 | 171 to 188 (change 29 to 31) | 69,820 KB | |
| re-render, same keys | 0.17 to 0.25 | 0.34 to 0.39 | 227 KB | 1,000 kept |
| re-key all (1,000 unmounts + 1,000 mounts) | 16.9 to 21.7 | 191 to 195 (change 74 to 79) | 81,451 KB | 0 kept |
| re-key every other | 9.8 to 10.5 | 81 to 88 | 41,370 KB | 500 kept |
| unmount all | 8.7 to 12.1 | 45 to 48 | 12,731 KB | |
| mount again after unmount all | 4.3 to 4.5 | 126 to 142 | 69,592 KB | |
| reverse | 17.1 to 20.9 | 141 to 149 (change 77 to 82) | 49,334 KB | 999 rows detached; focus lost 7 of 7 |
| shuffle (seeded) | 17.6 to 20.5 | 135 to 146 | 46,484 KB | 941 rows detached; focus lost 7 of 7 |
| move last to first | 0.21 to 0.23 | 5.4 to 6.9 | 422 KB | 1 row detached; focus lost 7 of 7 |

"Focus lost" means that a Button in a row the engine moves was focused before the change and wasn't afterwards.

### 2. Fresh against reused controls (1,000 rows, 4,000 controls)

| Case | Detached | Window | Allocated (window) |
|---|---|---|---|
| construct only (no tree) | 2.4 to 2.8 | | 9,217 KB (detached) |
| mount: create | 2.8 to 3.0 | 172 to 190 (change 23 to 30) | 67,513 KB, 5 gen0 |
| mount: rent from a full pool | 2.4 to 3.2 | **83 to 89** (change 32 to 37) | 36,312 KB, 2 gen0 |
| re-key all: create | 11.5 to 13.5 | 176 to 191 (change 65 to 70) | 78,790 KB |
| re-key all: pool, no cap | 13.0 to 15.5 | **143 to 161** (change 67 to 73) | 51,142 KB |
| re-key all: pool, cap 32 (upstream's) | 13.6 to 16.7 | 180 to 191 (change 74 to 77) | 81,623 KB |
| unmount all: drop | 8.5 to 12.4 | 42 to 48 | 12,690 KB |
| unmount all: reset and pool | 10.3 to 14.8 | 50 to 54 | 16,591 KB |

### 3. Moves: the engine's moves replayed (1,000 rows)

| Reorder | RemoveAt + Insert, detached | Controls.Move, detached | RemoveAt + Insert, window | Controls.Move, window |
|---|---|---|---|---|
| reverse (999 moves) | 17.8 to 23.1 | 0.56 to 0.62 | 137 to 154 (49,108 KB) | **5.6 to 6.7** (486 KB) |
| shuffle (941 moves) | 16.3 to 19.9 | 0.48 to 0.89 | 133 to 146 (46,258 KB) | **6.8 to 7.1** (467 KB) |
| move last to first (1 move) | 0.04 to 0.05 | 0.00 to 0.01 | 5.7 to 6.8 | 5.0 to 6.4 |

The change alone, in a window: reverse 75 to 81 ms with `RemoveAt` + `Insert`, against 0.65 to 0.72 ms with
`Controls.Move`. In a window, `RemoveAt` + `Insert` detached every moved row from the visual tree (999, 941 and 1),
and a focused Button in a moved row lost focus in 7 of 7 runs. `Controls.Move` detached none and kept focus in 7 of 7.
A 20-child FlexPanel reversed with `Controls.Move` laid out in the new order.

### 4. The engine with Controls.Move (move-collection.patch applied)

| Case | Detached | Window | Allocated (window) | Notes (window) |
|---|---|---|---|---|
| reverse | **1.2 to 2.3** (was 17.1 to 20.9) | **6.4 to 7.7** (was 141 to 149) | 711 KB (was 49,334) | 0 rows detached; focus kept 7 of 7 |
| shuffle (seeded) | 1.4 to 2.3 (was 17.6 to 20.5) | 7.5 to 8.0 (was 135 to 146) | 692 KB (was 46,484) | 0 rows detached; focus kept 7 of 7 |
| move last to first | 0.16 to 0.19 (was 0.21 to 0.23) | 5.6 to 6.4 (was 5.4 to 6.9) | 371 KB (was 422) | 0 rows detached; focus kept 7 of 7 |

The change alone for the reverse, in a window, went from 77 to 82 ms to 1.48 to 1.51 ms. The cases that don't move rows
(mount, re-render, re-key, unmount) stayed within their ranges in section 1. With the patch applied,
`dotnet test tests/Isotope.UI.Avalonia.Tests` passed 811 of 811.

## Findings

1. **Creating a control is not where the time goes.**
   - Constructing 4,000 controls and setting their values takes 2.4 to 2.8 ms.
   - Re-keying 1,000 rows in a window takes 191 to 195 ms through the engine, and 176 to 191 ms when the same controls
     are built directly. So nearly all of it is Avalonia's: attaching, styling, applying templates, layout and the
     `Loaded`/`Unloaded` jobs. The engine adds little.
2. **What reuse saves on Avalonia is the template.**
   - A reused Button keeps the Fluent template it built on its first measure. For a mount, the layout and posted jobs
     fall from about 150 to 160 ms to about 50 ms.
   - Attaching costs a little more (change 23 to 30 ms, against 32 to 37 ms reused), because the reset values are
     restyled.
   - Net, with an unbounded pool:
     - a mount takes 83 to 89 ms against 172 to 190 ms, with allocations 46% lower;
     - a full re-key takes 143 to 161 ms against 176 to 191 ms, with allocations 35% lower.
   - Detached there are no templates, so reuse gains nothing.
3. **Upstream's policy doesn't capture that saving.**
   - `ElementPool` keeps at most 32 controls of each type, so a re-key of 1,000 rows reuses 32 of each.
   - Measured: 180 to 191 ms against 176 to 191 ms, no gain.
   - To get the saving, the pool would have to keep every unmounted control alive: 4,000 controls for this list.
4. **Returning a control costs time too:** resetting and pooling adds about 2 ms (detached) and 5 to 8 ms (window) to
   unmounting 1,000 rows.
5. **A keyed move on Avalonia detaches the row, and that is the cost of a reorder.**
   - Through the engine, a reverse costs 141 to 149 ms in a window. Replaying just its 999 `RemoveAt` + `Insert`
     pairs costs 137 to 154 ms.
   - Each pair takes the row (and its subtree) out of the logical and visual trees and attaches it again, so it is
     restyled, re-measured and gets `Unloaded` and `Loaded` again. Allocations: 49 MB.
   - It also drops focus: a moved row's focused control loses it.
6. **`Controls.Move` keeps the row attached.**
   - Avalonia's `Panel` handles the collection's Move by moving its logical and visual children. The checks confirm the
     visual and logical order and the arranged positions after each replay.
   - The engine's reverse falls to 6.4 to 7.7 ms in a window, about 20 times less, most of it the 5 ms floor of any
     change. The change itself falls from 77 to 82 ms to 1.5 ms, allocations fall to 1.4%, and focus is kept.
   - `AvaloniaList.Move(old, new)` has the contract of upstream's `Move`: `newIndex` is the final position. The replay
     checks the final order against the engine's on every run, and the 811 tests pass with the patch.
7. **How the move would ship: a seam class, not a transform rule.**
   - The vendored `Core/ChildCollection.cs` declares `PanelChildCollection`, and the only code that constructs it is
     `Seam/Reconciler.V1.cs` (`ReconcilePanelChildrenInto`); no vendored test names it.
   - A tier 3 `IChildCollection` in `Seam/` (the patch's `MovingPanelChildCollection`, which keeps upstream's range
     asserts and edit counters) keeps upstream's file verbatim and adds no rule to watch across syncs.
   - A transform rule would also work: replace the `RemoveAt`/`Insert` body of `PanelChildCollection.Move` with
     `_children.Move(oldIndex, newIndex);`. It would put a tier 2 rule on a file upstream changes, for no gain over the
     seam class.
8. **Measuring traps hit here:**
   - **JIT warm-up.** The first case timed in a fresh process ran about 5 times slower than the same work later, at the
     same allocations (mount, detached: 21.5 ms first, against 4.3 to 4.5 ms after an unprinted pass of every case).
     Spike G's mount figure (37.5 to 40.2 ms), timed first in its process, may carry the same effect [J].
   - **The dispatcher queue.** A headless benchmark that runs inside one dispatcher job never pumps the queue, and closed
     windows piled up: the process reached 8.8 GB before it was stopped. The program now runs jobs and renders a frame
     between runs, and prints the managed heap at the end (6.4 MB).
   - **No direct comparison with #2.** #2's baseline (reverse 47.6 to 78.0 ms, shuffle 19.6 to 23.7 ms, detached) came
     from a program with no GC between runs and no suite-wide warm-up. Detached, this program measures reverse at 17.1
     to 20.9 ms and shuffle at 17.6 to 20.5 ms.

## Recommendation

- **Close #14 without porting pooling.** If a virtualized list arrives later, recycling belongs in it (reusing
  containers that already have their templates), not in `RentControl` [J].
- **Port the move:** a follow-up issue for `MovingPanelChildCollection` in `Seam/`. Its tests:
  - a keyed reorder in a window detaches no row (`DetachedFromVisualTree` count);
  - a focused control in a moved row keeps focus;
  - a FlexElement reorder lays out in the new order;
  - each proven to fail with the seam back on `PanelChildCollection`.

  The existing keyed-list tests already cover order, identity and state.

## What a pool would need, if it is ever ported

- A reset for each pooled control type: every property the engine or a modifier can write, the event payloads it keeps,
  and the reconciler's attached `ReactorState`. Upstream enforces this with `CleanElement`, two tests over the
  poolable list, and the `PoolResetSet*` analyzers.
- A test for each control type that a reused control behaves like a new one.
- A cap large enough to matter, and a decision on how much memory a pool may keep.

## Limits

- **Headless.** A real window also renders, and rendering isn't timed here; a real window is ask-first.
- **The patch is pinned to this branch's base.** Nothing in CI applies it, so a later edit near
  `ReconcilePanelChildrenInto` can make `git apply` reject it.
- **One row shape** (Border, StackPanel, TextBlock and Button). Controls with heavier templates (TextBox, ComboBox) would
  gain more from reuse, and plain panels less [J].
- **The pool is a model at the Avalonia layer,** not a port behind `RentControl`. It doesn't reset the engine's
  per-control state, which a real port would also have to do (more cost on return).
- **One machine,** 3 invocations of 7 runs. How much a case's median varied between invocations:
  - window cases over 50 ms: up to 13% (re-key all, pool, no cap: 143 to 161 ms);
  - window cases near the 5 ms floor: up to 32%;
  - detached cases, all under 25 ms: up to 85% (the engine's reverse with the patch: 1.2 to 2.3 ms).

  Differences inside those margins are not findings.
- **WinUI wasn't measured.** Whether upstream's `RemoveAt` + `Insert` drops focus on WinUI isn't verified (Spike G's
  review said WinUI defers `Unloaded` within a tick).

## Reproduce

```sh
dotnet run -c Release --project spikes/I-pooling/Measure            # [-- <rows> <runs>], default 1000 7; exit 0 = all checks passed
git apply spikes/I-pooling/move-collection.patch                    # the engine with Controls.Move
dotnet run -c Release --project spikes/I-pooling/Measure
dotnet test tests/Isotope.UI.Avalonia.Tests                         # 811 with the patch applied
git apply -R spikes/I-pooling/move-collection.patch
```
