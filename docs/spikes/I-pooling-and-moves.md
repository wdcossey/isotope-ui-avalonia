# Spike I: control pooling and keyed moves on Avalonia (issue #14)

Measured 2026-11-24 on the development machine: Avalonia 12.1.6 headless (no OS window), Skia and the Fluent theme as
the library's tests use them, .NET SDK 11.0.100 targeting `net10.0`, Release. Program: `spikes/I-pooling/Measure`.

## Answer

- **Pooling: don't port it. Close #14 with these numbers.**
  - Creating controls is cheap on Avalonia: 4,000 controls (1,000 rows) in 2.3 to 3.9 ms.
  - With upstream's pool policy (at most 32 controls per type), re-keying 1,000 rows in a window gains nothing
    measurable over creating them.
  - Reuse only pays with a pool big enough to hold every row: 30% on a full re-key and 50% on a mount, in a window. That
    saving comes from Button templates that a reused control keeps. It costs a reset for each control type, keeps every
    unmounted control alive, and doesn't help small edits.
- **Keyed moves: use Avalonia's `Controls.Move`.** This is the change that pays.
  - Upstream moves a child with `RemoveAt` then `Insert`, which on Avalonia takes the row out of the visual and
    logical trees and puts it back. Reversing 1,000 keyed rows in a window costs 112 to 119 ms.
  - With `Controls.Move` it costs **1.8 to 2.0 ms** through the engine. No row leaves the tree, and the focused control
    of a moved row keeps focus (with upstream's `RemoveAt` and `Insert` it lost focus in 8 of 8 runs).
  - **No transform rule is needed.** Upstream's `PanelChildCollection` is built in exactly one place, our tier 3 seam
    (`Seam/Reconciler.V1.cs`), so the seam can pass its own `IChildCollection` and no vendored file changes. The
    change is `spikes/I-pooling/move-collection.patch` (one new 28-line class and one changed line). All 811 library
    tests pass with it applied. Porting it, with tests, is a follow-up.

## Method

`spikes/I-pooling/Measure/Program.cs`, a console app. Rows are shaped like Spike G's: a memoized function component with
`UseState`, rendering a horizontal StackPanel of a TextBlock and a Button. The reconciler wraps each component in a
Border, so a row is 4 controls.

- **Runs.** First an unprinted pass of every case, so the JIT has tiered up. Then each case runs 7 times after its own
  warm-up, on a fresh tree, with the dispatcher pumped, a frame rendered and a full GC before each timed part. The
  program prints medians of:
  - the change alone;
  - the change plus a layout pass (`UpdateLayout`);
  - KB allocated on the UI thread over both (`GC.GetAllocatedBytesForCurrentThread`);
  - gen0 collections.
- **Two hostings for every case.** *Detached* has no window, like Spike G's numbers. *Window* is a shown headless 800×600
  window, where attaching, styling, templates and layout happen. The window numbers are the ones an app pays.
- **Checks on every run:** the rows end up in key order, and rows are kept or detached as expected. After a move, the
  visual and logical children are in the new order and the rows are arranged top to bottom. The program exits non-zero
  if a check fails. Every run below passed all of them.
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
  `dotnet run -c Release --project spikes/I-pooling/Measure` (1,000 rows, 7 runs), in ms of the change plus layout. The
  change alone is in brackets where it matters.

## Results

### 1. Engine: churn and reorders (1,000 rows)

| Case | Detached | Window | Allocated (window) | Notes (window) |
|---|---|---|---|---|
| mount | 4.3 | 94 to 110 (change 28 to 33) | 63,191 KB | |
| re-render, same keys | 0.16 to 0.20 | 0.33 to 0.42 | 227 KB | 1,000 kept |
| re-key all (1,000 unmounts + 1,000 mounts) | 15.8 to 20.6 | 150 to 163 (change 72 to 75) | 75,684 KB | 0 kept |
| re-key every other | 8.6 to 9.6 | 66 to 73 | 39,255 KB | 500 kept |
| unmount all | 8.2 to 9.1 | 34 to 38 | 12,713 KB | |
| mount again after unmount all | 4.1 to 4.3 | 98 to 104 | 62,965 KB | |
| reverse | 15.5 to 18.1 | 112 to 119 | 45,101 KB | 999 rows detached; focus lost 8 of 8 |
| shuffle (seeded) | 17.5 to 19.3 | 109 to 115 | 42,569 KB | 941 rows detached; focus lost 8 of 8 |
| move last to first | 0.20 to 0.22 | 1.07 to 1.15 | 401 KB | 1 row detached; focus lost 8 of 8 |

"Focus lost" means that a Button in a row the engine moves was focused before the change and wasn't afterwards.

### 2. Fresh against reused controls (1,000 rows, 4,000 controls)

| Case | Detached | Window | Allocated (window) |
|---|---|---|---|
| construct only (no tree) | 2.3 to 3.9 | | 9,217 KB (detached) |
| mount: create | 2.8 to 4.4 | 107 to 111 (change 22 to 29) | 60,882 KB, 4 gen0 |
| mount: rent from a full pool | 2.5 | **51 to 55** (change 33 to 35) | 29,681 KB, 1 gen0 |
| re-key all: create | 10.8 to 12.7 | 148 to 157 (change 61 to 67) | 73,021 KB |
| re-key all: pool, no cap | 12.2 to 14.7 | **100 to 109** (change 66 to 71) | 46,607 KB |
| re-key all: pool, cap 32 (upstream's) | 12.6 to 15.0 | 136 to 145 (change 73 to 78) | 75,894 KB |
| unmount all: drop | 7.1 to 9.1 | 32 to 38 | 12,672 KB |
| unmount all: reset and pool | 10.3 to 12.0 | 39 to 42 | 16,573 KB |

### 3. Moves: the engine's moves replayed (1,000 rows)

| Reorder | RemoveAt + Insert, detached | Controls.Move, detached | RemoveAt + Insert, window | Controls.Move, window |
|---|---|---|---|---|
| reverse (999 moves) | 14.7 to 16.8 | 0.57 to 0.61 | 111 to 118 (44,874 KB) | **1.11 to 1.15** (461 KB) |
| shuffle (941 moves) | 17.2 to 21.1 | 0.49 to 0.85 | 103 to 117 (42,342 KB) | **1.07 to 1.19** (442 KB) |
| move last to first (1 move) | 0.04 to 0.05 | 0.00 to 0.01 | 0.75 to 0.80 | 0.45 to 0.53 |

In a window, `RemoveAt` + `Insert` detached every moved row from the visual tree (999, 941 and 1), and a focused
Button in a moved row lost focus in 8 of 8 runs. `Controls.Move` detached none and kept focus in 8 of 8. A 20-child
FlexPanel reversed with `Controls.Move` laid out in the new order.

### 4. The engine with Controls.Move (move-collection.patch applied)

| Case | Detached | Window | Allocated (window) | Notes (window) |
|---|---|---|---|---|
| reverse | **1.34 to 1.46** (was 15.5 to 18.1) | **1.83 to 1.95** (was 112 to 119) | 688 KB (was 45,101) | 0 rows detached; focus kept 8 of 8 |
| shuffle (seeded) | 1.54 to 1.63 (was 17.5 to 19.3) | 2.32 to 2.43 (was 109 to 115) | 668 KB (was 42,569) | 0 rows detached; focus kept 8 of 8 |
| move last to first | 0.18 (was 0.20 to 0.22) | 0.73 to 0.79 (was 1.07 to 1.15) | 353 KB (was 401) | 0 rows detached; focus kept 8 of 8 |

The cases that don't move rows (mount, re-render, re-key, unmount) stayed within their ranges in section 1. With the
patch applied, `dotnet test tests/Isotope.UI.Avalonia.Tests` passed 811 of 811.

## Findings

1. **Creating a control is not where the time goes.**
   - Constructing 4,000 controls and setting their values takes 2.3 to 3.9 ms.
   - Re-keying 1,000 rows in a window takes 150 to 163 ms through the engine, and 148 to 157 ms when the same controls
     are built directly. So nearly all of it is Avalonia's: attaching, styling, applying templates and layout. The engine
     adds little.
2. **What reuse saves on Avalonia is the template.**
   - A reused Button keeps the Fluent template it built on its first measure, so a mount's layout falls from about 82 ms
     to about 19 ms.
   - Attaching costs a little more (change 22 to 29 ms, against 33 to 35 ms reused), because the reset values are
     restyled.
   - Net: 51 to 55 ms against 107 to 111 ms for a mount, and 100 to 109 ms against 148 to 157 ms for a full re-key, with
     allocations about a third lower.
   - Detached there are no templates, so reuse gains nothing.
3. **Upstream's policy doesn't capture that saving.**
   - `ElementPool` keeps at most 32 controls of each type, so a re-key of 1,000 rows reuses 32 of each.
   - Measured: 136 to 145 ms against 148 to 157 ms. That is about 8% faster, more than reusing 3% of the controls can
     explain; the rest is probably run order, since this case runs right after the unbounded pool's [J].
   - To get the 30%, the pool would have to keep every unmounted control alive: 4,000 controls for this list.
4. **Returning a control costs time too:** resetting and pooling adds 3 ms (detached) and 4 to 7 ms (window) to
   unmounting 1,000 rows.
5. **A keyed move on Avalonia detaches the row, and that is the cost of a reorder.**
   - Through the engine, a reverse costs 112 to 119 ms in a window. Replaying just its 999 `RemoveAt` + `Insert`
     pairs costs 111 to 118 ms.
   - Each pair takes the row (and its subtree) out of the logical and visual trees and attaches it again, so it is
     restyled and re-measured. Allocations: 45 MB.
   - It also drops focus: a moved row's focused control loses it.
6. **`Controls.Move` keeps the row attached.**
   - Avalonia's `Panel` handles the collection's Move by moving its logical and visual children. The checks confirm the
     visual and logical order and the arranged positions after each replay.
   - The engine's reverse falls to 1.8 to 2.0 ms in a window, about 60 times faster, with 1.5% of the allocations, and
     focus is kept.
   - `AvaloniaList.Move(old, new)` has the contract of upstream's `Move`: `newIndex` is the final position. The replay
     checks the final order against the engine's on every run, and the 811 tests pass with the patch.
7. **How the move would ship: a seam class, not a transform rule.**
   - The vendored `Core/ChildCollection.cs` declares `PanelChildCollection`, and the only code that constructs it is
     `Seam/Reconciler.V1.cs` (`ReconcilePanelChildrenInto`); no vendored test names it.
   - A tier 3 `IChildCollection` in `Seam/` (the patch's `MovingPanelChildCollection`) keeps upstream's file verbatim
     and adds no rule to watch across syncs.
   - A transform rule would also work: replace the `RemoveAt`/`Insert` body of `PanelChildCollection.Move` with
     `_children.Move(oldIndex, newIndex);`. It would put a tier 2 rule on a file upstream changes, for no gain over the
     seam class.
8. **Measuring traps hit here:**
   - **JIT warm-up.** The first case timed in a fresh process ran about 5 times slower than the same work later, at the
     same allocations (mount, detached: 21.5 ms first, against 4.3 to 4.5 ms after an unprinted pass of every case).
     Spike G's mount figure (37.5 to 40.2 ms), timed first in its process, may carry the same effect [J].
   - **The dispatcher queue.** A headless benchmark that runs inside one dispatcher job never pumps the queue, and closed
     windows piled up: the process reached 8.8 GB before it was stopped. The program now runs jobs and renders a frame
     between runs, and prints the managed heap at the end (5.6 MB).
   - **No direct comparison with #2.** #2's baseline (reverse 47.6 to 78.0 ms, shuffle 19.6 to 23.7 ms, detached) came
     from a program with no GC between runs and no suite-wide warm-up. Detached, this program measures reverse at 15.5
     to 18.1 ms and shuffle at 17.5 to 19.3 ms.

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
- **One row shape** (Border, StackPanel, TextBlock and Button). Controls with heavier templates (TextBox, ComboBox) would
  gain more from reuse, and plain panels less [J].
- **The pool is a model at the Avalonia layer,** not a port behind `RentControl`. It doesn't reset the engine's
  per-control state, which a real port would also have to do (more cost on return).
- **One machine,** 3 invocations of 7 runs. A case's median varied by up to 15% between invocations (shuffle, window,
  RemoveAt + Insert: 103 to 117 ms), so differences under that are not findings.
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
