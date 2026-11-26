# The sample app

One window with a page per area, chosen from a list on the left (#96), written as Isotope function components (no
XAML). Run it from the repository root:

```sh
dotnet run --project samples/Isotope.UI.Samples
```

The headless tests (`SampleAppTests`) mount every page, click through its main interaction and render it to PNG. A real
window still has to show what they can't: hover tooltips appearing, menus opened with the mouse, right-click menus,
keyboard input, and how the page list and the split view look and animate. The checklist below is that run.

**Last run:** 2026-11-26, Windows, at `512633f` (#100): the window and menu lines (the page list, #96), the Lists page
(#94) and the first three Basics lines passed; only those were run at this commit, the rest beside the new list not yet. The Rich text and Icons pages passed earlier that day at `cf10271` (#73, #92), and every other line on
2026-11-24 at `3678918` (#34). Linux and macOS haven't been run in a real window yet. Before a new run, clear the ticks.

## Checklist

Tick each line in a real window. "Shows" means on screen; the line after each arrow is what should happen.

**How to run the checklist:** from the repository root, run
`node tools/real-window/checklist.mjs samples/Isotope.UI.Samples/README.md --clear` and open the page it prints
(`http://127.0.0.1:8765/`; `--port` picks another). Fill in the system (OS, display server, version) once, then click
**Pass**, **Fail** or **Skip** on each line, with a note when there is something to say. Each answer is saved as it is
given, to `README.md.results.json` beside this file (git-ignored), so a closed tab or a stopped tool loses nothing: run
the command again to resume. **Finish** ticks the passed lines and unticks the failed ones, appends each note to its
line (`**Failed (<system>, <date>):** <note>` for a failure, `(<system>, <date>: <note>)` for a pass), leaves skipped
lines as they were, and prints a summary with a suggested "Last run" sentence for the paragraph above, which it
doesn't write. `--clear` unticks every line, as a new run needs; notes from earlier runs stay until removed by hand.

### The window and its menus

- [x] The window opens titled "Isotope samples", with a menu bar (Go, View) above a list of the pages on the left, each
  name on one line and none cut off: Basics, Buttons, Menus, Numbers, Split view, Rich text, Icons, Lists, Errors.
  Basics is selected, and its page fills the rest of the window; no page is cut off on the right.
- [x] Click **Go** with the mouse → its menu drops down, listing the nine pages with a radio mark on Basics.
- [x] Choose **Go > Numbers** → Numbers is selected in the list and its page shows. Click **Buttons** in the list, then
  open **Go** again → the radio mark is on Buttons.
- [x] Hover over the menu bar with Go open, then over View → the open menu follows the pointer. Esc or a click outside
  closes it.
- [x] Switch between pages → each page keeps what you did on it (a count, a typed draft, a deleted file).
- [x] Click a page in the list, then press Down and Up → the selection moves and its page shows. Press Tab → the focus
  moves into the page shown (its first control), never into a hidden page.

### Basics

- [x] Click **Click** three times → "Clicked 3 times".
- [x] Type a todo and click **Add** (or a few) → each row shows "[ ] title", the box empties, "n of n left" counts.
- [x] **Done** on a row → "[x]", its "toggled" count goes up; **Reverse** reorders the rows and each keeps its count;
  **Remove** drops the row.

### Buttons

- [x] Hover each button for about a second → its tooltip appears ("A button whose content is a badge and a label",
  "Stays pressed until clicked again", and so on) and goes when the pointer leaves.
- [x] Choose **View > Show tooltips** (the check mark goes) → hovering shows no tooltips. Choose it again → they're back.
- [x] **Save** shows a blue "S" badge beside its label; a click → "Saved once", then "Saved 2 times".
- [x] **Bold** → stays pressed (accent colour), "Bold: on"; again → released, "Bold: off".
- [x] **Filter** → each click moves "Filter:" on, mixed, off, with the button's look changing for each.
- [x] Hold **+** down → "Volume" keeps rising while held (from about a quarter of a second in), and stops at 100; hold
  **-** → it falls, stopping at 0.
- [x] The link-styled button looks like a link (underlined, link colour); a click → "Link clicked once", and no browser
  opens.

### Menus

- [x] Right-click **notes.txt** → a menu opens at the pointer: Open, Pinned, Label (a submenu), a line, Delete.
- [x] **Open** → "Last command: Opened notes.txt".
- [x] **Pinned** → the row reads "notes.txt (pinned)"; right-click it again → Pinned has a check mark; choose it →
  unpinned.
- [x] Hover **Label** → the submenu opens with none, red, green, blue, a radio mark on none. Choose **green** → the row
  ends in "[green]"; the next time, the mark is on green.
- [x] **Delete** → the row goes and "Last command: Deleted …". **Bring the files back** → all three return.
- [x] Esc, or a click outside, closes an open context menu without choosing anything.

### Numbers

- [x] The box shows 20 with up and down buttons; the slider's thumb is at 20; "Value: 20".
- [x] Click the up button → 25 in the box, the thumb moves, "Value: 25". Up and down arrow keys in the box do the same.
- [x] Type 42 in the box and press Enter → the slider follows, "Value: 42".
- [x] Drag the slider → the box follows, in whole numbers. **Reset** → back to 20.
- [x] Type 150 and press Enter → the box keeps the text 150 but its value doesn't take it: "Value:" and the slider stay
  where they were (Avalonia's NumericUpDown refuses typed text outside its range unless ClipValueToMinMax is on).
- [x] Empty the box and press Enter → the box stays empty; "Value:" and the slider keep the last number.

### Split view

- [x] The pane (grey, with Inbox, Sent, Archive) is open on the left beside the content; **Pane** is pressed.
- [x] Click **Archive** → "Showing Archive".
- [x] Click **Pane** → the pane closes (watch the animation), the button is released, "Pane closed"; again → it opens.
- [x] Pick **Overlay** in the drop-down → the open pane now lies over the content. Click the content → the pane closes
  by itself (a light dismiss) and **Pane** is released: the state followed the control.
- [x] **Compact inline** and **Compact overlay** → a closed pane leaves a narrow strip.

### Rich text

- [x] The first line reads "Rich text is **bold**, *italic*, underlined, struck through and coloured, in one text block.",
  each word styled as it says (underlined underlined, struck through crossed out, coloured green).
- [x] The **Like** button sits in the second line of text, on its baseline; a click → "liked 1 times", and the button is
  still in place.
- [x] Tab to **Like** and press Space → the count goes up. (Avalonia lays the text out again when it changes, so the
  button loses keyboard focus after each press: note what happens.) On Windows, 2026-11-26: focus doesn't stay on the
  button after a press.
- [x] "Red Green Blue", each in its colour and bold. **Rotate the colours** → "Green Blue Red", then "Blue Red Green".
- [x] Drag over the two lines under the button → the text is selected across both lines; Ctrl+C copies it (paste it
  somewhere: two lines). The second line is smaller.

### Icons

- [x] The first row: a black diamond (20 by 20), a steel-blue tick (32) and a triangle in the accent colour (48), each
  crisp at its size. **Change the colour** → the tick turns sea green; again → steel blue.
- [x] The second row: "Aa" in the default font, and on Windows a star outline in steel blue (a glyph of Segoe Fluent
  Icons). On Linux or macOS the star is missing or a box: note which. On Windows, 2026-11-26: the star is centred in
  its box.
- [x] The third row: a steel-blue ring (the bitmap's shape in the foreground), the same ring in its own teal and orange,
  and a bigger teal and orange ring (an image). **Change the colour** turns the first ring sea green. On Windows,
  2026-11-26: the first ring is an even colour, but its edges aren't smooth (#98).
- [x] **Next kind** → the last icon shows a glyph ("Aa"), then the ring as a shape, then the ring in its colours, then the
  tick again, with the line above saying which. The row doesn't flicker or shift.

### Lists

- [x] Four fruit rows (Apple, Banana, Cherry, Damson), each with "counted 0" and a **+1** button. **+1** on Banana →
  "counted 1"; "Last clicked" still says "nothing yet" (the button's click isn't the item's).
- [x] **Reverse**, then **Rotate** → the rows move and Banana keeps "counted 1". **Add** → Elderberry joins at the end.
- [x] Click a row → it is highlighted, "Selected:" and "Last clicked:" name it. Reverse → the same fruit stays selected.
- [x] Pick **Multiple (Ctrl, Shift)** → a plain click selects one row; Ctrl+click adds one, Shift+click a range.
  **Toggle (each tap)** → each click selects or deselects its row.
- [x] Focus a row (click it, then the arrow keys) and press Enter or Space → "Last clicked" names it.
- [x] The thousand rows scroll smoothly to "Row 1000" (Avalonia realises only the rows in view).

### Errors

- [x] Click **Click the panel** twice → "The panel works: clicked 2 times". Click **Click** under "Outside the
  boundary" → "Clicked 1 times".
- [x] **Throw** → the panel is replaced by "The panel failed: the Throw button was pressed" and **Retry**; "The panel
  has failed once"; the outside counter still says "Clicked 1 times" and still counts.
- [x] **Retry** → the panel is back with "clicked 0 times" (it was mounted again); the outside counter is unchanged.
- [x] The window never shows the host's full error panel (a stack trace) on this page.
