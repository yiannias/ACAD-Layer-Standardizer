# Continuity Handoff

## Current work

- Project: `ACAD-Layer-Standardizer`, Rust experiment based on pre-Rust Beta 1.0.
- Active worktree: `D:\Projects\ACAD-Layer-Standardizer-rust-core`.
- Branch: `codex/rust-beta1-parity` (uncommitted changes).
- Preserve the Beta 1.0 appearance and single-window workflow. Performance improvements are welcome, especially for drawing and arranging nodes.
- Do not make template setup a command-line prerequisite. The mapping editor now opens without a saved standard and offers `Choose Standard` inside the right panel. Chosen paths are saved for later launches.

## Implemented in this worktree

- Version 2 pipe protocol for mapping apply, apply-and-remember, safe empty-layer purge, and loading a standard drawing.
- AutoCAD-side apply/purge operations run in AutoCAD command context, check the source drawing identity, and validate submitted names against the drawing snapshot.
- Loading or switching the standard refreshes target layers, categories, properties, memory, and the cached snapshot. Manual assignments survive only where the target name remains present.
- The Rust editor now enables Apply and Apply & Remember when mappings exist, supports manual drag mappings (including mapping several selected sources to one target), mapping undo/redo, source and target search, and confirmed purge.
- The Rust editor includes Beta context actions for source unmatching/filter visibility, target-category visibility, and connection deletion.
- Target organization follows Beta behavior: currently connected standards come first; column count follows visible source count with a 20-row minimum and a five-column cap. Target filters preserve the inclusive broad-category rules, the primary Specific category rule, and always-hidden system layers.
- The Animations setting now eases node rearrangement after layout/filter changes without animating zoom or pan.
- Per-frame connection drawing now uses a target-name lookup instead of scanning every target for each connection. Exact and translation-memory matching now use prebuilt case-insensitive lookups rather than rescanning the standard layers per source layer.
- Release builds now launch the Rust editor too. The release UI executable is built and included beside each .NET-era plugin DLL in both the loose bundle and installer payload.
- The IPC snapshot carries AutoCAD's configured heuristic threshold into Rust. Rust restores the Beta UI preferences from `%LOCALAPPDATA%\AcLayerStandardizer\ui_preferences.json`, including window size/maximized state, graph zoom/pan, and animation preference.

## Verification and limits

- Chris confirmed the live Rust window now receives the active drawing and standards. The server log recorded a snapshot of 39 source layers and 321 standard layers; the Rust client read the response successfully. The corrected `bin/codex-fixed/Debug/net8.0-windows/AcLayerStandardizer.dll` was confirmed loaded.
- Rust Release UI build succeeded. .NET Release builds succeeded for net48, net8.0-windows, and net10.0-windows (existing warnings on the first full compile, no errors). `git diff --check` succeeded. No tests were run.
- `build.ps1 -PackageOnly -SkipInstaller` produced `AcLayerStandardizer.bundle`; verified that each R24/R25/R26 payload contains both the plugin DLL and `acad_layer_ui.exe`. The installer compiler was not run.
- Apply, Apply & Remember, standard switching, and Purge have not yet been exercised by Chris in AutoCAD. Codex has not opened or controlled AutoCAD.
- The release handoff and user-preference persistence code now build, but still need user-run AutoCAD verification. Do not call complete Beta 1.0 parity until the mapping/apply/purge flows and release bundle are verified.

## Next step

Have Chris restart AutoCAD, `NETLOAD` the Release net8 DLL from `src\AcLayerStandardizer\bin\Release\net8.0-windows\AcLayerStandardizer.dll`, and run `LSTDR`. Verify the Release build opens the Rust editor with source and standard layers. Then guide one user-controlled check at a time for standard switching, Apply & Remember, and safe purge. Do not launch or manipulate AutoCAD from Codex, and do not claim these paths are verified before Chris reports the result.

## Project instructions

- No `AGENTS.md` or `CLAUDE.md` is present in this repository. Follow the user's workspace instructions and `docs/rust-rearchitecture-plan.md`.
- Preserve unrelated changes in the primary checkout. Work only in the managed worktree and do not merge to `main` without a request.
