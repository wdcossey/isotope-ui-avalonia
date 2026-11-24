# Spike I: control pooling and keyed moves on Avalonia (issue #14)

Measured 2026-11-24 on the development machine: Avalonia 12.1.6 headless (no OS window), Skia and the Fluent theme as
the library's tests use them, .NET SDK 11.0.100 targeting `net10.0`, Release. Program: `spikes/I-pooling/Measure`.

## Answer

- **Pooling: don't port it. Close #14 with these numbers.**
  - Creating controls is cheap on Avalonia: 4,000 controls (1,000 rows) in 4.0 to 4.9 ms.
  - With upstream's pool policy (at most 32 controls per type), re-keying 1,000 rows in a window gains nothing over
    creating them.
  - Reuse only pays with a pool big enough to hold every row: in a window, about 17% on a full re-key and about 50% on a
    mount. That saving comes from the Button templates a reused control keeps. It costs a reset for each control type,
    keeps every unmounted control alive, and doesn't help small edits.
- **Keyed moves: use Avalonia's `Controls.Move`.** This is the change that pays.
  - Upstream moves a child with `RemoveAt` then `Insert`, which on Avalonia takes the row out of the visual and
    logical trees and puts it back. Reversing 1,000 keyed rows in a window costs 193 to 212 ms.
  - With `Controls.Move` it costs **7.3 to 10.7 ms** through the engine, about 20 times less. No row leaves the tree,
    and the focused control of a moved row keeps focus. With upstream's `RemoveAt` and `Insert` it lost focus in 7 of
    7 runs.
  - **No transform rule is needed.** Upstream's `PanelChildCollection` is built in exactly one place, our tier 3 seam
    (`Seam/Reconciler.V1.cs`), so the seam can pass its own `IChildCollection` and no vendored file changes.
    - The change is `spikes/I-pooling/move-collection.patch`: one new 50-line class and one changed line.
    - All 811 library tests pass with it applied.
    - Porting it, with tests, is a follow-up.

## Method

`spikes/I-pooling/Measure/Program.cs`, a console app. Rows are shaped like Spike G's: a memoized function component with
`UseState`, rendering a horizontal StackPanel of a TextBlock and a Button. The reconciler wraps each component in a
Border, so a row is 4 controls.

- **Runs.** First an unprinted pass of every case, so the JIT has tiered up. Then each case runs 7 times after its own
  warm-up, on a fresh tree. Before each timed part the dispatcher is pumped, a frame rendered and a full GC run. The
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
  - in a window, the expected distinct rows are detached: each removed row, and each moved row with `RemoveAt` +
    `Insert`; none with `Controls.Move`;
  - the Button focused before a reorder took focus;
  - after a replayed move, the visual and logical children are in the new order and the rows are arranged top to bottom.

  The program exits non-zero if a check fails. Every run below passed all of them. Kept, detached and focus counts in
  the notes cover the 7 timed runs, not the warm-up.
- **Sections of the program:**
  1. **Engine.** Churn and reorders through the library's reconciler and upstream's ChildReconciler.
  2. **Fresh against reused controls, at the Avalonia layer.** The same 4 controls per row are created fresh, or rented
     from a per-type pool of used controls.
     - Returning a control takes it apart and `ClearValue`s what was set, as upstream's `ElementPool.CleanElement`
       does. A pooled Button keeps its click handler, as upstream's pool does.
     - The pool comes in two sizes: unbounded, and capped at upstream's 32 per type. Its stacks are sized up front, as a
       pool in use would be.
     - This models what a pool behind `RentControl` could save, without changing the library. The engine's own work
       is the same with or without a pool.
  3. **Moves.** The engine's moves for each reorder are recorded from the panel's collection events. For these
     reorders the engine only moves: 999 moves for a reverse, and 941 for the seeded shuffle (418 of them to a later
     index).
     - They are replayed on built rows, as `RemoveAt` + `Insert` (vendored `PanelChildCollection.Move`) and as
       `Controls.Move`.
     - Two checks of `Controls.Move`'s contract, in both directions:
       - 200 seeded moves on 20 rows in a window, compared after each move with `RemoveAt` + `Insert` on a list;
       - a FlexPanel reversed with moves to earlier indices and back with moves to later ones.
  4. **The engine with Move.** `git apply spikes/I-pooling/move-collection.patch`, rebuild, run section 1 again, then
     `git apply -R` to undo it. The patch was never committed to the library.
- **What the tables show:** the lowest and highest median over 3 invocations of
  `dotnet run -c Release --project spikes/I-pooling/Measure` (1,000 rows, 7 runs), in ms of the change plus layout and
  posted jobs, with the change alone in its own column.
  - Sections 1 to 3 come from one set of 3 invocations, and section 4 from a set run straight after with the patch.
  - The machine was slower for these sets than for earlier sets the same day (mount, detached: 7.3 to 7.5 ms, against
    4.3 to 4.8 ms earlier), so compare within a table, not with other reports.
- **A floor of 6.4 to 9.2 ms** shows in every window case that changes the tree, even a one-row move. It is the layout
  pass and the jobs a change posts, whatever its size [J]. A re-render that changes nothing doesn't pay it.

## Results

### 1. Engine: churn and reorders (1,000 rows)

| Case | Detached | Window | Window, change alone | Allocated (window) | Notes (window) |
|---|---|---|---|---|---|
| mount | 7.3 to 7.5 | 231 to 261 | 39 to 48 | 69,821 KB |  |
| re-render, same keys | 0.26 to 0.35 | 0.41 to 0.55 | 0.41 to 0.55 | 227 KB | 1,000 kept; 0 detached |
| re-key all (1,000 unmounts + 1,000 mounts) | 18.7 to 23.3 | 249 to 273 | 91 to 105 | 81,522 KB | 0 kept; 1,000 detached |
| re-key every other | 11.3 to 14.8 | 106 to 118 | 53 to 61 | 41,403 KB | 500 kept; 500 detached |
| unmount all | 10.0 to 14.2 | 61 to 64 | 43 to 47 | 12,802 KB | 0 kept; 1,000 detached |
| mount again after unmount all | 6.9 to 8.9 | 215 to 231 | 34 to 38 | 69,594 KB |  |
| reverse | 24.6 to 26.0 | 193 to 212 | 108 to 116 | 49,405 KB | 1,000 kept; 999 detached; focus lost 7 of 7 |
| shuffle (seeded) | 20.1 to 26.5 | 182 to 196 | 89 to 108 | 46,555 KB | 1,000 kept; 941 detached; focus lost 7 of 7 |
| move last to first | 0.32 to 0.55 | 6.7 to 9.0 | 0.67 to 0.79 | 422 KB | 1,000 kept; 1 detached; focus lost 7 of 7 |
| move first to last | 0.26 to 0.44 | 7.9 to 9.2 | 0.70 to 0.82 | 422 KB | 1,000 kept; 1 detached; focus lost 7 of 7 |

"Focus lost" means that a Button in a row the engine moves was focused before the change and wasn't afterwards.

### 2. Fresh against reused controls (1,000 rows, 4,000 controls)

| Case | Detached | Window | Window, change alone | Allocated (window) |
|---|---|---|---|---|
| construct only (no tree) | 4.0 to 4.9 |  |  | 9,217 KB (detached) |
| mount: create | 4.5 to 6.0 | 229 to 245 | 37 to 39 | 67,513 KB, 5 gen0 |
| mount: rent from a full pool | 3.7 to 4.4 | **112 to 122** | 43 to 48 | 36,312 KB, 2 gen0 |
| re-key all: create | 17.5 to 17.9 | 234 to 250 | 86 to 92 | 78,790 KB, 4 gen0 |
| re-key all: pool, no cap | 19.8 to 20.2 | **191 to 211** | 82 to 98 | 51,077 KB, 3 gen0 |
| re-key all: pool, cap 32 (upstream's) | 19.1 to 21.4 | 248 to 262 | 95 to 106 | 81,620 to 81,621 KB, 4 gen0 |
| unmount all: drop | 9.0 to 13.2 | 55 to 65 | 41 to 48 | 12,690 KB, 0 gen0 |
| unmount all: reset and pool | 12.1 to 16.5 | 66 to 73 | 52 to 56 | 16,526 KB, 0 gen0 |

### 3. Moves: the engine's moves replayed (1,000 rows)

| Reorder | RemoveAt + Insert, detached | Controls.Move, detached | RemoveAt + Insert, window | Controls.Move, window |
|---|---|---|---|---|
| reverse (999 moves) | 22.6 to 25.8 | 0.84 to 1.11 | 194 to 205 (change 104 to 110; 49,180 KB) | **8.7 to 8.8** (change 0.88 to 1.21; 486 KB) |
| shuffle, seeded (941 moves, 418 to a later index) | 21.4 to 23.1 | 0.67 to 0.93 | 172 to 191 (change 93 to 104; 46,330 KB) | **9.1 to 9.8** (change 0.98 to 1.01; 467 KB) |
| move last to first (1 move) | 0.06 to 0.07 | 0.01 | 8.3 to 8.6 (change 0.28 to 0.29; 196 KB) | 7.0 to 8.4 (change 0.02; 144 KB) |
| move first to last (1 move, to a later index) | 0.06 to 0.07 | 0.00 to 0.01 | 6.8 to 8.6 (change 0.29 to 0.33; 196 KB) | 6.4 to 8.0 (change 0.02 to 0.03; 144 KB) |

- In a window, `RemoveAt` + `Insert` detached every moved row from the visual tree (999, 941 and 1), and a focused
  Button in a moved row lost focus in 7 of 7 runs. `Controls.Move` detached none and kept focus in 7 of 7.
- The contract checks passed: the 200 seeded moves (95 to a later index, 95 to an earlier one) and the FlexPanel
  reversed both ways.

### 4. The engine with Controls.Move (move-collection.patch applied)

| Case | Detached | Window | Window, change alone | Allocated (window) | Notes (window) |
|---|---|---|---|---|---|
| reverse | **1.5 to 2.0** (was 24.6 to 26.0) | **7.3 to 10.7** (was 193 to 212) | 1.6 to 2.3 (was 108 to 116) | 711 KB (was 49,405 KB) | 1,000 kept; 0 detached; focus kept 7 of 7 |
| shuffle (seeded) | **1.7 to 2.5** (was 20.1 to 26.5) | **9.0 to 10.5** (was 182 to 196) | 2.3 to 2.7 (was 89 to 108) | 692 KB (was 46,555 KB) | 1,000 kept; 0 detached; focus kept 7 of 7 |
| move last to first | 0.23 to 0.29 (was 0.32 to 0.55) | 6.4 to 7.3 (was 6.7 to 9.0) | 0.36 to 0.46 (was 0.67 to 0.79) | 371 KB (was 422 KB) | 1,000 kept; 0 detached; focus kept 7 of 7 |
| move first to last | 0.33 to 0.39 (was 0.26 to 0.44) | 6.8 to 8.7 (was 7.9 to 9.2) | 0.41 to 0.55 (was 0.70 to 0.82) | 371 KB (was 422 KB) | 1,000 kept; 0 detached; focus kept 7 of 7 |

- The cases that don't move rows (mount, re-render, re-key, unmount) make no `Move` call. They ran 0 to 23% faster in
  this set than in section 1's, the same drift as between earlier sets, not an effect of the patch [J].
- With the patch applied, `dotnet test tests/Isotope.UI.Avalonia.Tests` passed 811 of 811.

## Findings

1. **Creating a control is not where the time goes.**
   - Constructing 4,000 controls and setting their values takes 4.0 to 4.9 ms.
   - Re-keying 1,000 rows in a window takes 249 to 273 ms through the engine, and 234 to 250 ms when the same controls
     are built directly. So nearly all of it is Avalonia's: attaching, styling, applying templates, layout and the
     `Loaded`/`Unloaded` jobs. The engine adds little.
2. **What reuse saves on Avalonia is the template.**
   - A reused Button keeps the Fluent template it built on its first measure. For a mount, the layout and posted jobs
     fall from about 190 to 205 ms to about 70 ms.
   - Attaching costs a little more (change 37 to 39 ms, against 43 to 48 ms reused), because the reset values are
     restyled.
   - Net, with an unbounded pool:
     - a mount takes 112 to 122 ms against 229 to 245 ms, with allocations 46% lower;
     - a full re-key takes 191 to 211 ms against 234 to 250 ms, with allocations 35% lower.
   - Detached there are no templates, so reuse gains nothing.
3. **Upstream's policy doesn't capture that saving.**
   - `ElementPool` keeps at most 32 controls of each type, so a re-key of 1,000 rows reuses 32 of each.
   - Measured: 248 to 262 ms against 234 to 250 ms, no gain.
   - To get the saving, the pool would have to keep every unmounted control alive: 4,000 controls for this list.
4. **Returning a control costs time too:** resetting and pooling adds about 3 ms (detached) and 8 to 11 ms (window) to
   unmounting 1,000 rows.
5. **A keyed move on Avalonia detaches the row, and that is the cost of a reorder.**
   - Through the engine, a reverse costs 193 to 212 ms in a window. Replaying just its 999 `RemoveAt` + `Insert`
     pairs costs 194 to 205 ms.
   - Each pair takes the row (and its subtree) out of the logical and visual trees and attaches it again, so it is
     restyled, re-measured and gets `Unloaded` and `Loaded` again. Allocations: 49 MB.
   - It also drops focus: a moved row's focused control loses it.
6. **`Controls.Move` keeps the row attached.**
   - Avalonia's `Panel` handles the collection's Move by moving its logical and visual children, in both directions.
     The checks confirm the visual and logical order and the arranged positions after each replay and each of the 200
     seeded moves.
   - The engine's reverse in a window falls to 7.3 to 10.7 ms, about 20 times less, and most of that is the floor of
     any change. The change itself falls from 108 to 116 ms to 1.6 to 2.3 ms, allocations fall to 1.4%, and focus is
     kept.
   - `AvaloniaList.Move(old, new)` has the contract of upstream's `Move`: `newIndex` is the final position. The replay
     checks the final order against the engine's on every run, and the 811 tests pass with the patch.
7. **How the move would ship: a seam class, not a transform rule.**
   - The vendored `Core/ChildCollection.cs` declares `PanelChildCollection`. The only code that constructs it is
     `Seam/Reconciler.V1.cs` (`ReconcilePanelChildrenInto`), and no vendored test names it.
   - A tier 3 `IChildCollection` in `Seam/` keeps upstream's file verbatim and adds no rule to watch across syncs. That
     is the patch's `MovingPanelChildCollection`, which keeps upstream's range asserts and edit counters.
   - A transform rule would also work: replace the `RemoveAt`/`Insert` body of `PanelChildCollection.Move` with
     `_children.Move(oldIndex, newIndex);`. It would put a tier 2 rule on a file upstream changes, for no gain over the
     seam class.
8. **Measuring traps hit here:**
   - **JIT warm-up.** In an earlier set, the first case timed in a fresh process ran about 5 times slower than the same
     work later, at the same allocations (mount, detached: 21.5 ms first, against 4.3 to 4.5 ms after an unprinted pass
     of every case). Spike G's mount figure (37.5 to 40.2 ms), timed first in its process, may carry the same effect
     [J].
   - **The dispatcher queue.** A headless benchmark that runs inside one dispatcher job never pumps the queue, and closed
     windows piled up: the process reached 8.8 GB before it was stopped. The program now runs jobs and renders a frame
     between runs, and prints the managed heap at the end (7.1 MB).
   - **No direct comparison with #2.** #2's baseline (reverse 47.6 to 78.0 ms, shuffle 19.6 to 23.7 ms, detached) came
     from a program with no GC between runs and no suite-wide warm-up. Detached, this program measures reverse at 24.6
     to 26.0 ms and shuffle at 20.1 to 26.5 ms.

## Recommendation

- **Close #14 without porting pooling.** If a virtualized list arrives later, recycling belongs in it (reusing
  containers that already have their templates), not in `RentControl` [J].
- **Port the move:** a follow-up issue for `MovingPanelChildCollection` in `Seam/`. Its tests:
  - a keyed reorder in a window detaches no row (`DetachedFromVisualTree` count);
  - a focused control in a moved row keeps focus;
  - a move to a later index and one to an earlier index, with the visual and logical order checked;
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
- **One machine,** 3 invocations of 7 runs. How much a case's median varied between the invocations reported here:
  - window cases over 50 ms: up to 22% (re-key every other, with the patch: 86 to 105 ms);
  - window cases near the floor: up to 47% (reverse, with the patch: 7.3 to 10.7 ms);
  - detached cases, all under 30 ms: up to 83% (re-render, same keys, with the patch: 0.18 to 0.33 ms).

  Differences inside those margins are not findings.
- **WinUI wasn't measured.** Whether upstream's `RemoveAt` + `Insert` drops focus on WinUI isn't verified. Spike G's
  review said WinUI defers `Unloaded` within a tick.

## Reproduce

```sh
dotnet run -c Release --project spikes/I-pooling/Measure            # [-- <rows> <runs>], default 1000 7; exit 0 = all checks passed
git apply spikes/I-pooling/move-collection.patch                    # the engine with Controls.Move
dotnet run -c Release --project spikes/I-pooling/Measure
dotnet test tests/Isotope.UI.Avalonia.Tests                         # 811 with the patch applied
git apply -R spikes/I-pooling/move-collection.patch
```
