# Phase 2: Rust Owns Categorization, Memory, and Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Rust window loads config, the layer dictionary, translation memory, and categorization itself; the AutoCAD connector sends only what needs AutoCAD (drawing layers, standard-layer names read from a DWG) and applies approved changes.

**Architecture:** `acad_layer_core` gains a `config` module (it already has `categorization`, `memory`, `matching`). `acad_layer_ui` calls them directly. The connector's snapshot shrinks: no categories, hidden targets, or memory mappings; a new `GetStandardLayers` request returns standard-layer names for a template path while the connector caches the standard layers' *properties* for Apply. Remembered mappings are written by Rust after AutoCAD confirms an Apply.

**Tech Stack:** Rust (`serde`, `serde_json`), C# (net48 / net8.0-windows / net10.0-windows, xunit), shared golden JSON fixtures.

**Spec:** `docs/plan-1.3.md`, section "Phase 2: Move categorization, memory, and config to Rust". Builds on Phase 1 (`docs/superpowers/plans/2026-10-06-phase1-thin-connector-contract.md`).

## Global Constraints

- **C# categorization, memory, config, and the WPF editor are NOT deleted in this phase.** The WPF fallback, `LSR`, and `SettingsCommand` still use them until Phase 4; Phase 2 only stops the Rust path from depending on them.
- Config lives at `%APPDATA%\AcLayerStandardizer\config.json` (PascalCase keys: `TemplateDwgPath`, `MemoryFilePath`, `HeuristicThreshold`, `InstallRibbon`, `InstallMenu`); the translation memory at `%APPDATA%\AcLayerStandardizer\standards_memory.json` by default; layer dictionary at `%APPDATA%\AcLayerStandardizer\layer_dictionary.json` (shipped copy in `installer/assets/`).
- Memory file format is unchanged and cross-compatible: camelCase keys (`schemaVersion`, `lastModified`, `userIdentity`, `mappings`); a file written by 1.2.x must load, and a file written by Rust must load in the C# `MemoryStore`.
- Memory lookups are **case-insensitive** (C# uses `StringComparer.OrdinalIgnoreCase`); Rust must match.
- Rust must preserve config keys it does not own (e.g. `InstallRibbon`, `InstallMenu`) when saving.
- Protocol: additive only; version 3 clients and servers from Phase 1 keep working; new JSON fields optional on read.
- A corrupt or unreadable memory or config file must never be silently overwritten with an empty one by a save that did not first read it successfully.
- Tests run via `cargo test --workspace` and `dotnet test` (all three TFMs, in `build.ps1`).

## Review Focus

- **1.2.x memory file (including mixed-case source names) loads in Rust and matches case-insensitively**, and a Rust-saved file loads in C#. (Tasks 1, 2)
- **Saving config from Rust keeps `InstallRibbon` / `InstallMenu` / unknown keys.** (Task 1)
- **First run with no config and no template path:** the window opens, shows "No standard selected", and Choose Standard works; nothing panics or writes junk. (Tasks 1, 3)
- **Corrupt `standards_memory.json`:** the window starts with empty memory and does not overwrite the file until the user applies; the error is reported, not hidden. (Tasks 1, 4)
- **Apply succeeds in AutoCAD but memory save fails:** the user sees the warning, the Apply is not reported as failed. (Task 4)
- **Template path no longer exists on disk:** Rust offers Choose Standard instead of erroring at startup. (Task 3)

---

### Task 1: Rust `config` module and memory hardening

**Files:**
- Create: `rust/crates/acad_layer_core/src/config.rs`
- Modify: `rust/crates/acad_layer_core/src/lib.rs` (export), `rust/crates/acad_layer_core/src/memory.rs`
- Test: `rust/crates/acad_layer_core/src/config.rs`, `rust/crates/acad_layer_core/src/memory.rs` (`#[cfg(test)]`)

**Interfaces:**
- Produces:
  - `pub struct PluginConfig { pub template_dwg_path: String, pub memory_file_path: String, pub heuristic_threshold: f64, pub install_ribbon: bool, pub install_menu: bool, /* flattened unknown keys */ }` with `serde` PascalCase, defaults matching C# (`0.6`, `true`, `true`).
  - `pub fn config_dir() -> Option<PathBuf>` (`%APPDATA%\AcLayerStandardizer`), `PluginConfig::load_from(path) -> Result<PluginConfig, ConfigError>` distinguishing "file missing" (defaults) from "unreadable/corrupt" (error), `save_to(path) -> io::Result<()>`, `PluginConfig::effective_memory_path(&self, dir: &Path) -> PathBuf`.
  - `TranslationMemory::lookup(&self, source: &str) -> Option<&str>` (case-insensitive), `MemoryStore::load_checked(&self) -> Result<TranslationMemory, MemoryError>` (missing → empty; corrupt → error), `MemoryStore::save` writing atomically (temp file then rename).

- [ ] **Step 1: Write failing tests**
  - `config_reads_shipped_config_json`: loading `installer/assets/config.json` yields empty paths and default threshold `0.6`.
  - `config_round_trip_preserves_unknown_keys`: JSON with `InstallRibbon:false` and `"Extra":1` loads, saves, and the saved text still contains both.
  - `config_missing_file_gives_defaults` / `config_corrupt_file_is_an_error_not_defaults`.
  - `memory_lookup_is_case_insensitive`: mapping `"A-Wall"→"A-WALL"` is found by `"a-wall"`.
  - `memory_load_checked_reports_corruption`: invalid JSON → `Err`; missing file → empty memory.
  - `memory_save_is_atomic`: after save, no `*.tmp` sibling remains and the file parses.
  - `memory_reads_1_2_x_file`: a fixture saved by 1.2.x (`tests/fixtures/memory_1_2_x.json`, camelCase, mixed-case keys) loads with the expected mappings.
- [ ] **Step 2:** Run `cargo test -p acad_layer_core`. Expected: FAIL (items missing).
- [ ] **Step 3:** Implement the module and methods above; flatten unknown keys into a `serde_json::Map`.
- [ ] **Step 4:** Run `cargo test -p acad_layer_core`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): add config module, case-insensitive memory lookup, atomic memory save`.

### Task 2: Cross-language parity goldens

**Files:**
- Create: `tests/parity/categorization.golden.json`, `tests/parity/heuristic.golden.json`, `tests/parity/memory_roundtrip.golden.json`, `tests/AcLayerStandardizer.Tests/ParityTests.cs`, `rust/crates/acad_layer_core/tests/parity_tests.rs`
- Modify: none in production code.

**Interfaces:**
- Consumes: `LayerCategorizer.Classify`, `HeuristicMatcher`, `MemoryStore` (C#); `LayerCategorizer::classify`, `HeuristicMatcher`, `MemoryStore` (Rust).
- Produces: golden files that **both** languages assert against. Goldens are generated once from the C# implementation (the shipped behaviour).

- [ ] **Step 1:** Add a C# test `Generate_parity_goldens` that is skipped unless env `PARITY_GENERATE=1`, writing the three golden files from the real 325-layer fixture, a fixed list of (source, standard-set) heuristic cases, and a memory save of a mixed-case mapping set. Run it once with the variable set and commit the generated files.
- [ ] **Step 2: Write failing tests:** C# `Categorization_matches_golden`, `Heuristic_scores_match_golden`, `Memory_file_matches_golden` (C# output equals golden); Rust `categorization_matches_golden`, `heuristic_scores_match_golden`, `memory_file_matches_golden` (Rust output equals the same golden, comparing scores within `1e-9`).
- [ ] **Step 3:** Run both suites. Expected: C# PASS (pins current behaviour); Rust either PASS or FAIL. **Every Rust failure is a real parity bug:** fix it in Rust in this task (the C# output is the truth), do not edit the golden.
- [ ] **Step 4:** Run `cargo test -p acad_layer_core` and `dotnet test tests/AcLayerStandardizer.Tests`. Expected: PASS on all.
- [ ] **Step 5: Commit** `test(parity): pin categorization, heuristic, and memory behaviour across C# and Rust`.

### Task 3: Rust loads dictionary, memory, and categorization itself; `GetStandardLayers`

**Files:**
- Modify: `rust/crates/acad_layer_ipc/src/lib.rs`, `rust/crates/acad_layer_ui/src/main.rs`, `src/AcLayerStandardizer/Core/IpcBridgeServer.cs`, `src/AcLayerStandardizer/Commands/MappingsCommand.cs`
- Test: `rust/crates/acad_layer_ipc/src/lib.rs`, `rust/crates/acad_layer_ui/src/main.rs` (pure helper tests), `tests/AcLayerStandardizer.Tests/IpcBridgeServerTests.cs`

**Interfaces:**
- Consumes: `PluginConfig`, `MemoryStore::load_checked`, `LayerCategorizer::classify`, `LayerDictionaryDefinition` (Task 1/2).
- Produces:
  - `IpcRequest::GetStandardLayers { protocol_version: u32, path: String }`; `IpcResponse::StandardLayers(StandardLayersInfo { template_name: String, template_path: String, layers: Vec<String> })`; `pub fn get_standard_layers(path: String) -> Result<IpcResponse, String>`.
  - UI helper `fn build_target_view(standard: &[String], dictionary: &LayerDictionaryDefinition) -> TargetView { filters: Vec<TargetFilter>, always_hidden: Vec<String> }` (pure, unit-tested).
  - Connector: handler for `GetStandardLayers` that reads the template via `SideDatabase.LoadStandardLayers(path)`, caches `StandardLayerProperties` keyed by path, returns names only (sorted `"0"` first, then natural order, as today).
  - The existing snapshot fields for standards/categories/memory stay populated for old clients; the new Rust UI ignores them.

- [ ] **Step 1: Write failing tests**
  - Rust IPC: `get_standard_layers_request_shape` (JSON `{"type":"GetStandardLayers","payload":{"protocol_version":3,"path":"X.dwg"}}`) and `standard_layers_response_round_trips`.
  - UI: `target_view_matches_snapshot_filters_for_real_layers` (using the real-layer fixture; filter names and hidden set equal the golden from Task 2) and `target_view_with_no_dictionary_has_no_filters`.
  - C#: `GetStandardLayers_with_missing_file_returns_Error` (pipe test, no AutoCAD needed because the existence check precedes AutoCAD code, as in the Phase 1 `CheckProtocolVersion` pattern).
- [ ] **Step 2:** Run `cargo test --workspace` and `dotnet test tests/AcLayerStandardizer.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement the types and handler. In `main.rs`, on startup: load config; load memory with `load_checked` (corruption → status message, empty memory); load the dictionary (user copy under the config dir, else the shipped copy next to the exe); if `TemplateDwgPath` exists on disk call `get_standard_layers`, otherwise leave "No standard selected". Replace `set_snapshot`'s use of the snapshot's standards/filters/memory with the Rust-computed values. In `MappingsCommand`, when launching the Rust UI skip the template read, categorization, and memory load (the WPF fallback path keeps them).
- [ ] **Step 4:** Run the same suites. Expected: PASS.
- [ ] **Step 5: Commit** `feat: Rust loads config, dictionary, memory, and categorization itself`.

### Task 4: Rust owns template path, remembered mappings, and heuristic threshold

**Files:**
- Modify: `rust/crates/acad_layer_ui/src/main.rs`, `rust/crates/acad_layer_ipc/src/lib.rs`, `src/AcLayerStandardizer/Core/IpcBridgeServer.cs`
- Test: `rust/crates/acad_layer_ui/src/main.rs`, `tests/AcLayerStandardizer.Tests/IpcBridgeServerTests.cs`

**Interfaces:**
- Consumes: `PluginConfig::save_to`, `MemoryStore::save`, `TranslationMemory` (Task 1); `get_standard_layers` (Task 3).
- Produces: `fn remember_mappings(store: &MemoryStore, source_layers: &[String], mappings: &HashMap<String,String>) -> Result<(), MemoryError>` (same semantics as C# `SaveRememberedMappings`: mapped sources set, unmapped sources removed); `ApplyPlan.remember` is honoured by Rust, and the connector's `Applied.remembered` is `false` for requests that carry a new field `memory_handled_by_client: true`.

- [ ] **Step 1: Write failing tests**
  - `remember_mappings_sets_and_removes` (mapped source stored, previously remembered but now unmapped source removed, other sources untouched).
  - `remember_mappings_refuses_to_overwrite_corrupt_file`: corrupt file → `Err`, file bytes unchanged.
  - `applied_with_memory_failure_is_reported_as_warning_not_failure` (status text contains the warning and "Applied").
  - C#: `ApplyPlan_with_client_memory_flag_skips_memory_write` is not unit-testable without AutoCAD; covered by the manual step in Task 5 (state this in the commit message).
- [ ] **Step 2:** Run `cargo test --workspace`. Expected: FAIL.
- [ ] **Step 3:** Implement: after a successful `Applied`, Rust writes memory (if `remember`) and, when the user chooses a standard, saves `TemplateDwgPath` to config via `PluginConfig::save_to`; the connector stops writing config in `LoadStandardAsync` when the request carries `memory_handled_by_client`, and skips `SaveRememberedMappings` likewise. Heuristic threshold is read from Rust-loaded config instead of the snapshot.
- [ ] **Step 4:** Run `cargo test --workspace` and `dotnet test tests/AcLayerStandardizer.Tests`. Expected: PASS.
- [ ] **Step 5: Commit** `feat: Rust persists remembered mappings and the chosen standard`.

### Task 5: Verification and docs

**Files:**
- Modify: `docs/plan-1.3.md` (Phase 2 status), `docs/rust-rearchitecture-plan.md`

- [ ] **Step 1:** Run `.\build.ps1 -SkipInstaller`. Expected: Rust and all .NET tests pass on net48, net8.0-windows, net10.0-windows.
- [ ] **Step 2: Manual check in AutoCAD** (user-run; do not launch GUI apps without asking): (a) first run with no config; (b) existing 1.2.x memory file with mixed-case names, matches still appear and "Apply & Remember" updates it; (c) the C# `SETTINGS`/memory import-export still reads the file Rust wrote; (d) a corrupt memory file shows a message and is left intact; (e) config still has `InstallRibbon`/`InstallMenu` afterwards; (f) the WPF fallback (rename the Rust exe) still works.
- [ ] **Step 3:** Update the two docs and **commit** `docs: record Phase 2 status`.
