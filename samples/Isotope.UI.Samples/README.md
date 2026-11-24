# The sample app

One window with a tab per area, written as Isotope function components (no XAML). Run it from the repository root:

```sh
dotnet run --project samples/Isotope.UI.Samples
```

The headless tests (`SampleAppTests`) mount every page, click through its main interaction and render it to PNG. A real
window still has to show what they can't: hover tooltips appearing, menus opened with the mouse, right-click menus,
keyboard input, and how the tabs and the split view look and animate. The checklist below is that run.

**Last run:** 2026-11-24, Windows, at `3678918` (#34): every line passed. Linux and macOS haven't been run in a real
window yet. Before a new run, clear the ticks.

## Checklist

Tick each line in a real window. "Shows" means on screen; the line after each arrow is what should happen.

### The window and its menus

- [x] The window opens titled "Isotope samples", with a menu bar (Go, View) above six tabs: Basics, Buttons, Menus,
  Numbers, Split view, Errors. Basics is selected.
- [x] Click **Go** with the mouse → its menu drops down, listing the six pages with a radio mark on Basics.
- [x] Choose **Go > Numbers** → the Numbers tab is selected. Click the **Buttons** tab, then open **Go** again → the
  radio mark is on Buttons.
- [x] Hover over the menu bar with Go open, then over View → the open menu follows the pointer. Esc or a click outside
  closes it.
- [x] Switch between tabs → each page keeps what you did on it (a count, a typed draft, a deleted file).

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

### Errors

- [x] Click **Click the panel** twice → "The panel works: clicked 2 times". Click **Click** under "Outside the
  boundary" → "Clicked 1 times".
- [x] **Throw** → the panel is replaced by "The panel failed: the Throw button was pressed" and **Retry**; "The panel
  has failed once"; the outside counter still says "Clicked 1 times" and still counts.
- [x] **Retry** → the panel is back with "clicked 0 times" (it was mounted again); the outside counter is unchanged.
- [x] The window never shows the host's full error panel (a stack trace) on this page.
