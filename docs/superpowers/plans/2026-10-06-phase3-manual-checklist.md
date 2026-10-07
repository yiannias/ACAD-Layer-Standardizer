# Phase 3 manual checklist: live sync and close protection

For Chris. Plain steps, one scenario per section. Tick the box when the result matches. If it does not, write what you saw in the results table at the end.

Words used here:

- **the mapping window**: the window opened with LSTDR.
- **Source**: the left side of the mapping window (the layers in the drawing).
- **Target**: the right side (the standard's layers).
- **the footer**: the line along the bottom of the mapping window.
- **a connection**: you drag a layer on the left (Source) onto a layer on the right (Target) to say "turn this layer into that one". A connection you made by hand and have not yet applied is an **unapplied connection**.

Commands you will type: **LSTDR** opens the mapping window. **CLOSE** closes the current drawing. **QUIT** exits AutoCAD.

Rules for the whole checklist:

- Whenever AutoCAD asks "Save changes?", choose **No**. That keeps your test drawings in their original state, so every section can start fresh.
- Layers you add, rename or delete in one section are gone again after you answer No to a save prompt. In later sections, where this checklist says "any layer", just pick any layer that is in the drawing.
- Do the sections in order. Each section says what state it starts in.

## Setup (do once)

This checklist only ever uses throwaway test copies: `LayerTest_A\Beds_in_plan.dwg` and `LayerTest_B\Beds_in_plan.dwg` on your Desktop (called **drawing A** and **drawing B** below). You may edit and save them freely. Your original drawings (for example in `D:\CAD WAREHOUSE`) are never opened by this checklist.

- [ ] 1. Back up `%APPDATA%\AcLayerStandardizer` (your usual zip).
- [ ] 2. Safety copy, because later steps (Choose Standard in J, Apply & Remember in K) can change your saved Standardizer files. In File Explorer type `%APPDATA%` in the address bar. If the folder `AcLayerStandardizer` exists, copy it to your Desktop and rename the copy `AcLayerStandardizer_aside`. If it does not exist yet, write "none" here: ______ and skip the restore in section K.
- [ ] 3. Open `%APPDATA%\AcLayerStandardizer\config.json` in Notepad (if it exists) and look for a line containing `MemoryFilePath`. If it has a value pointing **outside** `%APPDATA%\AcLayerStandardizer`, write "memory elsewhere" here: ______ and skip step 5 of section K (tell Claude instead). Do not change the file.
- [ ] 4. Close AutoCAD.
- [ ] 5. Run the installer `D:\Projects\ACAD-Layer-Standardizer\dist\AcLayerStandardizer_BETA-1.2.4.exe`.
- [ ] 6. Start AutoCAD 2027.
- [ ] 7. Open the two test drawings: `LayerTest_A\Beds_in_plan.dwg` and `LayerTest_B\Beds_in_plan.dwg` on your Desktop.
- [ ] 8. In drawing A add the layers `A-WALL-TEST`, `A-DOOR-TEST`, `A-SPARE-1` and `A-SPARE-2`. In drawing B add `B-FURN-TEST` and `B-LITE-TEST`. Also add a layer named `SHARED-TEST` to **both** drawings (same name in each).
- [ ] 9. Save both drawings (QSAVE).

---

## A. Switching drawings

Start state: A and B open, mapping window closed.

1. Make drawing A the active drawing. Type LSTDR.
2. Note what the Source side shows (A's layers) and what the Target side shows.
3. Click the tab for drawing B (or press Ctrl+Tab) so B is active. Watch the mapping window.

- [ ] Within about 1 second the Source side shows B's layers, including `B-FURN-TEST`.
- [ ] The Target side is exactly as it was.

4. Switch back to A.

- [ ] Within about a second the Source side shows A's layers again.

Leave the mapping window open for the next section.

## B. Layer changes in the Layer Properties palette

Start state: A active, mapping window open. Open the Layer Properties palette (type LA). First, in the mapping window, make two connections: `A-WALL-TEST` to any Target layer, and `A-DOOR-TEST` to any Target layer. Then:

1. In the palette, add a new layer called `NEW-ADDED-TEST`.

- [ ] Within about a second it appears on the Source side.
- [ ] The status line shows some wording about the list being refreshed. Write down the exact words you see (it may not match this text).

2. Rename `NEW-ADDED-TEST` to `NEW-RENAMED-TEST`.

- [ ] The Source side shows the new name and the old name is gone.

3. Rename `A-WALL-TEST` (a connected one) to `A-WALL-RENAMED`.

- [ ] The connection from `A-WALL-TEST` disappears (the renamed layer starts with no connection).

4. Delete `A-DOOR-TEST` (the other connected one). The palette will not delete the current layer or one that is in use; if it refuses, make a different layer current first.

- [ ] `A-DOOR-TEST` disappears from the Source side and its connection is gone.

5. If you had any other connections on layers you did not touch:

- [ ] They are still there.

Leave the mapping window open for the next section. Drawing A may still have an unapplied connection or two from this section; that is fine and section C accounts for it.

## C. Connections survive switching, and the footer note

Start state: A active, mapping window open.

1. Look at drawing A's connections now. Remove any you do not want, then make sure A has exactly 2 connections (make more by hand if needed). Write down the number: it should be 2.
2. Switch to drawing B.

- [ ] The footer shows a note that drawing A has 2 unapplied connections (the drawing name and number match).

3. In B, make exactly **1** connection. Switch back to A.

- [ ] A's 2 connections are still there (A to B to A kept them).
- [ ] The footer now mentions B's connection, not A's. **Write down the exact footer text you see for the single connection** (the wording for 1 may differ from the wording for 2).

4. Open a third drawing (File > Open, any other drawing; a new blank drawing also works). In the mapping window make 1 connection there. Switch to drawing A.

- [ ] The footer says "2 other drawings have unapplied connections" (or the right number for what you did).

5. Clear everything. Close the mapping window with its X button. The Apply / Discard / Cancel dialog appears: choose **Discard**. Then type LSTDR to reopen the window.

- [ ] The footer note is gone (no other drawing has unapplied connections).

6. Close the third drawing (CLOSE, choose No if asked to save).

Leave the mapping window open.

## D. No drawing open

Start state: mapping window open, A and B open.

1. First make sure no unapplied connections remain: close the mapping window with X, choose **Discard** if the dialog appears, then type LSTDR to reopen it.
2. Close every drawing (type CLOSE in each; answer No to save prompts) while the mapping window stays open.

- [ ] The Source side says "No drawing is open".
- [ ] The Apply button is greyed out.

3. Open drawing A again (File > Open).

- [ ] Within about a second the Source side shows A's layers and Apply is available again.

4. Also open drawing B so both are open again. Leave the mapping window open.

## E. Same filename in two folders

Start state: mapping window open.

Set up: on the Desktop make two new folders named `SameName_1` and `SameName_2`. Copy the file `LayerTest_A\Beds_in_plan.dwg` into `SameName_1` and the file `LayerTest_B\Beds_in_plan.dwg` into `SameName_2` (both copies keep the name `Beds_in_plan.dwg`). They now have the same file name but different layers. This section uses ONLY these two new copies.

1. Close the mapping window with X (Discard if asked). Open both `Beds_in_plan.dwg` files in AutoCAD. Make `SameName_1\Beds_in_plan.dwg` active and type LSTDR. Make a connection.
2. Switch to `SameName_2\Beds_in_plan.dwg`.

- [ ] The Source side shows that drawing's layers (including `B-FURN-TEST`), not the other one's, even though the file name is the same.
- [ ] No error message appears even though two drawings are open.

3. Switch back to the `SameName_1` drawing.

- [ ] The connection you made is still there. It did not move to the other drawing.

4. Click Apply while the `SameName_1` drawing is shown and active.

- [ ] The change happens in the `SameName_1` drawing (check its layer list). The `SameName_2` drawing is unchanged.

5. Refusal test. Type LSTDR on the `SameName_1` drawing (the window closed after Apply) and make a connection. Now **minimize** the mapping window. Switch to the `SameName_2` drawing. Restore the mapping window and **click Apply immediately** (the window may refresh to the new drawing within a second, so be quick).

- [ ] If a plain message appears saying the window's drawing is not the active one, and nothing changed in either drawing: PASS.
- [ ] If the window had already refreshed to `SameName_2` (the active drawing), there is nothing to apply there, so Apply is greyed out or does nothing: also acceptable; write "window refreshed first".
- [ ] FAIL if Apply changed the `SameName_1` drawing while `SameName_2` was active, or if you saw a technical error message.

6. Close the two `Beds_in_plan.dwg` drawings with CLOSE. The `SameName_1` drawing still has an unapplied connection, so the Apply / Discard / Cancel dialog appears: choose **Discard**. Answer **No** to any save prompt. Then make sure drawing A and drawing B are open (open them again if needed) and the mapping window is closed.

## F. Close a drawing that has unapplied connections

Start state: A and B open, mapping window closed. Type LSTDR on drawing A.

1. **Cancel.** Make 2 connections by hand in A, then type CLOSE.

- [ ] A dialog with three buttons appears: Apply, Discard, Cancel.

Choose **Cancel**.

- [ ] The drawing stays open and the 2 connections are still there.

2. **Discard.** Type CLOSE again and choose **Discard**.

- [ ] The connections are dropped and the drawing closes (or AutoCAD's save prompt appears) promptly, **without you pressing any key**. If nothing happens until you press a key, mark FAIL and write "close waits for a keypress". (If a save prompt appears, choose No.)

3. **Apply.** Reopen drawing A (File > Open). If the mapping window is not open, type LSTDR. Make 2 connections, type CLOSE and choose **Apply**.

- [ ] The connections are applied, then the drawing closes (or the save prompt appears) promptly, without a keypress. Same failure note if it waits for a key. (Choose No if a save prompt appears.)

4. **Minimized window.** Reopen drawing A, type LSTDR (the window closed after Apply), make 2 connections, then **minimize** the mapping window. Type CLOSE.

- [ ] Within about a second the mapping window comes to the front by itself and shows the dialog.
- [ ] Choose Cancel. Then minimize the window again and switch between drawings A and B after first getting rid of the pending connections (click each connection to remove it by hand, or close the mapping window with X, choose Discard, and reopen it with LSTDR): the window does NOT come to the front by itself at any other time.

5. Make sure the mapping window is closed (X, Discard if asked) and A and B are both open.

## G. Close the mapping window itself with unapplied connections

Start state: A and B open, mapping window closed.

1. Type LSTDR with A active. Make 2 connections. Switch to B, make 1 connection, switch back to A.
2. Close the mapping window with its X button.

- [ ] The Apply / Discard / Cancel dialog appears.
- [ ] The text mentions that drawing B would also lose its connections.
- [ ] The Apply choice says it also closes the window (where that applies).

3. Choose **Cancel**.

- [ ] The window stays open with the connections intact.

4. Click X again and choose **Discard**.

- [ ] The window closes. Type LSTDR to reopen it: A's connections are gone.

5. Make 2 connections in A again (the window is open from the last step; if not, type LSTDR first). Click X and choose **Apply**.

- [ ] The connections are applied and the window closes.

## H. Quit AutoCAD with unapplied connections

Start state: A and B open, mapping window closed. This tests whether AutoCAD's quit is detected.

1. Type LSTDR with A active. Make 2 connections in A.
2. Type **QUIT**.

- [ ] AutoCAD does NOT close. The mapping window shows the dialog.
- [ ] The dialog is about quitting AutoCAD. **If the dialog instead says "closing a drawing", and AutoCAD only closes that one drawing (or the QUIT is lost), quit detection did not work.** Mark FAIL and write which happened.

3. Choose **Cancel**.

- [ ] Nothing closes and the connections stay.

4. Type QUIT again.

- [ ] Blocked again with the dialog (this is expected). Choose **Cancel** again.

5. Type QUIT once more and choose **Discard**.

- [ ] QUIT runs again by itself and AutoCAD starts closing (choose No at save prompts). If AutoCAD stays open until you type QUIT yourself, mark FAIL and write "QUIT not replayed".

6. Start AutoCAD again and open A and B. Type LSTDR and make connections in A. Now try each other way to leave, **choosing Cancel after each attempt**:
   - the AutoCAD window's X button (top right);
   - the application menu > Exit.

- [ ] Each shows the same dialog and AutoCAD stays open. If one of them is not blocked and AutoCAD closes, restart AutoCAD, open A and B, type LSTDR and make connections again before the next attempt; write which one was not blocked.

7. Now try QUIT once more and choose **Apply**.

- [ ] The connections are applied, then QUIT re-runs and AutoCAD closes normally (with its normal save prompts; choose No).

## I. Never trapped

Start state: AutoCAD closed.

1. Start AutoCAD and open A and B. Do **not** open the mapping window. Type CLOSE on B (No to save), then type QUIT (No to save if asked).

- [ ] No Standardizer dialog appears. CLOSE and QUIT behave like normal AutoCAD (save prompts only).

2. Start AutoCAD, open A, type LSTDR, make a connection and click **Apply** (this closes the window). Within 5 seconds type CLOSE.

- [ ] The drawing close is NOT blocked. If it is blocked for a few seconds with no dialog, mark FAIL and write "blocked after Apply". (Choose No at a save prompt.)

3. Open A and B again and type LSTDR. Make no connections and leave the window open and untouched. Type CLOSE on B.

- [ ] Not blocked.

4. Type QUIT (choose No at save prompts).

- [ ] Not blocked. A window with no unapplied connections never blocks anything. AutoCAD is now closed.

## J. A new standard while another drawing has pending connections

Start state: AutoCAD closed.

1. Start AutoCAD and open A and B. Type LSTDR with A active. Connect any layer in drawing A to a Target layer. Note that Target layer's name.
2. Switch to drawing B.
3. In the mapping window click **Choose Standard** and pick a different standard that does **not** contain the Target layer you used in step 1. If you have no such standard, make one: copy your standard file to a new name on the Desktop, delete that Target layer from the copy, and pick the copy. If you have no second standard and cannot make one, skip J and tell Claude.
4. Switch back to A.

- [ ] The connection to the missing Target layer is not kept (no connection pointing at something that no longer exists).
- [ ] No error appears. Any connections to Target layers that still exist are kept.

5. Close the mapping window with X and choose **Discard** if asked.

## K. Phase 2 still works (and keeping your saved settings safe)

This section changes your saved Standardizer files. The safety copy was made in Setup step 2 (the folder `AcLayerStandardizer_aside` on your Desktop). **Do not skip step 1 or step 8.** If you wrote "memory elsewhere" in Setup step 3, skip steps 5 and 6 (tell Claude instead).

1. Check that `AcLayerStandardizer_aside` still exists on your Desktop (unless you wrote "none" in Setup step 2). If it is missing, STOP and do not continue. Close AutoCAD.
2. Check that the aside copy contains `config.json` and `standards_memory.json` (if the originals existed).
3. In the original `%APPDATA%\AcLayerStandardizer` folder, delete `config.json` and `standards_memory.json` (so there is no saved setup).
4. Start AutoCAD, open drawing A, type LSTDR.

- [ ] The mapping window opens with no saved setup.

5. Connect the layer `SHARED-TEST` to any Target layer and choose **Apply & Remember**.

- [ ] It applies and remembers (no error).

6. Open drawing B and type LSTDR (the window closed after Apply).

- [ ] `SHARED-TEST` is suggested automatically with the same Target (the remembered match).

7. Close the mapping window and quit AutoCAD (No to save prompts).
8. **Restore your saved settings.** In `%APPDATA%`, delete the whole `AcLayerStandardizer` folder (it now holds only test data). Copy `AcLayerStandardizer_aside` from your Desktop into `%APPDATA%` and rename it back to `AcLayerStandardizer`. (If you wrote "none" in Setup step 2, just delete the test-written folder. If anything looks wrong, unzip your Setup backup instead.) Section J's Choose Standard may also have changed your settings; this restore undoes that too.

- [ ] `config.json` and `standards_memory.json` are back in `%APPDATA%\AcLayerStandardizer` (or "none" case: folder removed).

---

## Results (paste this table back to Claude)

| Scenario | Pass / Fail | One-line note |
|---|---|---|
| A. Switching drawings | | |
| B. Layer changes (include the status line wording) | | |
| C. Switching away and back, footer note (include the footer text for 1 connection) | | |
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
