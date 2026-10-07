# Phase 3: Live-sync and Unsaved-Mapping Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Rust mapping window follows the drawing active in AutoCAD, keeps unapplied connections per drawing, and asks Apply / Discard / Cancel before a window, drawing or AutoCAD close would lose them.

**Architecture:** The C# connector publishes a numbered event list (`EventFeed`) that the Rust window polls once a second (`PollEvents`), and the window reports its pending-connection counts on every poll. Close and quit decisions are made instantly by a pure `CloseGuard` from that last report; AutoCAD never waits for the window. Per-drawing state lives in a pure Rust `DrawingSessions`.

**Tech Stack:** C# (net48 / net8.0-windows / net10.0-windows, xunit), Rust (`serde`, `serde_json`, egui/eframe), named-pipe JSON protocol version 4.

**Spec:** `docs/superpowers/specs/2026-10-06-phase3-live-sync-design.md`. Builds on Phase 1 (`2026-10-06-phase1-thin-connector-contract.md`) and Phase 2 (`2026-10-06-phase2-rust-owns-data.md`). Two names differ from the spec text: the by-id layer read is `GetLayersForDrawing` (a `GetDrawingLayers` request already exists), and the connector gets a `ReplayClose` request (Task 7).

## Global Constraints

- Protocol version goes from 3 to 4; versions 2, 3 and 4 are accepted. Changes are additive; every existing request keeps working.
- Poll interval is 1 second, always; paused only while the window is minimized or hidden.
- The connector never blocks AutoCAD's thread waiting for the window. A close is allowed unless a window checked in within the last 5 seconds (`CloseGuard.CheckInFreshness`) and the drawing has pending connections.
- Any failure in the close decision allows the close.
- "Unapplied" means manual connections only (`MappingEditor.overrides`); automatic suggestions never count.
- Nothing is persisted across an AutoCAD restart.
- The WPF fallback window gets none of this. Version files stay at 1.2.4; do not tag or release.
- Events and the feed know nothing about layers beyond their payloads (the feed is meant to be reused).
- No GUI app is launched, window fronted, or input synthesized without asking Chris first (Task 9 needs this). Run `.\build.ps1` (not piped through `2>&1`) before each commit that ends a task group, and report real output.
- Parity goldens in `tests/parity/` are not touched.
- Commits end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. The once-a-second poll must not flood `%TEMP%\AcLayerStandardizer-ipc.log` (Task 2 tests that `PollEvents` is not logged).
2. Re-activation of the drawing already displayed must not wipe its connections (Task 6).
3. A layer deleted or renamed between polls: connections for vanished Source layers are dropped, case-insensitively (Task 4).
4. A window that crashed or hung must not make drawing close vetoes permanent (`CloseGuard` freshness, Task 7).
5. No drawing open when the window starts, or the last drawing closes while it is open (Task 6).

## File Structure

- `src/AcLayerStandardizer/Core/EventFeed.cs` (new): ring buffer of numbered events. Pure, no AutoCAD.
- `src/AcLayerStandardizer/Core/CloseGuard.cs` (new): `PendingRegistry` (last report) and `CloseGuard.ShouldVeto`. Pure.
- `src/AcLayerStandardizer/Core/IpcProtocol.cs`: version 4, `BuildEventsResponse`, pending parsing.
- `src/AcLayerStandardizer/Core/IpcBridgeServer.cs`: `PollEvents`, `GetLayersForDrawing`, `ReplayClose`.
- `src/AcLayerStandardizer/Core/ActiveDrawingTracker.cs`: publishes feed events; close veto; document lookup by id.
- `rust/crates/acad_layer_ipc/src/lib.rs`: new request/response types and `poll_events`, `get_layers_for_drawing`, `replay_close`.
- `rust/crates/acad_layer_ipc/src/feed.rs` (new): `FeedState`, `step`, `spawn_feed`.
- `rust/crates/acad_layer_ui/src/sessions.rs` (new): `EditState`, `DrawingSessions`, `drop_missing_sources`.
- `rust/crates/acad_layer_ui/src/mapping_editor.rs`: `take_edit_state`, `restore_edit_state`, `unapplied_count`, footer note line.
- `rust/crates/acad_layer_ui/src/main.rs`: feed wiring, switching, dialogs.
- Tests: `tests/AcLayerStandardizer.Tests/EventFeedTests.cs`, `CloseGuardTests.cs`, additions to `IpcBridgeServerTests.cs` and `IpcProtocolTests.cs`; Rust tests are inline `#[cfg(test)]` modules.

---

### Task 1: Protocol v4 and the C# event feed

**Files:** Create `src/AcLayerStandardizer/Core/EventFeed.cs`, `tests/AcLayerStandardizer.Tests/EventFeedTests.cs`. Modify `Core/IpcProtocol.cs`, `tests/.../IpcProtocolTests.cs`, and `rust/crates/acad_layer_ipc/src/lib.rs` (`IPC_PROTOCOL_VERSION`).

**Interfaces:**
- Produces:
  ```csharp
  public sealed record FeedEvent(long Seq, string Type, object Payload);
  public sealed record FeedPage(long Head, bool Reset, IReadOnlyList<FeedEvent> Events);
  public sealed class EventFeed {
      public const int DefaultCapacity = 200;
      public EventFeed(int capacity = DefaultCapacity, long baseSequence = 0);
      public static EventFeed Shared { get; }   // random per-process base, as ActiveDrawingRegistry
      public long Head { get; }
      public long Publish(string type, object payload);   // returns the new sequence number
      public FeedPage Read(long? since);
  }
  // IpcProtocol:
  public static string BuildEventsResponse(FeedPage page);  // {"type":"Events","payload":{"head":N,"reset":b,"events":[{"seq","type","payload"}]}}
  ```
- Read rules: `since == null` → `Reset=true`, no events, `Head`. `since == Head` → empty, no reset. `since` inside the retained window → the events after `since`, no reset. `since` older than the oldest retained event minus one, or greater than `Head` → `Reset=true`, no events.

- [ ] **Step 1: Write failing tests** in `EventFeedTests.cs`: `Read_with_no_since_asks_for_a_reset`; `Read_at_head_returns_nothing`; `Read_returns_only_newer_events_in_order`; `Capacity_drops_the_oldest_and_an_old_since_resets` (capacity 3, publish 5, `Read(since: first)` resets); `A_since_from_another_process_resets` (since > Head); `Sequence_numbers_start_from_the_base` (`new EventFeed(baseSequence: 1000)`, first published seq is 1001). In `IpcProtocolTests.cs`: `Version_4_is_supported_and_5_is_not` and `Events_response_has_head_reset_and_events` (parse the JSON).
- [ ] **Step 2:** Run `dotnet test tests/AcLayerStandardizer.Tests` and confirm the new tests fail to compile or fail.
- [ ] **Step 3:** Implement `EventFeed` (a lock around a `List<FeedEvent>` trimmed to capacity), set `IpcProtocol.CurrentVersion = 4`, add `BuildEventsResponse`; set `IPC_PROTOCOL_VERSION: u32 = 4` in `lib.rs` (check the existing Rust tests that assert the version and update them).
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests` and `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: all pass (C# count rises from 79).
- [ ] **Step 5: Commit** `feat: protocol v4 and the connector event feed`.

### Task 2: `PollEvents` request, feed publishing from the tracker

**Files:** Modify `Core/IpcBridgeServer.cs`, `Core/ActiveDrawingTracker.cs`, `tests/.../IpcBridgeServerTests.cs`.

**Interfaces:**
- Consumes: `EventFeed.Shared`, `IpcProtocol.BuildEventsResponse` (Task 1).
- Produces: the pipe request `{"type":"PollEvents","payload":{"protocol_version":4,"since":N|null,"pending":[{"drawing_id":"..","count":N}]}}` answered with `Events`. The `pending` field is parsed and ignored here; Task 7 uses it. Events published by the tracker: `DrawingActivated {drawing_id, display_name}`, `LayersChanged {drawing_id, fingerprint}`, `DrawingClosed {drawing_id}`.

- [ ] **Step 1: Write failing tests** in `IpcBridgeServerTests.cs` using the existing pipe-client pattern: `PollEvents_without_since_returns_a_reset`; `PollEvents_returns_events_published_after_since` (publish to `EventFeed.Shared`, poll with the previous head); `PollEvents_rejects_an_unsupported_version`; `PollEvents_is_not_written_to_the_ipc_log` (read the log file length before and after a poll).
- [ ] **Step 2:** Run them and confirm they fail.
- [ ] **Step 3:** Add the `"PollEvents"` case to `HandleMessageAsync` (version check, `EventFeed.Shared.Read(since)`), and skip the `Request received` / `Response sent` log lines when the type is `PollEvents` (in `ServerLoop`). In `ActiveDrawingTracker`, publish `DrawingActivated` from `OnDocumentActivated`, `LayersChanged` where `Refresh` publishes a new fingerprint (only when the fingerprint or id changed), and `DrawingClosed` from `OnDocumentToBeDestroyed`.
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests`. Expected: pass on all three TFMs.
- [ ] **Step 5: Commit** `feat: PollEvents request and tracker events`.

### Task 3: Rust IPC types, `poll_events`, and the feed client

**Files:** Modify `rust/crates/acad_layer_ipc/src/lib.rs`. Create `rust/crates/acad_layer_ipc/src/feed.rs` (and `mod feed; pub use feed::*;` in `lib.rs`).

**Interfaces:**
- Produces (in `lib.rs`):
  ```rust
  pub struct FeedEvent { pub seq: u64, #[serde(rename = "type")] pub kind: String, pub payload: serde_json::Value }
  pub struct PendingEntry { pub drawing_id: String, pub count: usize }
  // IpcRequest::PollEvents { protocol_version: u32, since: Option<u64>, pending: Vec<PendingEntry> }
  // IpcResponse::Events { head: u64, reset: bool, events: Vec<FeedEvent> }
  pub fn poll_events(since: Option<u64>, pending: Vec<PendingEntry>) -> Result<IpcResponse, String>; // 3 connection attempts, not 50
  ```
- Produces (in `feed.rs`):
  ```rust
  pub enum FeedMessage { Events(Vec<FeedEvent>), Resync, Down(String), Up }
  pub struct FeedState { pub since: Option<u64>, pub connected: bool }   // Default: since None, connected true
  pub fn step(state: &mut FeedState, outcome: Result<IpcResponse, String>) -> Vec<FeedMessage>;
  pub struct FeedHandle { /* stop, paused flags */ }
  impl FeedHandle { pub fn stop(&self); pub fn set_paused(&self, paused: bool); }
  pub fn spawn_feed<F: Fn(FeedMessage) + Send + 'static>(pending: std::sync::Arc<std::sync::Mutex<Vec<PendingEntry>>>, deliver: F) -> FeedHandle;
  ```
- `step` rules: `Events{reset:true, head}` → `since = Some(head)`, `[Resync]` (plus `Up` first if it was down). `Events{reset:false, head, events}` → `since = Some(head)`, `[Events(events)]` only if non-empty (plus `Up` first if it was down). `Err(e)` or `Ok(Error(e))` → `connected=false`, `[Down(e)]` only on the transition (no repeats). Other responses are ignored. `spawn_feed` loops: lock `pending`, call `poll_events(state.since, pending)`, `step`, deliver each message, sleep 1 second; skips polling while paused.

- [ ] **Step 1: Write failing tests** in `feed.rs`: `first_poll_reset_resyncs_and_records_head`; `new_events_are_delivered_and_since_advances`; `empty_events_deliver_nothing`; `a_failure_reports_down_once_and_recovery_reports_up_then_events`; and in `lib.rs`: `poll_events_request_shape` (serialized JSON has `type":"PollEvents"` and `payload.since`, `payload.pending`), `events_response_deserializes`.
- [ ] **Step 2:** Run `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ipc`; expect compile failures.
- [ ] **Step 3:** Implement the types, `poll_events` (reuse the private `request`, adding a variant that takes the attempt count), `step`, and `spawn_feed` (std thread, `AtomicBool` stop/paused).
- [ ] **Step 4:** Run `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: pass.
- [ ] **Step 5: Commit** `feat(rust): feed client and PollEvents types`.

### Task 4: Per-drawing sessions (pure Rust)

**Files:** Create `rust/crates/acad_layer_ui/src/sessions.rs` (`mod sessions;` in `main.rs`). Modify `mapping_editor.rs`.

**Interfaces:**
- Consumes: `acad_layer_ipc::PendingEntry` (Task 3).
- Produces:
  ```rust
  #[derive(Default, Clone)]
  pub struct EditState { pub overrides: HashMap<String, Option<String>>, pub undo: Vec<HashMap<String, Option<String>>>, pub redo: Vec<HashMap<String, Option<String>>> }
  impl EditState { pub fn pending_count(&self) -> usize }            // overrides.len()
  #[derive(Default)] pub struct DrawingSessions { /* id -> (name, EditState) */ }
  impl DrawingSessions {
      pub fn stash(&mut self, id: &str, name: &str, state: EditState);   // empty states are not kept
      pub fn take(&mut self, id: &str) -> EditState;                     // removes; default if unseen
      pub fn forget(&mut self, id: &str);
      pub fn pending_report(&self, current: Option<(&str, usize)>) -> Vec<PendingEntry>; // count > 0 only
      pub fn others_pending(&self, current_id: Option<&str>) -> Vec<(String, usize)>;    // (name, count), name-sorted
      pub fn footer_note(&self, current_id: Option<&str>) -> Option<String>;
  }
  pub fn drop_missing_sources(overrides: &mut HashMap<String, Option<String>>, source_layers: &[String]) -> usize;
  // MappingEditor:
  pub fn take_edit_state(&mut self) -> EditState;      // leaves defaults behind
  pub fn restore_edit_state(&mut self, state: EditState);
  pub fn unapplied_count(&self) -> usize;
  ```
- Footer text exactly: one other drawing → `"{name} has {n} unapplied connection(s)"` with "connection" singular when `n == 1`; several → `"{k} other drawings have unapplied connections"`; none → `None`.

- [ ] **Step 1: Write failing tests** in `sessions.rs`: `stash_then_take_restores_the_same_state`; `an_unseen_drawing_gets_a_fresh_state`; `empty_states_are_not_stashed`; `pending_report_includes_the_current_drawing_and_stashed_ones`; `footer_note_for_one_other_drawing` (`"Beds.dwg has 3 unapplied connections"` and the singular case); `footer_note_for_several_drawings` (`"2 other drawings have unapplied connections"`); `footer_note_is_none_when_only_the_current_drawing_is_pending`; `drop_missing_sources_is_case_insensitive_and_counts` (overrides for `LAYER1` and `Gone`, sources `["layer1"]` keeps the first, returns 1). In `mapping_editor.rs` tests: `edit_state_round_trips_through_the_editor`.
- [ ] **Step 2:** Run `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ui sessions`; expect failures.
- [ ] **Step 3:** Implement. `take_edit_state` uses `std::mem::take` on `overrides`, `undo_stack`, `redo_stack`.
- [ ] **Step 4:** Run `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: pass.
- [ ] **Step 5: Commit** `feat(rust): per-drawing edit sessions`.

### Task 5: `GetLayersForDrawing` (connector and Rust) and the snapshot following the displayed drawing

**Files:** Modify `Core/IpcBridgeServer.cs`, `Core/ActiveDrawingTracker.cs`, `Core/IpcProtocol.cs`, `rust/crates/acad_layer_ipc/src/lib.rs`, `tests/.../IpcBridgeServerTests.cs`.

**Interfaces:**
- Consumes: `GetActiveLayerNames(Database)` and `GetEmptyLayers(Database)` already used by `Commands/MappingsCommand.cs` (move them to a shared internal helper if they are private there).
- Produces:
  - Request `{"type":"GetLayersForDrawing","payload":{"protocol_version":4,"drawing_id":".."}}` → `{"type":"DrawingLayers","payload":{"drawing_id","drawing_name","source_layers":[..],"empty_layers":[..]}}`, or `Error("The drawing is no longer open.")`.
  - `ActiveDrawingTracker.TryFindDocument(string drawingId): Document?` (loop `Application.DocumentManager`, compare `GetDrawingId`).
  - Rust: `pub struct DrawingLayersInfo { drawing_id, drawing_name, source_layers: Vec<String>, empty_layers: Vec<String> }`, `IpcResponse::DrawingLayers(DrawingLayersInfo)`, `pub fn get_layers_for_drawing(drawing_id: String) -> Result<IpcResponse, String>`.
- Side effect required for Apply to stay correct: the connector's stored snapshot (`_drawingSnapshot`) is replaced by `current with { Document, DrawingId, DrawingName, SourceLayers, EmptyLayers }` for the drawing just read, so `ApplyPlan` and `PurgeEmptyLayers` operate on the displayed drawing. The standard-layer data in the snapshot is kept.

- [ ] **Step 1: Write failing tests:** Rust `get_layers_for_drawing_request_shape` and `drawing_layers_response_deserializes`; C# `GetLayersForDrawing_rejects_an_unsupported_version` and `GetLayersForDrawing_reports_an_unknown_drawing` (pipe test; no AutoCAD in tests, so an unknown id yields the "no longer open" error).
- [ ] **Step 2:** Run both suites; expect failures.
- [ ] **Step 3:** Implement the handler using `ExecuteInCommandContextAsync` as `GetStandardLayersAsync` does, with the snapshot replacement guarded the same way (only replace if `_drawingSnapshot` still equals the one read at the start, else return an error asking to retry).
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests` and `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: pass.
- [ ] **Step 5: Commit** `feat: read a drawing's layers by id; snapshot follows the displayed drawing`.

### Task 6: Window integration: live Source side

**Files:** Modify `rust/crates/acad_layer_ui/src/main.rs` (and `mapping_editor.rs` for the footer line).

**Interfaces:**
- Consumes: `spawn_feed`, `FeedMessage`, `FeedHandle`, `PendingEntry` (Task 3); `DrawingSessions`, `EditState`, `drop_missing_sources`, `MappingEditor::{take,restore}_edit_state/unapplied_count` (Task 4); `get_layers_for_drawing`, `get_active_drawing`.
- Produces on `LayerStandardizerApp`: fields `feed: Option<FeedHandle>`, `sessions: DrawingSessions`, `pending_report: Arc<Mutex<Vec<PendingEntry>>>`, `connection_lost: bool`, `refresh_wanted: bool`; a pure helper `fn plan_feed_events(current_id: &str, events: &[FeedEvent]) -> FeedPlan` where `FeedPlan { switch_to: Option<String>, refresh_current: bool, forget: Vec<String> }`.
- Behavior (from the spec): `DrawingActivated` for another id → stash the current drawing's `EditState` (`sessions.stash`), then request `get_layers_for_drawing(new_id)`; on `DrawingLayers` → set Source/empty layers, `restore_edit_state(sessions.take(id))`, `drop_missing_sources`, recompute matches. `LayersChanged` for the displayed drawing → request layers, reconcile overrides, status line "Layers changed in AutoCAD — list refreshed". `DrawingClosed` → `sessions.forget`; if it was the displayed drawing, show "No drawing is open" until the next activation. `Resync` → `get_active_drawing(None)` then layers for that id. `Down`/`Up` toggle `connection_lost` and the status line "AutoCAD connection lost". While `apply_pending` is true, set `refresh_wanted` instead of refreshing; refresh when it clears. Update `pending_report` every frame from `sessions.pending_report(Some((current_id, editor.unapplied_count())))`. The feed starts after the first snapshot arrives; `set_paused` follows the window's minimized/visible state. Footer line: the mapping editor shows `sessions.footer_note(...)` next to the status text. Apply stays disabled with no drawing.

- [ ] **Step 1: Write failing tests** for the pure helper in `main.rs`: `activation_of_another_drawing_switches`; `activation_of_the_displayed_drawing_does_not_switch_or_refresh` (Review Focus 2); `layers_changed_for_the_displayed_drawing_refreshes`; `layers_changed_for_another_drawing_is_ignored`; `closed_drawings_are_forgotten`; and in `sessions.rs` `switching_away_and_back_restores_connections_and_drops_deleted_layers` (stash, `take`, `drop_missing_sources`).
- [ ] **Step 2:** Run `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ui`; expect failures.
- [ ] **Step 3:** Implement `plan_feed_events` and the wiring above; add a no-drawing state (empty `drawing_id`, Apply disabled, "No drawing is open").
- [ ] **Step 4:** Run `cargo test --manifest-path rust/Cargo.toml --workspace`, then `.\build.ps1`. Expected: all pass (C# 79+ on each TFM).
- [ ] **Step 5: Commit** `feat(rust): window follows the active drawing with per-drawing connections`.

### Task 7: Connector close protection

**Files:** Create `Core/CloseGuard.cs`, `tests/.../CloseGuardTests.cs`. Modify `Core/IpcBridgeServer.cs`, `Core/ActiveDrawingTracker.cs`, `Core/IpcProtocol.cs`, `rust/crates/acad_layer_ipc/src/lib.rs`.

**Interfaces:**
- Produces:
  ```csharp
  public sealed record PendingDrawing(string DrawingId, int Count);
  public static class PendingRegistry {
      public static void Report(IReadOnlyList<PendingDrawing> pending, DateTime nowUtc);
      public static (IReadOnlyList<PendingDrawing> Pending, DateTime? LastCheckInUtc) Current { get; }
  }
  public static class CloseGuard {
      public static readonly TimeSpan CheckInFreshness = TimeSpan.FromSeconds(5);
      // drawingId == null means a quit: veto if ANY drawing has pending connections.
      public static bool ShouldVeto(IReadOnlyList<PendingDrawing> pending, DateTime? lastCheckInUtc, DateTime nowUtc, string? drawingId);
  }
  ```
  - `PollEvents` now calls `PendingRegistry.Report(parsed pending, DateTime.UtcNow)`.
  - New request `ReplayClose {protocol_version, kind: "drawing"|"quit", drawing_id, pending}` → updates `PendingRegistry`, then runs `_.CLOSE` (or `_.QUIT`) on the document with that id via `SendStringToExecute`, answered with `{"type":"Replayed"}` (or `Error` when the drawing is gone). Rust: `IpcRequest::ReplayClose {..}`, `IpcResponse::Replayed`, `pub fn replay_close(kind: String, drawing_id: String, pending: Vec<PendingEntry>) -> Result<IpcResponse, String>`.
  - `ActiveDrawingTracker` handles `Document.BeginDocumentClose` (subscribed in `Watch`): when `CloseGuard.ShouldVeto(...)` is true, call `args.Veto()` and publish `CloseBlocked {kind:"drawing", drawing_id}`. The whole handler is wrapped so any exception allows the close.

- [ ] **Step 1: Write failing tests** in `CloseGuardTests.cs`: `no_check_in_means_allow`; `a_stale_check_in_means_allow` (6 s old; Review Focus 4); `a_fresh_check_in_with_pending_for_that_drawing_vetoes`; `pending_in_another_drawing_does_not_veto_a_drawing_close`; `a_quit_vetoes_when_any_drawing_is_pending`; `zero_counts_are_ignored`. In `IpcBridgeServerTests.cs`: `PollEvents_records_the_pending_report` and `ReplayClose_reports_an_unknown_drawing`. Rust: `replay_close_request_shape`.
- [ ] **Step 2:** Run both suites; expect failures.
- [ ] **Step 3:** Implement as described. `ShouldVeto` is pure; the tracker passes `DateTime.UtcNow` and `PendingRegistry.Current`.
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests` and `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: pass.
- [ ] **Step 5: Commit** `feat: close protection in the connector`.

### Task 8: Window close dialogs

**Files:** Modify `rust/crates/acad_layer_ui/src/main.rs`, `rust/crates/acad_layer_ui/src/sessions.rs`.

**Interfaces:**
- Consumes: `replay_close` (Task 7), `CloseBlocked` events (`payload.kind`, `payload.drawing_id`), `DrawingSessions::others_pending`.
- Produces: `enum CloseChoice { Apply, Discard, Cancel }`; `struct CloseDialog { reason: CloseReason, others: Vec<(String, usize)> }` with `enum CloseReason { WindowClose, DrawingClose(String), Quit }`; pure `fn dialog_text(reason: &CloseReason, drawing_name: &str, count: usize, others: &[(String, usize)]) -> String` (names the drawing and count, and lists other drawings that would lose connections).
- Behavior: intercept the window's own close request (`ctx.input(|i| i.viewport().close_requested())`) when `unapplied_count() > 0` → `ViewportCommand::CancelClose` and show the dialog. A `CloseBlocked` event shows the same dialog for the displayed drawing. **Apply** = the existing plain Apply (`remember = false`); on `Applied` in blocked-close mode, call `replay_close` (with the cleared pending report) before the window closes; for a window-close dialog just close. **Discard** clears the drawing's `EditState` (and, for blocked close, calls `replay_close`, leaving the window open). **Cancel** only dismisses the dialog.

- [ ] **Step 1: Write failing tests:** `dialog_text_names_the_drawing_and_count`; `dialog_text_lists_other_drawings_that_would_lose_connections`; `a_window_close_with_nothing_pending_is_not_intercepted` (a pure `fn should_intercept_close(unapplied: usize) -> bool`).
- [ ] **Step 2:** Run `cargo test --manifest-path rust/Cargo.toml -p acad_layer_ui`; expect failures.
- [ ] **Step 3:** Implement the dialog (reuse the style of the existing error dialog), the interception, and the three choices.
- [ ] **Step 4:** Run `cargo test --manifest-path rust/Cargo.toml --workspace`, then `.\build.ps1`. Expected: all pass.
- [ ] **Step 5: Commit** `feat(rust): Apply / Discard / Cancel on window and drawing close`.

### Task 9: Quit handling (needs a live AutoCAD; ask Chris before launching anything)

**Files:** Modify `Core/ActiveDrawingTracker.cs` (or a new `Core/QuitGuard.cs`), `docs/superpowers/specs/2026-10-06-phase3-live-sync-design.md` (record the outcome).

**Interfaces:**
- Consumes: `CloseGuard.ShouldVeto(..., drawingId: null, ...)` (Task 7), the `CloseBlocked {kind:"quit"}` event and its dialog (Task 8).
- Produces: either a working quit veto, or the documented fallback (a `QuitRequested` event the window uses to tell the user what was lost, with no cancel).

- [ ] **Step 1:** Ask Chris for a go-ahead for ONE AutoCAD launch with a throwaway test build, stating that it tries (a) the COM `Application.BeginQuit` Cancel flag, then (b) vetoing `BeginDocumentClose` while AutoCAD is quitting, and reports what each did.
- [ ] **Step 2:** With the go-ahead, build the test, have Chris run it, and record which route cancels a quit (or neither) in the spec's "Quit" section.
- [ ] **Step 3:** Implement the working route behind `CloseGuard` with a test of the decision part, or the fallback if neither works (then ask Chris whether the fallback is acceptable before relying on it).
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests` and `cargo test --manifest-path rust/Cargo.toml --workspace`. Expected: pass.
- [ ] **Step 5: Commit** `feat: handle AutoCAD quit with unapplied connections`.

### Task 10: Full verification and the manual checklist

**Files:** Create `docs/superpowers/plans/2026-10-06-phase3-manual-checklist.md`. Modify `docs/plan-1.3.md` (mark Phase 3 status).

- [ ] **Step 1:** Run `.\build.ps1` (not piped). Expected: Rust all green, C# tests green on net48, net8.0 and net10.0, installer built. Report the real counts.
- [ ] **Step 2:** Write the checklist for Chris, each item with the expected result: switch between two drawings; edit a layer in the Layer Properties palette; connect layers, switch away and back; footer note appears and clears; close a drawing with unapplied connections (Apply, Discard, Cancel each once); close the window with unapplied connections; quit AutoCAD with unapplied connections (per Task 9's outcome); work with no drawing open; same filename in two folders; stop the window and confirm closing a drawing is not blocked.
- [ ] **Step 3:** Ask Chris to back up `%APPDATA%\AcLayerStandardizer` first, as for Phase 2, and to run the checklist; do not launch anything.
- [ ] **Step 4: Commit** `docs: Phase 3 manual checklist`.
