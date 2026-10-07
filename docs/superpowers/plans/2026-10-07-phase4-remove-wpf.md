# Phase 4: Everything but the Connector is Rust (Beta 1.4.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `LSR`, `LSTDR`, the ribbon and menu open the Rust mapping window directly; the window gets a Settings panel and a once-per-session beta notice; the WPF editor, preview flow, typed settings commands and the C# matching/categorizing/memory code are removed.

**Architecture:** Stage 1 adds the Rust Settings panel (config, memory import/export and file switching in `acad_layer_core` and `acad_layer_ui`) and the connector's single command handler, notice flag and launch guard, with the old editor still present as a fallback. Stage 2 moves the apply/undo code into `Core/`, trims the protocol, then deletes the WPF editor and dead C#/Rust. Stage 3 tidies the project and bumps the version.

**Tech Stack:** Rust (`serde`, egui/eframe, `rfd`), C# (net48 / net8.0-windows / net10.0-windows, xunit), Inno Setup installer.

**Spec:** `docs/superpowers/specs/2026-10-07-phase4-remove-wpf-design.md`. Builds on Phases 1 to 3 (Beta 1.3.0).

## Global Constraints

- Panel wording: **"Standards File"** and **"Memory File"**. Notice text, exactly: `Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes.`
- Entry points that all open the mapping window: `LSR`, `LSTDR`, `LAYERSTANDARDIZER`, `StandardizeLayers`, `STD_Mappings`, the ribbon button and the menu item.
- Deleted commands: `ApplyStandardization`, `STD_Settings`, `STD_SetTemplate`, `STD_SetMemoryFile`, `STD_ExportMemory`, `STD_ImportMemory`. Kept: `ACLAYERSTD.UndoStandardization`.
- Protocol version stays 4; request/response shapes are unchanged. The snapshot keeps sending `memory_mappings: {}`, `target_filters: []`, `always_hidden_targets: []` and `heuristic_threshold: 0.6` (the Rust `DrawingSnapshot` requires them). `ApplyPlan`'s `remember` is accepted and ignored (`remembered: false`).
- `UseWPF` stays in the connector project (the ribbon and the startup timer use WPF types). Only our own XAML/code and `Nodify` go.
- A missing or failing Rust window must print a plain AutoCAD command-line message and never throw.
- **xunit pitfall in this repo:** a theory whose `InlineData` parameter is an enum is silently dropped. Use string/bool/int parameters. After adding or deleting C# tests, check the `dotnet test` total changes by exactly the expected amount.
- Version bump to 1.4.0 happens only in Task 13, and only `build.ps1`, `rust/crates/acad_layer_ui/Cargo.toml` (+ `Cargo.lock`), `dist/PackageContents.xml` and a new release-notes file. Do NOT change `docs/index.html`, tag, push or release; those wait for Chris's go-ahead.
- Never launch AutoCAD, the app or any GUI; no screenshots. Run `.\build.ps1` in PowerShell with output redirected to a file (not piped through `2>&1`). Do not pipe `cargo` through `2>&1` in PowerShell.
- Work directly on `main`. Files use CRLF line endings; keep each file's existing endings. Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Rust window missing or broken (Tasks 6 and 10):** the command prints one plain message with the folder searched, then stops; nothing references WPF afterwards; a test pins the exact messages.
2. **Importing the memory file into itself (Task 2):** adds nothing, writes nothing, reports 0 new. A corrupt source or corrupt current memory is an error and neither file is changed.
3. **Switching the memory file (Task 4):** a corrupt or unreadable target leaves the old memory and config untouched; a not-yet-existing file is allowed (empty memory).
4. **Settings actions while an Apply, Purge or Load Standard is in flight (Task 5):** disabled, never racing the connector.
5. **Notice once per AutoCAD session (Task 6):** the flag is passed on the first successful launch only, not again when the window is relaunched or when a second `LSTDR` merely focuses the open window; unknown command-line arguments never crash the window.
6. **Deleted tests (Task 10):** the `dotnet test` total falls by exactly the number of tests in the deleted files, so no surviving test was dropped.

## File Structure

- Rust: `acad_layer_core/src/config.rs` (`set_memory_path`), `acad_layer_core/src/memory.rs` (`ImportReport`, `import_from`, `export_to`), `acad_layer_ui/src/settings_panel.rs` (new: panel state, pure helpers), `acad_layer_ui/src/launch.rs` (new: argument parsing), `acad_layer_ui/src/data.rs` (`switch_memory_file`), `acad_layer_ui/src/main.rs` and `mapping_editor.rs` (wiring, Settings button, banner).
- C#: `Core/NoticePolicy.cs` and `Core/LaunchGuard.cs` (new, pure), `Core/RustUiLauncher.cs`, `Commands/MappingsCommand.cs` (single handler), `Core/LayerApplier.cs`, `Core/LayerReader.cs`, `Commands/UndoCommand.cs` (new), `Core/IpcBridgeServer.cs` (trim).
- Tests: `tests/AcLayerStandardizer.Tests/NoticePolicyTests.cs`, `LaunchGuardTests.cs`, `InstallerScriptTests.cs` (new).
- Docs: `docs/superpowers/plans/2026-10-07-phase4-manual-checklist.md`, `docs/release-notes-beta-1.4.0.md`, `docs/plan-1.3.md`.

---

## Stage 1: Add

### Task 1: `PluginConfig::set_memory_path`

**Files:** Modify `rust/crates/acad_layer_core/src/config.rs`.

**Interfaces:**
- Produces: `impl PluginConfig { pub fn set_memory_path(path: &Path, memory: &str) -> Result<PluginConfig, ConfigError>; }` with the same contract as `set_template_path`: re-reads the file, keeps unknown keys and other settings, a corrupt config is an error and is not overwritten, a missing config is created.

- [ ] **Step 1: Write failing tests** in `config.rs`: `setting_the_memory_path_keeps_other_settings` (file has `HeuristicThreshold` 0.9 and `InstallRibbon` false; after the call they are unchanged and `MemoryFilePath` is the new value), `setting_the_memory_path_never_overwrites_a_corrupt_config` (error, file content unchanged), `setting_the_memory_path_creates_the_config_when_missing`.
- [ ] **Step 2:** `cargo test --manifest-path rust/Cargo.toml -p acad_layer_core set` and confirm the new tests fail to compile.
- [ ] **Step 3:** Implement `set_memory_path` next to `set_template_path`, reusing `load_from` and `save_to`.
- [ ] **Step 4:** `cargo test --manifest-path rust/Cargo.toml --workspace`; expected all pass.
- [ ] **Step 5: Commit** `feat(rust): set the memory file path without disturbing other settings`.

### Task 2: Memory import and export

**Files:** Modify `rust/crates/acad_layer_core/src/memory.rs` and `lib.rs` (re-export `ImportReport`).

**Interfaces:**
- Produces:
  ```rust
  #[derive(Debug, Clone, PartialEq, Eq)]
  pub struct ImportReport { pub imported: usize, pub added: usize }
  impl MemoryStore {
      pub fn import_from(&self, source: &Path) -> Result<ImportReport, MemoryError>;
      pub fn export_to(&self, destination: &Path) -> Result<(), MemoryError>;
  }
  ```
- `import_from`: read `source` and the store's own file with `load_checked` (a missing own file counts as empty); add only mappings with no case-insensitive match in the current memory (existing entries win); `imported` is the number of mappings in the source, `added` the number actually added; save with the existing `save` (backup and validation) only when `added > 0`. If `source` is the same file as the store's file (compare canonical paths), return `{ imported: n, added: 0 }` and write nothing.
- `export_to`: errors with `MemoryError::Io("there is no translation memory to export yet")` when the store's file does not exist; otherwise reads it with `load_checked` and saves it to `destination` through a `MemoryStore::new(destination)` (creating parent folders).

- [ ] **Step 1: Write failing tests:** `import_adds_only_new_mappings_and_existing_ones_win`, `import_ignores_case_when_deciding_what_is_new`, `importing_the_memory_file_into_itself_adds_nothing_and_writes_nothing` (file bytes and no new `.bak-`), `a_corrupt_import_source_is_an_error_and_changes_nothing`, `a_corrupt_current_memory_is_an_error_and_is_not_overwritten`, `import_into_a_missing_memory_file_creates_it`, `export_writes_a_copy_with_the_same_mappings`, `export_without_a_memory_file_says_there_is_nothing_to_export`.
- [ ] **Step 2:** `cargo test --manifest-path rust/Cargo.toml -p acad_layer_core`; confirm failures.
- [ ] **Step 3:** Implement as above.
- [ ] **Step 4:** `cargo test --manifest-path rust/Cargo.toml --workspace`; all pass, warning-free.
- [ ] **Step 5: Commit** `feat(rust): import and export the translation memory`.

### Task 3: Settings panel logic (pure)

**Files:** Create `rust/crates/acad_layer_ui/src/settings_panel.rs` (`mod settings_panel;` in `main.rs`).

**Interfaces:**
- Consumes: `acad_layer_core::ImportReport` (Task 2).
- Produces:
  ```rust
  pub const BETA_NOTICE: &str = "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes.";
  #[derive(Default)] pub struct SettingsPanel { pub open: bool, pub status: String }
  pub enum SettingsAction { ChangeStandard, ChangeMemoryFile, ImportMemory, ExportMemory }
  pub struct StandardsFileLabel { pub text: String, pub missing: bool }
  pub fn standards_file_label(path: &str, exists: bool) -> StandardsFileLabel;
  pub fn import_status(report: &ImportReport) -> String;
  pub fn export_status(path: &Path) -> String;
  pub fn about_line(version: &str, build: &str) -> String;
  pub fn settings_actions_enabled(busy: bool) -> bool;   // false while an Apply/Purge/Load Standard is in flight
  ```
- Strings: empty path -> `"No Standards File chosen"` (not missing); existing -> the file name only; missing -> `"Unavailable: <file name>"` with `missing: true`. `import_status`: `"Imported N mappings. M were new."` with singular `"1 mapping"` / `"1 was new"` where the count is 1. `export_status`: `"Exported your memory to <path>."`. `about_line`: `"Layer Standardizer Beta <version> - Build <build>"`.

- [ ] **Step 1: Write failing tests:** `standards_file_label_covers_empty_existing_and_missing`, `import_status_uses_singular_and_plural_wording`, `export_status_names_the_file`, `about_line_has_the_version_and_build`, `beta_notice_is_the_agreed_wording` (exact string), `settings_actions_are_disabled_while_busy`.
- [ ] **Step 2:** `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ui settings_panel`; confirm failures.
- [ ] **Step 3:** Implement. No egui in this file.
- [ ] **Step 4:** `cargo test --manifest-path rust/Cargo.toml --workspace`; all pass, warning-free (allow `dead_code` only narrowly until Task 5 wires it, with a comment).
- [ ] **Step 5: Commit** `feat(rust): settings panel logic`.

### Task 4: Switching the memory file; launch arguments

**Files:** Modify `rust/crates/acad_layer_ui/src/data.rs`; create `rust/crates/acad_layer_ui/src/launch.rs` (`mod launch;`).

**Interfaces:**
- Consumes: `PluginConfig::set_memory_path` (Task 1), `MemoryStore::load_checked`.
- Produces:
  ```rust
  // data.rs
  pub struct MemoryChange { pub config: PluginConfig, pub store: MemoryStore, pub memory: TranslationMemory }
  pub fn switch_memory_file(config_path: &Path, config_dir: &Path, new_path: &str) -> Result<MemoryChange, String>;
  // launch.rs
  pub struct LaunchArgs { pub owner_hwnd: Option<isize>, pub first_run_notice: bool }
  pub fn parse_launch_args(args: impl Iterator<Item = String>) -> LaunchArgs;
  ```
- `switch_memory_file`: read the target with `MemoryStore::new(new_path).load_checked()` first (a missing file is fine and gives empty memory); on error return `Err(<plain message naming the file>)` without touching the config; on success call `PluginConfig::set_memory_path`, then return the reloaded config, the store at `config.effective_memory_path(config_dir)` and the memory.
- `parse_launch_args`: recognises `--owner-hwnd=<decimal>` and `--first-run-notice`; unknown arguments and an unparsable HWND are ignored (never a panic).

- [ ] **Step 1: Write failing tests:** `switching_to_a_valid_memory_file_updates_the_config_and_loads_it`, `switching_to_a_corrupt_file_changes_nothing` (config bytes unchanged, `Err`), `switching_to_a_new_path_starts_with_empty_memory`, `parses_owner_and_notice`, `ignores_unknown_arguments`, `an_invalid_owner_is_ignored`.
- [ ] **Step 2:** `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ui`; confirm failures.
- [ ] **Step 3:** Implement; replace the inline `--owner-hwnd` parsing in `main()` with `parse_launch_args`.
- [ ] **Step 4:** `cargo test --manifest-path rust/Cargo.toml --workspace`; all pass.
- [ ] **Step 5: Commit** `feat(rust): switch the memory file safely; parse launch arguments`.

### Task 5: Settings panel and banner in the window (egui wiring)

**Files:** Modify `rust/crates/acad_layer_ui/src/mapping_editor.rs` (`draw_left_panel`, `MappingEditorEvent`) and `main.rs`.

**Interfaces:**
- Consumes: everything from Tasks 1 to 4.
- Produces: `MappingEditorEvent.open_settings: bool`; in `LayerStandardizerApp`: `settings: SettingsPanel`, `notice_visible: bool` (true when `LaunchArgs.first_run_notice`), `fn handle_settings_action(&mut self, ctx, frame, action)`.
- Behavior:
  - A **Settings** button at the bottom of the left panel, under the "Display" section, sets `open_settings`.
  - The panel is an egui window over the canvas with a close button. **Standards File:** label from `standards_file_label` (amber when missing; full path as hover text) and **Change...** which runs the existing Choose Standard flow. **Memory File:** the effective path and **Change...** (save dialog; calls `switch_memory_file`; on success replaces `plugin_config`, `memory_store`, `memory_mappings` and recomputes matches; on error shows the message in `settings.status`), **Import...** (open dialog; `memory_store.import_from`; show `import_status`; on success reload `memory_mappings` and recompute) and **Export...** (save dialog; `export_to`; show `export_status`). **About:** `about_line(env!("CARGO_PKG_VERSION"), env!("ACAD_LAYER_UI_BUILD_ID"))` and `BETA_NOTICE`.
  - All four action buttons are disabled when `!settings_actions_enabled(self.apply_pending)`.
  - A dismissible banner at the top of the window shows `BETA_NOTICE` while `notice_visible`; the close button clears it.
  - Dialogs use `rfd` with the window as parent, as Choose Standard does. Errors never panic; a failed action shows its message in the panel.
- The egui drawing is compile-checked only (no GUI is launched); state this plainly in the report.

- [ ] **Step 1:** No GUI test is possible: all decisions already live in the tested helpers of Tasks 2 to 4 (`standards_file_label`, `import_status`, `switch_memory_file`, `settings_actions_enabled`, `parse_launch_args`). Keep egui code free of logic beyond calling them; if you find yourself adding a branch worth testing, move it into a pure function in `settings_panel.rs` with a test.
- [ ] **Step 2:** Implement the wiring.
- [ ] **Step 3:** `cargo build --manifest-path rust/Cargo.toml --workspace` is warning-free (remove the Task 3 narrow `dead_code` allows) and `cargo test --manifest-path rust/Cargo.toml --workspace` passes (UI test count rises only by any pure helper you added).
- [ ] **Step 4: Commit** `feat(rust): Settings panel and first-run notice in the mapping window`.

### Task 6: Connector: notice policy, launch guard, installer check

**Files:** Create `src/AcLayerStandardizer/Core/NoticePolicy.cs`, `Core/LaunchGuard.cs`, `tests/AcLayerStandardizer.Tests/NoticePolicyTests.cs`, `LaunchGuardTests.cs`, `InstallerScriptTests.cs`. Modify `Core/RustUiLauncher.cs`, `Commands/MappingsCommand.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public static class NoticePolicy { public const string Argument = "--first-run-notice"; public static bool ShouldPassNotice(bool alreadyShownThisSession); }
  public enum LaunchOutcome { Launched, AlreadyOpen, ExecutableNotFound, StartFailed }
  public static class LaunchGuard { public static string? DescribeFailure(LaunchOutcome outcome, string? detail); }
  ```
- `DescribeFailure` returns `null` for `Launched` and `AlreadyOpen`; for `ExecutableNotFound`: `"The Layer Standardizer window could not start: its program file (acad_layer_ui.exe) was not found (looked in: {detail}). Please reinstall the Layer Standardizer."`; for `StartFailed`: `"The Layer Standardizer window could not start ({detail}). Please reinstall the Layer Standardizer."`.
- `RustUiLauncher.TryLaunchFromActiveAutoCad` changes from returning `bool` to `public static LaunchOutcome TryLaunchFromActiveAutoCad(<existing parameters>, out string? detail)`: `AlreadyOpen` when the existing-window guard focused the open window (`WindowInstance`), `ExecutableNotFound` with `detail` = the folder(s) searched, `StartFailed` with `detail` = the exception message, otherwise `Launched`. It passes `NoticePolicy.Argument` in the process arguments on the first `Launched` in this AutoCAD process only (a static bool set after `Process.Start` succeeds; `AlreadyOpen` does not count). Both callers in `MappingsCommand` are updated and print `LaunchGuard.DescribeFailure(outcome, detail)` when it is not `null`. While the old editor still exists (Stage 1) a failure prints the message AND falls back to it as today; Task 10 removes the fallback and keeps the message. Task 9 later trims the parameter list.

- [ ] **Step 1: Write failing tests:** `NoticePolicyTests.The_first_launch_passes_the_notice_and_later_ones_do_not`, `LaunchGuardTests.Success_and_already_open_need_no_message`, `LaunchGuardTests.A_missing_program_file_message_names_where_it_looked_and_says_to_reinstall` (exact string), `LaunchGuardTests.A_failed_start_message_includes_the_reason` (exact string), `InstallerScriptTests.Installer_installs_the_rust_window_for_every_autocad_generation` (find the repo root by walking up from `AppContext.BaseDirectory` until `installer\ACADLayerStandardizer.iss` exists; assert one `Source:` line per `R24`, `R25`, `R26` for `acad_layer_ui.exe`). Use string/bool parameters only for any theory.
- [ ] **Step 2:** `dotnet test tests/AcLayerStandardizer.Tests`; confirm the new tests fail (compile errors).
- [ ] **Step 3:** Implement the two small classes and the launcher change.
- [ ] **Step 4:** `dotnet test tests/AcLayerStandardizer.Tests` on all three target frameworks. Expected: the total rises by exactly the number of new test methods (155 + the count you added); report both numbers.
- [ ] **Step 5: Commit** `feat: first-run notice flag and launch-failure message`.

### Task 7: One handler for every entry point

**Files:** Modify `src/AcLayerStandardizer/Commands/MappingsCommand.cs`, `Commands/StandardizeCommand.cs`, `Commands/WelcomeCommand.cs`.

**Interfaces:**
- Produces: `MappingsCommand.ShowMappingsEditor()` carries every command attribute: `[CommandMethod("LSTDR")]`, `[CommandMethod("LSR", CommandFlags.Modal)]`, `[CommandMethod("ACLAYERSTD", "STD_Mappings", CommandFlags.Modal)]`, `[CommandMethod("ACLAYERSTD", "StandardizeLayers", CommandFlags.Modal)]`, `[CommandMethod("LAYERSTANDARDIZER", CommandFlags.Modal)]`, `[CommandMethod("ACLAYERSTD", "LAYERSTANDARDIZER", CommandFlags.Modal)]`. The old `LaunchStandardizer` (LSTDR) forwarding method is merged into it. The `LSR`, `StandardizeLayers` and `LAYERSTANDARDIZER` attributes are removed from `StandardizeCommand.StandardizeLayers` and `WelcomeCommand.ShowWelcome` (the methods stay until Task 10).
- `LsrCommandHandler` and the menu macro keep sending `LSR ` (now it opens the mapping window directly).

- [ ] **Step 1:** This task is compile-checked only (AutoCAD command registration cannot run in tests); say so in the report.
- [ ] **Step 2:** Make the change.
- [ ] **Step 3:** `dotnet test tests/AcLayerStandardizer.Tests` (total unchanged from Task 6) and `.\build.ps1`; expected exit 0.
- [ ] **Step 4:** Stage 1 verification: `cargo test --manifest-path rust/Cargo.toml --workspace`, `dotnet test`, `.\build.ps1` all green; report the real totals.
- [ ] **Step 5: Commit** `feat: every entry point opens the mapping window directly`.

---

## Stage 2: Remove

### Task 8: Move the apply, undo and layer-reading code out of `StandardizeCommand`

**Files:** Create `src/AcLayerStandardizer/Core/LayerApplier.cs`, `Core/LayerReader.cs`, `Commands/UndoCommand.cs`. Modify `Commands/StandardizeCommand.cs`, `Commands/MappingsCommand.cs`, `Core/IpcBridgeServer.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public static class LayerApplier {
      public sealed record ApplyMappingsResult(int Renamed, int Synced);
      public static ApplyMappingsResult ApplyMappings(Database db, IReadOnlyDictionary<string, string> mappings,
          IReadOnlyDictionary<string, LayerProperties> standardLayers, PropertyMatchSettings? propSettings = null);
      internal static ObjectId? GetLayerId(LayerTable lt, Transaction tr, string name);
      internal static ObjectId GetLinetypeId(Database db, Transaction tr, string name);
      internal static string GetLinetypeName(Transaction tr, LayerTableRecord ltr);
      internal static LineWeight ParseLineWeight(string value);   // plus any other helper UndoCommand needs
  }
  internal static class LayerReader { internal static List<string> GetActiveLayerNames(Database db); internal static HashSet<string> GetEmptyLayers(Database db); }
  // Commands/UndoCommand.cs: [CommandMethod("ACLAYERSTD", "UndoStandardization", CommandFlags.Modal)] public static void UndoStandardization()
  ```
- `ApplyMappings` (including the layer-0 check from `LayerHelper.FindUnapplicableMapping`, `EnsureNotCurrentLayer`, `TransferEntities`, the by-layer helpers and the rollback snapshot) and `UndoStandardization` are moved verbatim. No behavior change. Update every caller (`IpcBridgeServer` uses `LayerApplier.ApplyMappingsResult`, `LayerApplier.ApplyMappings`, `LayerReader.*`). Delete `ApplyStandardization` and its preview flow, `SetupPipeline`, `SaveNewMappings`, `PrintResults`, `GetActiveLayerNames(Editor)` and `IsStandardizationCandidate` from `StandardizeCommand` ONLY if nothing else calls them (the build proves it); the file itself is deleted in Task 10.

- [ ] **Step 1:** Behavior-preserving move: no new tests. Confirm the existing tests cover what they did (`LayerHelperLayerZeroTests`, `IpcBridgeServerTests`).
- [ ] **Step 2:** Make the move.
- [ ] **Step 3:** `dotnet test tests/AcLayerStandardizer.Tests` (total unchanged from Task 7) and `.\build.ps1` (exit 0).
- [ ] **Step 4: Commit** `refactor: move apply, undo and layer reading out of StandardizeCommand`.

### Task 9: Trim the connector protocol

**Files:** Modify `src/AcLayerStandardizer/Core/IpcBridgeServer.cs`, `Core/RustUiLauncher.cs`, `Commands/MappingsCommand.cs`, `tests/AcLayerStandardizer.Tests/IpcBridgeServerTests.cs`.

**Interfaces:**
- Produces: the stored snapshot record loses `MemoryMappings`, `MemoryFilePath`, `TargetFilters` and `AlwaysHiddenTargets`; `SerializeSnapshot` still emits `memory_mappings: {}`, `target_filters: []`, `always_hidden_targets: []` and `heuristic_threshold: 0.6` so the Rust `DrawingSnapshot` still parses. `SetDrawingSnapshot` and `RustUiLauncher.TryLaunch...` take only what is still needed: `(Document document, IEnumerable<string> sourceLayers, IEnumerable<string> emptyLayers, string templatePath)`. The `LoadStandard` case and `LoadStandardAsync` are removed. `ApplyPlanAsync` ignores `remember`: it never writes memory, and answers `remembered = false, warning = null`; `SaveRememberedMappings` is removed.

- [ ] **Step 1: Write failing tests** in `IpcBridgeServerTests.cs`: `LoadStandard_is_an_unknown_request` (reply is an `Error`), `GetDrawingSnapshot_without_a_snapshot_still_refuses_plainly` (unchanged behavior). Remove or rewrite any existing test that depended on `LoadStandard`, memory or categories.
- [ ] **Step 2:** `dotnet test tests/AcLayerStandardizer.Tests`; confirm the new test fails.
- [ ] **Step 3:** Implement. Compile-checked only for the AutoCAD parts; say so.
- [ ] **Step 4:** `dotnet test` (report before/after totals with the reason for each change) and `.\build.ps1`.
- [ ] **Step 5: Commit** `refactor: the connector no longer touches memory, categories or LoadStandard`.

### Task 10: Delete the WPF editor and the dead C#

**Files:** Delete: the whole `src/AcLayerStandardizer/UI/` folder; `Matching/*`; `Data/MemoryStore.cs`, `Data/TranslationMemory.cs`; `Core/LayerCategorizer.cs`, `Core/LayerDictionaryDefinition.cs`, `Core/UserPreferences.cs`, `Core/RustNativeBridge.cs`; `Commands/StandardizeCommand.cs`, `Commands/WelcomeCommand.cs`, `Commands/SettingsCommand.cs`; tests `HeuristicMatcherTests.cs`, `LayerCategorizerTests.cs`, `LayerConnectionViewModelTests.cs`, `ParityTests.cs`, `RustBridgeTests.cs`. Modify: `Commands/MappingsCommand.cs` (remove the WPF branch and everything it loaded).

**Interfaces:**
- Consumes: Tasks 6 to 9.
- Result: `MappingsCommand.ShowMappingsEditor()` only reads the active drawing's layers (via `LayerReader`), calls `RustUiLauncher`, and when the outcome is a failure prints `LaunchGuard.DescribeFailure(...)` to the command line and returns. It never throws.
- Rule: delete a file only when nothing remaining references it; if the build shows a remaining caller, move that type to `Core/` instead and say so in the report. Keep `Data/RollbackSnapshot.cs`, `Core/SideDatabase.cs`, `Core/LayerProperties.cs`, `Core/PluginConfig.cs`.

- [ ] **Step 1: Before deleting**, run `dotnet test tests/AcLayerStandardizer.Tests -f net10.0-windows --list-tests` and record the number of tests in each file to be deleted (count the lines per class); compute the expected new total = current total minus that sum.
- [ ] **Step 2:** Delete the files and fix the compile errors; the Rust parity tests keep reading `tests/parity/*.json` (do not delete those files).
- [ ] **Step 3:** `dotnet test tests/AcLayerStandardizer.Tests` on all three frameworks. Expected: the total equals the number computed in Step 1. Also `cargo test --manifest-path rust/Cargo.toml --workspace` and `.\build.ps1` (exit 0). Report the numbers.
- [ ] **Step 4:** Grep the connector for `System.Windows` and `.xaml` and report what remains (expected: only `RibbonSetup`, `EntryPoint` and `LsrCommandHandler`).
- [ ] **Step 5: Commit** `refactor: remove the WPF editor and the C# matching, categorizing and memory code`.

### Task 11: Remove the dead Rust pieces

**Files:** Delete `rust/crates/acad_layer_ffi/`; modify `rust/Cargo.toml` (workspace members), `rust/crates/acad_layer_ipc/src/lib.rs`, and any build script or document that names the ffi crate.

**Interfaces:**
- Removes: the `acad_layer_ffi` crate; in `acad_layer_ipc`: `IpcRequest::LoadStandard`, `IpcResponse::TemplateLoaded`, `load_standard`, and any other item with no remaining caller (check `ClassifyLayers`, `CategorizeLayers`, `Classification`, `Categorization`, `GetDrawingLayers`, `Layers` with a search across the workspace; remove an item only when nothing uses it).

- [ ] **Step 1:** Remove the items; the compiler and `cargo test --workspace` prove nothing else used them.
- [ ] **Step 2:** `cargo test --manifest-path rust/Cargo.toml --workspace` (report per-crate counts and the reason for any change), `cargo build --manifest-path rust/Cargo.toml --workspace` warning-free, `.\build.ps1` exit 0.
- [ ] **Step 3: Commit** `refactor(rust): remove the unused ffi crate and dead protocol pieces`.

---

## Stage 3: Tidy

### Task 12: Project file and build script

**Files:** Modify `src/AcLayerStandardizer/AcLayerStandardizer.csproj`, `build.ps1` (only if it names removed items).

**Interfaces:** Remove the `Nodify` package reference and any item that points at deleted files. Try removing `<UseWPF>true</UseWPF>`: if the build fails (expected, because the ribbon and the startup timer use WPF types), restore it and add a one-line comment that it stays for those two files.

- [ ] **Step 1:** Make the change; run `dotnet build` for all three target frameworks.
- [ ] **Step 2:** `dotnet test tests/AcLayerStandardizer.Tests` (total unchanged from Task 10) and `.\build.ps1`; confirm the installer no longer contains `Nodify.dll` (list the files the script copies) and report the installer size before and after.
- [ ] **Step 3: Commit** `build: drop Nodify and WPF-only project items`.

### Task 13: Version 1.4.0 and release notes (no release)

**Files:** Modify `build.ps1` (`$AppVersion = "BETA/1.4.0"`), `rust/crates/acad_layer_ui/Cargo.toml` and `rust/Cargo.lock` (version `1.4.0`), `dist/PackageContents.xml` (all four `Version="1.4.0"`). Create `docs/release-notes-beta-1.4.0.md`.

**Interfaces:** The release notes (plain language, same style as `docs/release-notes-beta-1.3.0.md`) cover: `LSR` and the ribbon now open the mapping window directly; the Settings panel (Standards File, Memory File with Import and Export); the beta notice; one editor only (the older fallback is gone, with a clear message if the window cannot start); the removed commands; `UndoStandardization` still available. Do NOT touch `docs/index.html`, create a tag, push or publish.

- [ ] **Step 1:** Make the edits.
- [ ] **Step 2:** `.\build.ps1` (output to a log file); confirm `AcLayerStandardizer_BETA-1.4.0.exe` exists, and `dotnet test` / `cargo test --workspace` totals are unchanged from Task 12.
- [ ] **Step 3: Commit** `release: prepare Beta 1.4.0 (not published)`.

### Task 14: Manual checklist, docs, final verification

**Files:** Create `docs/superpowers/plans/2026-10-07-phase4-manual-checklist.md`. Modify `docs/plan-1.3.md`.

**Interfaces:** The checklist is for a non-developer (plain language, the same format as `2026-10-06-phase3-manual-checklist.md`: backup first with an unmissable restore block, numbered steps with expected results and pass/fail boxes, a results table). Sections: A every entry point (`LSR`, `LSTDR`, `LAYERSTANDARDIZER`, `STD_Mappings`, ribbon button, menu item) opens the mapping window directly, with no welcome dialog; B the Settings button and panel (opens over the canvas, closes with its X); C Standards File label (name, "Unavailable:" in amber when missing, Change... works); D Memory File (Change... to a new path, Import... of a file with new and existing entries including the "Imported N mappings. M were new." text, Export... and re-import, a corrupt memory file is refused with a message and the old one stays); E the beta notice shows once per AutoCAD session as a banner, can be dismissed, does not return on a second open, and is always in Settings; F typing `LSR` again while the window is open brings it forward; G the failure message: with Chris's permission and a UAC prompt, rename `acad_layer_ui.exe` in one `Contents\R2x` folder, run `LSR`, expect the plain message with the folder, then rename it back (restore step cannot be skipped); H `ACLAYERSTD.UndoStandardization` after an Apply still restores the layers; I a short Phase 3 smoke check (drawing switch, close protection, quit protection); J the removed commands (`STD_Settings`, `ApplyStandardization`) report "Unknown command". Update `docs/plan-1.3.md`: Phase 4 status "implemented on main, awaiting manual verification (see the checklist)" and remove from the post-1.3 list the items this phase made obsolete.

- [ ] **Step 1:** Run the full verification in order: `cargo test --manifest-path rust/Cargo.toml --workspace`, `dotnet test tests/AcLayerStandardizer.Tests`, `.\build.ps1`. Report real totals and the installer's timestamp and size.
- [ ] **Step 2:** Write the checklist and the docs edits.
- [ ] **Step 3:** Re-read the checklist as Chris: every step doable, every expected result stated, restore steps present at every exit point.
- [ ] **Step 4: Commit** `docs: Phase 4 manual checklist and plan status`.
