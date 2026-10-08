# Phase 4 design: everything but the connector is Rust (Beta 1.4.0)

Status: draft for review. Roadmap context: `docs/plan-1.3.md`, Phase 4. This work ships as **Beta 1.4.0** (the 1.3 plan's name stays; the version moves to 1.4). Builds on Phases 1 to 3 (released as Beta 1.3.0).

## Goal

After this phase no logic or UI lives outside the AutoCAD connector:

1. `LSR`, `LSTDR`, `LAYERSTANDARDIZER`, the ribbon button and the menu item all open the **Rust mapping window** directly. The WPF welcome dialog is gone.
2. The window gets a **Settings panel** (Standards File, Memory File with import and export, About) and a once-per-session **beta notice**.
3. The WPF mapping editor (the old fallback), its dialogs, the preview-and-apply flow, the typed settings commands and the C# matching, categorizing and memory code are removed.

Done when: nothing in the C# connector reads or interprets layer-matching data, the plug-in has no WPF user interface of its own, and every user-visible job the old dialogs did is available in the Rust window.

## Decisions already made

| Question | Decision |
|---|---|
| What opens on `LSR` / the ribbon button | The mapping window directly; settings move into the window. |
| How the Settings panel is reached | A **Settings** button at the bottom of the left panel, under "Display". |
| The beta notice | Shown once per AutoCAD session as a dismissible banner at first window open, and always in Settings, in lighter wording (below). |
| Quick preview-and-apply flow (`ApplyStandardization`, `PreviewDialog`) | Deleted. `UndoStandardization` stays as a connector command. |
| Typed settings commands (`STD_Settings`, `STD_SetTemplate`, `STD_SetMemoryFile`, `STD_ExportMemory`, `STD_ImportMemory`) | Deleted; the panel replaces them. |
| Structure | One release in three stages: Add, Remove, Tidy. Each stage ends green and committed. |
| Wording | The panel says **"Standards File"**. |

Notice wording: "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes."

## What users see

- Every entry point (`LSR`, `LSTDR`, `LAYERSTANDARDIZER`, `StandardizeLayers`, `STD_Mappings`, the ribbon button, the menu item) runs the same handler, which launches (or brings forward) the mapping window.
- **Settings** panel, opened from the bottom of the left panel, drawn over the canvas (an egui window with a close button):
  - **Standards File:** the file name (full path as a tooltip), "Unavailable: <name>" in amber when the file cannot be found, and a **Change...** button that runs the existing Choose Standard flow.
  - **Memory File:** the current path, **Change...** (a save dialog, so a new file can be created), **Import...** (open dialog), **Export...** (save dialog). A one-line status area shows the result.
  - **About:** "Layer Standardizer Beta 1.4.0 - Build <id>" and the notice text.
- First open of the window in an AutoCAD session also shows the notice as a dismissible banner at the top.

## Window (Rust) design

- **New module** `rust/crates/acad_layer_ui/src/settings_panel.rs`: panel state (open flag, status line) and pure helpers (the Standards File label with its missing-file marker, import and export status messages). egui drawing stays thin; the logic is unit tested.
- **Core additions** in `acad_layer_core`:
  - `PluginConfig::set_memory_path(path: &Path, memory: &str) -> Result<PluginConfig, ConfigError>`: like `set_template_path`, re-reads the file, keeps unknown keys, never overwrites a corrupt config.
  - `MemoryStore::import_from(&self, source: &Path) -> Result<ImportReport, MemoryError>` with `ImportReport { imported: usize, added: usize }`: both files are read with `load_checked`, existing entries win (as `MemoryStore::merge` does today), the result is saved with the existing backup rules. A corrupt source or destination is an error and nothing is written.
  - `MemoryStore::export_to(&self, destination: &Path) -> Result<(), MemoryError>`: writes the current memory to the chosen file; if there is no memory file yet, the error says there is nothing to export.
- **Changing the memory file** (`LayerStandardizerApp`): the new location is read with `load_checked` first; on success the config is updated, `memory_store` and `memory_mappings` are replaced and matches recomputed; on failure the old memory stays in use and the status line says why.
- **Notice banner:** the window takes a `--first-run-notice` argument. When present it shows the banner once, dismissible, with no persistence beyond the process.
- File dialogs use the existing `rfd` crate; none runs on a worker thread that outlives the window.

## Connector (C#) design

**Stays:** `EntryPoint`, `RibbonSetup`, `MenuSetup`, `LsrCommandHandler`, `PluginConfig` (ribbon/menu flags, config paths), the pipe server and protocol (`IpcBridgeServer`, `IpcProtocol`, `EventFeed`, `CloseGuard`, `WindowInstance`, `ActiveDrawingTracker`/`State`), `RustUiLauncher`, `SideDatabase` and `LayerProperties` (standard-layer reading), `LayerHelper`, `NaturalSortComparer`, `Net48Compat`, the apply code (`ApplyMappings` and its rollback snapshot) and `UndoStandardization`.

**Changes:**
- One handler (`MappingsCommand.ShowMappingsEditor`) carries every command attribute above. It no longer loads the template, memory or categories and no longer has a WPF branch.
- The apply code moves out of `StandardizeCommand` into `Core/LayerApplier.cs` (`ApplyMappings` unchanged); `UndoStandardization` moves into `Commands/UndoCommand.cs`; `StandardizeCommand.cs`, `WelcomeCommand.cs` and `SettingsCommand.cs` are deleted.
- `RustUiLauncher` passes `--first-run-notice` on the first launch in an AutoCAD session (a static flag, decided by a small pure `NoticePolicy`).
- Protocol: no new version. `ApplyPlan`'s `remember` field is accepted and ignored (the window writes memory itself; the response says `remembered: false`); the legacy `LoadStandard` request is removed; the memory/category fields of the snapshot are sent empty. A client of any older shape still gets a valid answer.

**Removed:** the entire `UI/` folder (WPF editor, view models, dialogs, theme, converters; any type the connector still needs moves to `Core/`), `Matching/*`, `Core/LayerCategorizer.cs`, `Core/LayerDictionaryDefinition.cs`, `Core/UserPreferences.cs`, `Core/RustNativeBridge.cs`, `Data/MemoryStore.cs`, `Data/TranslationMemory.cs`, and, in Rust, the unused `acad_layer_ffi` crate. Rule: delete anything with no remaining caller, and let the build and tests prove it. The plan lists the exact files after a call-site check. The same rule applies to the Rust protocol crate: the pieces of the removed requests (`load_standard`, `IpcRequest::LoadStandard`, `IpcResponse::TemplateLoaded`) and any other `acad_layer_ipc` item with no caller go too.

**Project file:** remove the `Nodify` package and our own XAML. The WPF framework reference (`UseWPF`) **stays**: the ribbon (`RibbonSetup`) and the startup timer (`EntryPoint`) use WPF types. The plug-in just has no WPF user interface of its own.

## The launch guard (added before the fallback is removed)

With the old editor gone, a missing or broken Rust program file means no editor. So:
- If the executable is not found, or the process cannot start, the command writes a plain message at the AutoCAD command line: what failed (with the folder searched or the error) and "Please reinstall the Layer Standardizer." Nothing else happens; it never throws.
- A C# test reads `installer/ACADLayerStandardizer.iss` and asserts that `acad_layer_ui.exe` is installed into all three era folders (`R24`, `R25`, `R26`), so a broken installer script fails the build.
- The launch outcomes are modelled as a pure function (found / not found / start failed) with unit tests.

## Build order and testing

**Stage 1 - Add** (nothing deleted; the old editor still works as fallback):
1. `acad_layer_core`: `set_memory_path`, `import_from`, `export_to` with tests (corrupt files, existing keys win, missing file).
2. `settings_panel.rs` and the Settings button, the memory-file switch, the banner and `--first-run-notice`.
3. Connector: one handler for all commands; `NoticePolicy`; the launch guard and installer test.

**Stage 2 - Remove:** delete the WPF editor, `UI/`, `Matching/*`, the C# categorizing/memory/config helpers, the preview flow, the typed commands, `RustNativeBridge` and `acad_layer_ffi`; move the apply and undo code; drop `remember`/`LoadStandard`. The C# tests that covered only deleted code are removed with it (HeuristicMatcher, LayerCategorizer, the WPF view-model tests, the C# parity generator and RustBridge tests); the Rust parity tests keep reading the frozen files in `tests/parity/`.

**Stage 3 - Tidy:** project file (drop `Nodify`, our XAML and any unused references), `build.ps1`/installer, version bump to 1.4.0, release notes, the plan documents.

Every stage: tests written first; the whole suite (`dotnet test`, `cargo test --workspace`, then `.\build.ps1`) run at the end; test totals checked against the totals the plan predicts (they rise in Stage 1 and fall in Stage 2 by the number of tests removed with the code); one commit per logical step.

**Verification:** a manual checklist for Chris (Settings panel, memory import/export, the notice, every entry point, the failure message with the Rust file temporarily renamed, undo after an apply) before any release. Releasing and pushing happen only on his explicit go-ahead.

## Out of scope

- Moving the ribbon, the menu or the entry point out of C# (AutoCAD requires them to be .NET).
- A UI for `InstallRibbon` / `InstallMenu`.
- The remaining items in the post-1.3 list in `docs/plan-1.3.md` (they are small and independent).
- A Rust "apply all suggestions" mode or an undo button in the window.

## Risks

| Risk | Mitigation |
|---|---|
| The window cannot start and no fallback exists | Launch guard with a plain message; installer-script test; checklist step that renames the Rust file. |
| Deleting code something still calls | Delete only after a call-site check; the build and tests prove it; Stage 1 first so the fallback exists until Stage 2. |
| Memory import/export damages the memory file | Both sides read with `load_checked`; existing backup-and-validate save; corrupt input refused. |
| Deleted tests hide a regression | Tests covering behavior that still exists (apply, undo, protocol, close guard) are kept; the parity data stays frozen. |
| `UseWPF` cannot be removed | It stays by design; only our own WPF code goes. |
