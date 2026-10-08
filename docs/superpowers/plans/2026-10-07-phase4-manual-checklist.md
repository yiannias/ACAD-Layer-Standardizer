# Phase 4 manual checklist: the mapping window opens directly (Beta 1.4.0)

For Chris. Plain steps, one scenario per section. Tick the box when the result matches. If it does not, write what you saw in the results table at the end.

What changed in this version: the old editor window is gone. Every way of starting the Standardizer now opens the new mapping window straight away, with no welcome dialog. The window has a new **Settings** panel, and it shows a one-time beta notice.

Words used here:

- **the mapping window**: the window the Standardizer opens (the one with Source on the left and Target on the right).
- **Source**: the left side of the mapping window (the layers in the drawing).
- **Target**: the right side (the standard's layers).
- **the left sidebar**: the column of buttons at the left edge of the mapping window. The **Settings** button is at its bottom.
- **the Settings panel**: the small box that opens over the mapping window when you click Settings. Its X is in its top right corner.
- **the memory file**: the file where the Standardizer remembers the matches you chose (`standards_memory.json`).

Commands you will type: **LSR** and **LSTDR** open the mapping window. **LAYERSTANDARDIZER** and **STD_Mappings** do the same. **CLOSE** closes the current drawing. **QUIT** exits AutoCAD.

Rules for the whole checklist:

- Whenever AutoCAD asks "Save changes?", choose **No**. That keeps your test drawings in their original state.
- Do the sections in order. Each section says what state it starts in.
- When a step says "close the mapping window with X" and a dialog asks Apply / Discard / Cancel, choose **Discard** unless the step says otherwise.

## LAST STEP, ALWAYS: restore (two things)

Several steps change things on your machine: your saved Standardizer files (sections C and D) and, in section G, the name of a program file inside the AutoCAD plug-in folder. **Whenever you stop, whether you finish everything, quit after any section, or hit a STOP, do both restores before you finish.**

**Restore 1: the program file (only matters if you did section G).**

1. Close AutoCAD.
2. Open `C:\Program Files\Autodesk\ApplicationPlugins\AcLayerStandardizer.bundle\Contents\R26` in File Explorer.
3. If you see `acad_layer_ui.exe.off` and no `acad_layer_ui.exe`, rename `acad_layer_ui.exe.off` back to `acad_layer_ui.exe` (click Continue on the permission prompt).
4. If you cannot fix it, run the installer `D:\Projects\ACAD-Layer-Standardizer\dist\AcLayerStandardizer_BETA-1.4.0.exe` again. That puts the file back.

**Restore 2: your saved settings.**

1. Close AutoCAD.
2. In File Explorer type `%APPDATA%` in the address bar. Delete the whole `AcLayerStandardizer` folder (it holds test data now).
3. Copy `AcLayerStandardizer_aside` from your Desktop into `%APPDATA%` and rename it to `AcLayerStandardizer`.
4. If `AcLayerStandardizer_aside` is missing, unzip your Setup backup into `%APPDATA%` instead.
5. If you wrote "none" in Setup step 2, just delete the test-written folder; there is nothing to put back.
6. Delete the `Phase4Test` folder from your Desktop (it holds the test memory files).

There are "Restore done" rows at the end of the results table.

## Setup (do once)

This checklist only uses throwaway test copies: `LayerTest_A\Beds_in_plan.dwg` and `LayerTest_B\Beds_in_plan.dwg` on your Desktop (**drawing A** and **drawing B**). Your original drawings are never opened.

- [ ] 1. Back up `%APPDATA%\AcLayerStandardizer` (your usual zip).
- [ ] 2. Safety copy, because sections C and D change your saved Standardizer files. In File Explorer type `%APPDATA%` in the address bar. If the folder `AcLayerStandardizer` exists, copy it to your Desktop and rename the copy `AcLayerStandardizer_aside`. If it does not exist yet, write "none" here: ______ (the restore then just deletes the test-written folder). You must do the "LAST STEP, ALWAYS: restore" at the top when you finish or stop early.
- [ ] 3. Close AutoCAD.
- [ ] 4. Run the installer `D:\Projects\ACAD-Layer-Standardizer\dist\AcLayerStandardizer_BETA-1.4.0.exe`.
- [ ] 5. Make a folder `Phase4Test` on your Desktop. In it, create three plain text files with Notepad (File > Save As, with "Save as type" set to All files so Notepad does not add `.txt`). The text between the dashed lines is the whole content of the file:

`mem_one.json`
```
{ "schemaVersion": "1.0", "mappings": { "TESTSRC-1": "TESTTGT-1", "TESTSRC-2": "TESTTGT-2" } }
```

`mem_two.json`
```
{ "schemaVersion": "1.0", "mappings": { "TESTSRC-1": "TESTTGT-1", "TESTSRC-2": "TESTTGT-2", "TESTSRC-3": "TESTTGT-3" } }
```

`bad_memory.json`
```
this is not valid json
```

- [ ] 6. Start AutoCAD 2027. Open the two test drawings: `LayerTest_A\Beds_in_plan.dwg` and `LayerTest_B\Beds_in_plan.dwg` (from your Desktop).
- [ ] 7. In drawing A make sure the layers `A-SPARE-1` and `A-SPARE-2` exist (add them if not). In drawing B make sure `B-FURN-TEST` exists. Save both (QSAVE).

---

## A. Every way in opens the mapping window directly

Start state: A and B open, A active, mapping window closed. A thin beta notice may appear across the top of the window on the first open; section E checks that, so ignore it here.

For each way in below: start it, check the result, then close the mapping window with X (Discard if asked) before trying the next one.

1. Type **LSR**.

- [ ] The mapping window opens right away, showing drawing A's layers on the Source side.
- [ ] No welcome dialog, preview dialog or older-looking editor window appears at any point.

2. Type **LSTDR**.

- [ ] Same result: the mapping window opens, nothing else.

3. Type **LAYERSTANDARDIZER**.

- [ ] Same result.

4. Type **STD_Mappings**.

- [ ] Same result.

5. Click the ribbon button: the **Add-ins** tab, the **Layer Standardizer** button. (If you do not see an Add-ins tab or the button, write "no ribbon button" in the results table and carry on.)

- [ ] Same result.

6. Use the menu item: **Custom Apps > Layer Standardizer** (if you chose the menu item at install time; if you cannot find it, write "no menu item").

- [ ] Same result.

7. Type **StandardizeLayers**.

- [ ] Same result.

Close the mapping window with X before the next section.

## B. The Settings button and panel

Start state: mapping window closed. Type LSR.

1. Find the **Settings** button at the bottom of the left sidebar and click it.

- [ ] A Settings panel opens over the middle of the mapping window. It has the headings **Standards File**, **Memory File** and **About**.
- [ ] The mapping window behind it is still visible.
- [ ] The About part shows a line starting "Layer Standardizer Beta 1.4.0 - Build" and, under it, the sentence "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes."

2. Click the X in the top right corner of the Settings panel.

- [ ] The panel closes. The mapping window stays open and works as before.

3. Click Settings again, then close it again with its X.

- [ ] It opens and closes the same way each time.

Leave the mapping window open for the next section.

## C. Standards File

Start state: mapping window open, Settings closed. Click **Settings**.

1. Look at the **Standards File** line.

- [ ] It shows the file name of your current standard (the name only, for example `office.json` or `Standard.dwg`), not "Unavailable". Hover the mouse over it: the full path appears. If no standard has been chosen yet it says "No Standards File chosen" instead (write which).

2. Click **Change...** next to Standards File. In the file dialog pick your usual standard drawing.

- [ ] The Target side loads that standard (the status line says it is loading from AutoCAD, then the Target layers appear).
- [ ] The Standards File line now shows the file name you picked.

3. Missing file check. Close the Settings panel and the mapping window (X, Discard if asked). In File Explorer find the standard file you just picked and rename it by adding `_off` at the end of its name, for example `Standard.dwg_off`. Back in AutoCAD (drawing A active) type LSR and click **Settings**.

- [ ] The Standards File line reads "Unavailable:" followed by the file name, in amber (orange-yellow) colour.
- [ ] A short "Checking..." may show for a moment first; it must settle on "Unavailable:".

4. Rename the standard file back to its proper name in File Explorer (remove the `_off`). Click **Change...** in the panel and pick it again.

- [ ] The line returns to the plain file name (not amber).

Leave the mapping window open.

## D. Memory File: change, import, export, corrupt file

Start state: mapping window open. Click **Settings**. This section changes your saved memory settings; the restore at the top undoes it.

1. Look at the **Memory File** line.

- [ ] It shows the full path of the memory file in use (normally `...\AcLayerStandardizer\standards_memory.json`). Write the path here: ______

2. **Change to a new file.** Click **Change...** under Memory File. In the save dialog go to your Desktop `Phase4Test` folder, type the name `test_memory.json` and save. (This file does not exist yet; that is fine.)

- [ ] The status text in the panel reads "Memory file changed. Now using test_memory.json."
- [ ] The Memory File line now shows the `Phase4Test\test_memory.json` path.

3. **Import a file.** Click **Import...** and pick `mem_one.json`.

- [ ] The status text reads "Imported 2 mappings. 2 were new."

4. **Import a file with new and existing entries.** Click **Import...** and pick `mem_two.json`.

- [ ] The status text reads "Imported 3 mappings. 1 was new." (one entry, `TESTSRC-3`, is new; the other two were already there).

5. Import `mem_two.json` once more.

- [ ] "Imported 3 mappings. 0 were new." Nothing is added.

6. **Import the file it is already using.** Click **Import...** and pick `Phase4Test\test_memory.json` itself.

- [ ] The status text reads "Imported 3 mappings. 0 were new." and there is no error.

7. **Export.** Click **Export...**, save as `export_out.json` in the `Phase4Test` folder.

- [ ] The status text reads "Exported your memory to" followed by that file's path.
- [ ] Open `export_out.json` in Notepad: it lists `TESTSRC-1`, `TESTSRC-2` and `TESTSRC-3`.

8. **Re-import the export.** Click **Change...** and save a new file named `test_memory2.json` in `Phase4Test`. Then click **Import...** and pick `export_out.json`.

- [ ] The status text reads "Imported 3 mappings. 3 were new."

9. **A corrupt file is refused.** Click **Change...** and pick `bad_memory.json` in `Phase4Test` (if Windows asks "replace?", answer Yes; the Standardizer does not overwrite it).

- [ ] A message appears in the panel saying it could not use the memory file `bad_memory.json` and that the old memory stays in use.
- [ ] The Memory File line still shows `test_memory2.json` (it did not change).
- [ ] Open `bad_memory.json` in Notepad: it still says "this is not valid json" (it was not touched).

10. Click **Import...** and pick `bad_memory.json`.

- [ ] A message starting "Import failed:" appears. The Memory File line is unchanged.

11. Close the Settings panel and the mapping window (X, Discard if asked).

Do the "LAST STEP, ALWAYS: restore" (Restore 2) soon after this section. You can keep going if you wish; nothing later depends on the memory file.

## E. The beta notice: once per AutoCAD session

Start state: close AutoCAD completely, then start it fresh and open drawing A. The notice is shown on the first open in a session only, so do not open the mapping window before step 1.

1. Type **LSR**.

- [ ] A banner across the top of the mapping window reads: "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes."
- [ ] The mapping window behind it is usable (you can click layers).

2. Click the small **x** at the right end of the banner.

- [ ] The banner disappears.

3. Close the mapping window with X (Discard if asked). Type **LSR** again.

- [ ] The banner does **not** come back.

4. Click **Settings**.

- [ ] The beta sentence is shown in the About part of the panel. (It is always there.) Close the panel.

5. Close the mapping window. Type LSR a third time.

- [ ] Still no banner. Close the mapping window.

6. Close AutoCAD, start it again, open drawing A and type LSR.

- [ ] The banner is back (new AutoCAD session). Close it with its x and close the mapping window.

## F. Typing LSR while the window is open

Start state: drawing A open, mapping window closed. Type LSR so the window is open.

1. Click on the AutoCAD drawing area so AutoCAD is in front of the mapping window (or press Alt+Tab). Type **LSR** again.

- [ ] No second mapping window opens. The existing one comes to the front.
- [ ] The AutoCAD command line shows: "The Layer Standardizer window is already open; it follows the active drawing."

2. Minimize the mapping window. Type **LSTDR**.

- [ ] The window is restored and comes to the front. Still only one mapping window exists. Same message on the command line.

3. Close the mapping window with X (Discard if asked).

## G. The failure message (a program file missing)

This section temporarily renames a file inside the installed plug-in. **Chris: only do this when you are ready for a permission prompt (UAC). Ask Claude first if you are unsure. The restore step at the end cannot be skipped.** Start state: AutoCAD open, mapping window closed. AutoCAD 2027 uses the `R26` folder.

1. In File Explorer open `C:\Program Files\Autodesk\ApplicationPlugins\AcLayerStandardizer.bundle\Contents\R26`. Rename `acad_layer_ui.exe` to `acad_layer_ui.exe.off`. Click **Continue** on the permission prompt.

- [ ] The rename worked and the folder now shows `acad_layer_ui.exe.off`.

2. In AutoCAD (drawing A active) type **LSR**.

- [ ] No window opens, and AutoCAD does not crash or show an error box.
- [ ] The command line shows one plain message: "The Layer Standardizer window could not start: its program file (acad_layer_ui.exe) was not found (looked in:" followed by the plug-in folder (ending in `Contents\R26`) and some more folder text, then "Please reinstall the Layer Standardizer."
- [ ] Write down the exact message you see: ______

3. Type **LSTDR**.

- [ ] The same kind of message appears (no window, no crash).

4. **RESTORE NOW, do not skip.** In File Explorer rename `acad_layer_ui.exe.off` back to `acad_layer_ui.exe` (Continue on the prompt).

- [ ] The folder shows `acad_layer_ui.exe` again and no `.off` file.

5. Type **LSR** in AutoCAD.

- [ ] The mapping window opens normally. Close it with X (Discard if asked).

If at any point you stop between step 1 and step 4, do "Restore 1" at the top.

## H. Undo still works

Start state: drawing A open, mapping window closed. This section changes drawing A; the layers come back with Undo, and you answer No to any save prompt.

1. Type **LSR**. On the Source side connect the layer `A-SPARE-1` to any Target layer (drag it onto the Target layer). Click **Apply**.

- [ ] The change is applied (the status line says "Applied 1 mappings." or similar; write the exact words ______).

2. Type **LA** (Layer Properties) and look for `A-SPARE-1`.

- [ ] `A-SPARE-1` is no longer in the list (it was renamed or merged into the Target layer). Close the palette.

3. Type `ACLAYERSTD.UndoStandardization` and press Enter.

- [ ] The command line shows a snapshot line, "1 renamed layers" and "0 erased layers" or "0 renamed layers" and "1 erased layers" (one of the two is 1), then asks "Revert these changes?" with Yes and No.

4. Type **Yes** and press Enter.

- [ ] The command line ends with "AcLayerStandardizer: Undo complete."
- [ ] In Layer Properties (LA) `A-SPARE-1` is back.

5. Type the command again.

- [ ] It says "No standardization snapshot found to revert." (the snapshot is used up).

## I. Short Phase 3 smoke check

Start state: A and B open, mapping window closed. Type LSR with A active.

1. **Drawing switch.** Click the tab for drawing B.

- [ ] Within about a second the Source side shows B's layers (including `B-FURN-TEST`). Switch back to A: A's layers come back.

2. **Close protection.** In A make 1 connection by hand. Type **CLOSE**.

- [ ] A dialog with Apply, Discard and Cancel appears. Choose **Cancel**: the drawing stays open and the connection is still there.

3. **Quit protection.** With the connection still in place type **QUIT**.

- [ ] AutoCAD does not close. The mapping window shows its Apply / Discard / Cancel dialog. Choose **Cancel**: nothing closes.

4. Close the mapping window with X and choose **Discard**.

## J. The removed commands

Start state: any drawing open, mapping window closed.

1. Type **STD_Settings** and press Enter.

- [ ] The command line says Unknown command "STD_SETTINGS". Nothing opens.

2. Type **ApplyStandardization** and press Enter.

- [ ] The command line says Unknown command "APPLYSTANDARDIZATION". Nothing opens.

3. Type **ACLAYERSTD.UndoStandardization** and press Enter, then press Esc or choose No.

- [ ] This one is still a known command (it shows the snapshot message or "No standardization snapshot found"). It is the only old command that stays.

---

**Now do the "LAST STEP, ALWAYS: restore" at the top (both parts) if you have not already.**

## Results (paste this table back to Claude)

| Scenario | Pass / Fail | One-line note |
|---|---|---|
| A. Every way in (LSR, LSTDR, LAYERSTANDARDIZER, STD_Mappings, ribbon button, menu item, StandardizeLayers) | | |
| B. Settings button and panel | | |
| C. Standards File (name, amber "Unavailable:", Change...) | | |
| D. Memory File (change, import, export, corrupt refused; include the status texts) | | |
| E. Beta notice once per session | | |
| F. LSR while the window is open | | |
| G. Failure message (include the exact message) | | |
| H. Undo after Apply | | |
| I. Phase 3 smoke check | | |
| J. Removed commands | | |
| Restore 1 done (program file back), or G not done | Done / Not done | |
| Restore 2 done (saved settings back) | Done / Not done | |

If something fails, also mention these two log files (they are in `%TEMP%`):

- `%TEMP%\AcLayerStandardizer-ipc.log`
- `%TEMP%\AcLayerStandardizer-rust-ipc.log`
