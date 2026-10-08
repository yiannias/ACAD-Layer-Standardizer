# Beta 1.4.0

This release makes the mapping window the one and only editor, and adds a Settings panel so you no longer need typed commands to manage your files.

**One way in**
- `LSR`, `LSTDR`, `LAYERSTANDARDIZER` and the ribbon and menu buttons now open the mapping window directly. The welcome dialog is gone.
- The mapping window is now the only editor. The older fallback editor has been removed.
- If the window cannot start, a plain message at the AutoCAD command line says why and suggests reinstalling.

**New Settings panel**
- A Settings button at the bottom of the left panel opens the panel inside the window.
- Standards File: choose a different one, and the panel shows when the file is missing.
- Memory File: change its location, Import and Export.
- An About line shows the version.

**Beta notice**
- A short notice is shown once per AutoCAD session and is always available in Settings: "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes."

**Removed commands**
- `ApplyStandardization` (the quick preview-and-apply) is gone. Use the mapping window.
- `STD_Settings`, `STD_SetTemplate`, `STD_SetMemoryFile`, `STD_ExportMemory` and `STD_ImportMemory` are gone. Their jobs are now in the Settings panel.
- `ACLAYERSTD.UndoStandardization` still works.

**Node mode**
- The drawing-name subtitles and the filter boxes now match the node text size.
- The hint text no longer shows an empty box.

**Under the hood**
- The plug-in is now about two thirds Rust. The AutoCAD connector keeps only what AutoCAD requires.

The installer includes the AutoCAD plug-in and Rust mapping editor for AutoCAD 2021–2027.
