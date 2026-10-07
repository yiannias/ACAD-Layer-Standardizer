# Phase 3 manual checklist: live sync and close protection

For Chris. Plain steps, one scenario per section. Tick the box when the result matches. If it does not, write what you saw in the results table at the end.

Words used here: **the mapping window** (opened with LSTDR), **Source** (left side), **Target** (right side), **the footer** (the line along the bottom of the mapping window).

## Setup (do once)

- [ ] 1. Back up `%APPDATA%\AcLayerStandardizer` (your usual zip).
- [ ] 2. Close AutoCAD.
- [ ] 3. Run the installer `D:\Projects\ACAD-Layer-Standardizer\dist\AcLayerStandardizer_BETA-1.2.4.exe`.
- [ ] 4. Start AutoCAD 2027.
- [ ] 5. Open the two test drawings: the LayerTest_A and LayerTest_B copies on your Desktop.
- [ ] 6. In each drawing, add a few different layers (for example A gets `A-WALL-TEST`, `A-DOOR-TEST`; B gets `B-FURN-TEST`, `B-LITE-TEST`). Also add one layer with the **same name** in both (for example `SHARED-TEST`).
- [ ] 7. Save both drawings (QSAVE).

Commands you will type: **LSTDR** opens the mapping window. **CLOSE** closes the current drawing. **QUIT** exits AutoCAD.

Do the sections in order. Between sections, close the mapping window and restart it with LSTDR if it looks confused, and say so in the notes.

---

## A. Switching drawings

1. Make drawing A the active drawing. Type LSTDR.
2. Note what the Source side shows (A's layers) and what the Target side shows.
3. Click the tab for drawing B (or use Ctrl+Tab) so B is active. Watch the mapping window.

- [ ] Within about 1 second the Source side shows B's layers, including `B-FURN-TEST`.
- [ ] The Target side is exactly as it was.
4. Switch back to A.
- [ ] Source shows A's layers again, within about a second.

Leave the mapping window open for the next section.

## B. Layer changes in the Layer Properties palette

Keep drawing A active and the mapping window open. Open the Layer Properties palette (LA). First, in the mapping window, connect one Source layer to a Target layer (for example `A-WALL-TEST`) and another Source layer (for example `A-DOOR-TEST`).

1. In the palette, add a new layer called `NEW-ADDED-TEST`.
- [ ] Within about a second it appears on the Source side. The status line says the list was refreshed.
2. Rename `NEW-ADDED-TEST` to `NEW-RENAMED-TEST`.
- [ ] The Source side shows the new name and the old name is gone.
3. Rename `A-WALL-TEST` (the one you connected) to `A-WALL-RENAMED`.
- [ ] The connection from `A-WALL-TEST` disappears (the renamed layer starts fresh).
4. Delete `A-DOOR-TEST` (the other connected one). The palette will not delete a layer that is in use or is the current layer; if so, make a different layer current first.
- [ ] `A-DOOR-TEST` disappears from the Source side and its connection is gone.
- [ ] Connections you did NOT touch are still there.

## C. Connections survive switching, and the footer note

1. Drawing A active, mapping window open. Make two connections by hand.
2. Switch to drawing B.
- [ ] The footer shows "LayerTest_A.dwg has 2 unapplied connections" (name and number match what you did).
3. In B, make one connection by hand. Switch back to A.
- [ ] A's two connections are still there (A -> B -> A kept them).
- [ ] The footer now mentions B's connections, not A's.
4. If you can open a third drawing (any test drawing), make a connection in it too, then switch to a drawing that has none.
- [ ] The footer says "2 other drawings have unapplied connections" (or the right number).
5. Apply or discard the connections in each drawing (use the Discard choice in section F, or Apply in each drawing; Apply closes the window, so reopen it with LSTDR).
- [ ] When no other drawing has pending connections, the footer note is gone.

## D. No drawing open

1. Open the mapping window. Close every drawing (CLOSE, answer any save prompts) while the mapping window stays open.
- [ ] The Source side says "No drawing is open".
- [ ] The Apply button is greyed out.
2. Open a drawing again (File > Open, either test drawing).
- [ ] Within about a second the Source side shows that drawing's layers and Apply works again.

## E. Same filename in two folders

Set up: make folders `LayerTest_A` and `LayerTest_B`, each with a copy named `Beds_in_plan.dwg`, and give them different layers (add one layer to only one of them). Open both.

1. Open the mapping window with `LayerTest_A\Beds_in_plan.dwg` active. Make a connection.
2. Switch to `LayerTest_B\Beds_in_plan.dwg`.
- [ ] The Source side shows B's layers (not A's), even though the file name is the same.
- [ ] Reading B's layers works while A is also open (no error message).
3. Switch back to A.
- [ ] The connection you made is still there. It did not move to B.
4. Press Apply while A is shown and A is active.
- [ ] The change happens in A (check the layer list in A). B is unchanged.
5. Reopen the window (LSTDR) on A. Switch the active drawing to B, then quickly click Apply in a window that still shows A (if the window already followed to B, skip this step and write "could not test").
- [ ] A plain message appears (not a technical error) and nothing is changed in either drawing.

## F. Close a drawing that has unapplied connections

Do this part three times, once for each choice. Each time: drawing A active, mapping window open, make 2 connections by hand, then type CLOSE.

1. **Cancel.** In the dialog choose Cancel.
- [ ] The dialog shows three buttons: Apply, Discard, Cancel.
- [ ] After Cancel the drawing stays open and the connections are still there.
2. **Discard.** Type CLOSE again, choose Discard.
- [ ] The connections are dropped and the drawing closes (or its save prompt appears) **promptly, without you pressing any key**. If nothing happens until you press a key, mark FAIL and write "close waits for a keypress".
3. Reopen the drawing, make 2 connections, type CLOSE, choose **Apply**.
- [ ] The connections are applied, then the drawing closes (or the AutoCAD save prompt appears) promptly, without a keypress. Same failure note if it waits for a key.
4. **Minimized window.** Reopen a drawing, make 2 connections, then minimize the mapping window. Type CLOSE.
- [ ] Within about a second the mapping window comes to the front by itself and shows the dialog.
- [ ] A minimized window does NOT come to the front at any other time (for example while you just switch drawings with no pending connections).

## G. Close the mapping window itself with unapplied connections

1. Drawing A active, mapping window open, make 2 connections. Also leave pending connections in drawing B (switch to B, make 1, switch back to A).
2. Close the mapping window with its X button.
- [ ] The Apply / Discard / Cancel dialog appears.
- [ ] The text mentions that drawing B would also lose its connections.
- [ ] The Apply choice says it also closes the window (where that applies).
3. Choose Cancel.
- [ ] The window stays open, connections intact.
4. Click X again and choose Discard.
- [ ] The window closes. Reopen it with LSTDR: A's connections are gone.
5. Repeat: make connections, click X, choose Apply.
- [ ] The connections are applied and the window closes.

## H. Quit AutoCAD with unapplied connections

This tests whether AutoCAD's quit is detected. Keep both test drawings open.

1. Open the mapping window. Make 2 connections in drawing A.
2. Type **QUIT**.
- [ ] AutoCAD does NOT close. The mapping window shows the dialog.
- [ ] The dialog is about quitting AutoCAD. **If the dialog instead says "closing a drawing", and AutoCAD only closes that one drawing (or the QUIT is lost), quit detection did not work.** Mark FAIL and write which happened.
3. Choose Cancel.
- [ ] Nothing closes, connections stay.
4. Type QUIT again.
- [ ] Blocked again with the dialog (this is expected).
5. Choose Discard.
- [ ] QUIT runs again by itself and AutoCAD starts closing (save prompts as usual). Say "no" to saves if you want. If AutoCAD stays open until you type QUIT yourself, mark FAIL and write "QUIT not replayed".
6. Restart AutoCAD, open both drawings, open the mapping window, make connections, then try the other ways to quit: the AutoCAD application X button (top right) and the application menu > Exit.
- [ ] Same dialog each time. (If one of them is not blocked, write which.)
7. Choose Apply this time.
- [ ] The connections are applied, then QUIT re-runs and AutoCAD closes normally (with its normal save prompts).

## I. Never trapped

1. Close the mapping window completely (no unapplied connections). With both drawings open, type CLOSE on one, then QUIT.
- [ ] No dialog from the Standardizer. CLOSE and QUIT behave as normal AutoCAD (save prompts only).
2. Restart AutoCAD, open drawing A, open the mapping window, make a connection, press **Apply** (this closes the window). Within 5 seconds type CLOSE.
- [ ] The drawing close is NOT blocked. If it is blocked for a few seconds with no dialog, mark FAIL and write "blocked after Apply".
3. Reopen the mapping window, make no connections, leave it open and untouched. Type CLOSE, then (after reopening the drawing) QUIT.
- [ ] Neither is blocked. A window with no unapplied connections never blocks anything.

## J. A new standard while another drawing has pending connections

1. Drawing A active, mapping window open. Connect `A-WALL-TEST` (or any layer) to a Target layer that exists only in the current standard. Note the Target layer's name.
2. Switch to drawing B.
3. In the mapping window, load a different standard (Load Standard), one that does **not** contain the Target layer you used in step 1.
4. Switch back to A.
- [ ] The connection to the missing Target layer is not kept (no connection pointing at something that no longer exists).
- [ ] No error appears. Connections to Target layers that still exist are kept.

## K. Phase 2 still works

1. Close the mapping window and AutoCAD. Move the contents of `%APPDATA%\AcLayerStandardizer` aside (your Setup backup covers this) so there is no saved config, start AutoCAD, open a drawing, type LSTDR.
- [ ] The mapping window opens with no config present.
2. Make a connection and choose Apply & Remember.
- [ ] It applies and remembers.
3. Run LSTDR again on a fresh drawing with the same layer.
- [ ] The remembered match is suggested automatically.

---

## Results (paste this table back to Claude)

| Scenario | Pass / Fail | One-line note |
|---|---|---|
| A. Switching drawings | | |
| B. Layer changes | | |
| C. Switching away and back, footer note | | |
| D. No drawing open | | |
| E. Same filename, two folders | | |
| F. Close a drawing (Apply / Discard / Cancel, minimized) | | |
| G. Close the mapping window | | |
| H. Quit AutoCAD | | |
| I. Never trapped | | |
| J. New standard with pending connections | | |
| K. Phase 2 still works | | |

If something fails, also mention these two log files (they are in `%TEMP%`):

- `%TEMP%\AcLayerStandardizer-ipc.log`
- `%TEMP%\AcLayerStandardizer-rust-ipc.log`
