# Phase 1: Thin Connector Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Rust window ask AutoCAD "which drawing is active, and has it changed?" cheaply, and target Apply/Purge by a unique drawing id, without breaking any 1.2.x message.

**Architecture:** A C# `ActiveDrawingTracker` runs on AutoCAD's thread, listens to document events, and publishes an immutable `ActiveDrawingState` (session drawing id, display name, layer-name fingerprint, revision). The pipe thread only serves that state (it never touches AutoCAD APIs). The Rust `acad_layer_ipc` crate gains matching types and a `get_active_drawing` client call. Apply/Purge accept an optional `drawing_id`; name matching stays as the fallback.

**Tech Stack:** C# (net48 / net8.0-windows / net10.0-windows, LangVersion 12, xunit), Rust workspace (`serde`, `serde_json`), newline-delimited JSON over the named pipe `acad_layer_standardizer`.

**Spec:** `docs/plan-1.3.md`, section "Phase 1: Thin connector contract".

## Global Constraints

- Pipe name stays `acad_layer_standardizer`; messages are one JSON object per line, UTF-8 **without BOM**; `{"type": ..., "payload": ...}` envelope (unit variants omit `payload`).
- The pipe worker thread must never call AutoCAD APIs; only values published by the tracker or the existing `DrawingSnapshot` are read there.
- Existing messages (`Ping`, `GetDrawingLayers`, `GetDrawingSnapshot`, `ApplyPlan`, `PurgeEmptyLayers`, `LoadStandard`) keep working unchanged for protocol version 2 clients.
- `IPC_PROTOCOL_VERSION` / `IpcProtocol.CurrentVersion` becomes **3**; versions **2 and 3** are accepted.
- New JSON fields are optional on read (`#[serde(default)]` in Rust, absent-tolerant in C#).
- Tests run on every TFM via `dotnet test` (in `build.ps1`) and `cargo test -p acad_layer_ipc`.
- No change to what the UI shows in this phase.
- **Scope note:** the spec's slim "drawing layers" message (names, properties, empty layers without standard data) is deferred to Phase 2, where the snapshot actually shrinks; Phase 1 reuses `GetDrawingSnapshot` for layer data and adds only identity and change detection.

## Review Focus

- **Two open drawings with the same file name:** ids differ, and Apply carrying the other drawing's id is rejected. (Task 2, Task 3)
- **Old 1.2.x Rust exe against the new server (and the reverse):** no `drawing_id` / version 2 still applies; old snapshot JSON still parses. (Task 1, Task 2)
- **No drawing open, or the active drawing closed after the snapshot:** `NoActiveDrawing`, never a stale state or an exception. (Task 3)
- **Fingerprint stability:** same layer set in a different order or case gives the same fingerprint; system and xref layers don't affect it; adding a layer changes it. (Task 2)
- **Unsaved `Drawing1.dwg`:** still gets a unique id and a display name. (Task 3)

---

### Task 1: Rust IPC contract types and client call

**Files:**
- Modify: `rust/crates/acad_layer_ipc/src/lib.rs`
- Test: `rust/crates/acad_layer_ipc/src/lib.rs` (`#[cfg(test)] mod tests`)

**Interfaces:**
- Produces:
  - `IPC_PROTOCOL_VERSION: u32 = 3`
  - `DrawingSnapshot.drawing_id: String` (`#[serde(default)]`)
  - `pub struct ActiveDrawingInfo { pub drawing_id: String, pub display_name: String, pub layer_fingerprint: String, pub revision: u64 }`
  - `IpcRequest::GetActiveDrawing { known_revision: Option<u64> }`
  - `IpcResponse::{ActiveDrawing(ActiveDrawingInfo), ActiveDrawingUnchanged, NoActiveDrawing}`
  - `IpcRequest::ApplyPlan` and `PurgeEmptyLayers` gain `drawing_id: String` (`#[serde(default, skip_serializing_if = "String::is_empty")]`)
  - `pub fn get_active_drawing(known_revision: Option<u64>) -> Result<IpcResponse, String>` (Windows implementation plus the non-Windows stub); signatures become `apply_plan(drawing_name, drawing_id, mappings, remember, properties)` and `purge_empty_layers(drawing_name, drawing_id, layers)`.

- [ ] **Step 1: Write failing tests** in `mod tests`:
  - `snapshot_without_drawing_id_still_parses`: a 1.2.x snapshot JSON string (no `drawing_id`) deserializes with `drawing_id == ""`.
  - `get_active_drawing_serializes_known_revision`: `serde_json::to_string(&IpcRequest::GetActiveDrawing { known_revision: Some(7) })` equals `{"type":"GetActiveDrawing","payload":{"known_revision":7}}`.
  - `active_drawing_response_round_trips`: `{"type":"ActiveDrawing","payload":{"drawing_id":"doc-1","display_name":"A.dwg","layer_fingerprint":"ab12","revision":3}}` parses to `ActiveDrawingInfo` with those values; `{"type":"ActiveDrawingUnchanged"}` and `{"type":"NoActiveDrawing"}` parse to the unit variants.
  - `apply_plan_omits_empty_drawing_id`: serializing `ApplyPlan` with `drawing_id: String::new()` contains no `"drawing_id"` key; with `"doc-2"` it contains `"drawing_id":"doc-2"`.
- [ ] **Step 2:** Run `cargo test -p acad_layer_ipc`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the types and fields above in `lib.rs`, bump `IPC_PROTOCOL_VERSION`, and add `get_active_drawing` that calls the existing private `request(...)`.
- [ ] **Step 4:** Run `cargo test -p acad_layer_ipc`. Expected: PASS. Run `cargo check -p acad_layer_ui` and fix call sites by passing the current drawing id (`String::new()` until Task 4).
- [ ] **Step 5: Commit** `feat(ipc): add active-drawing contract types and client call`.

### Task 2: C# pure contract (state, fingerprint, protocol rules)

**Files:**
- Create: `src/AcLayerStandardizer/Core/ActiveDrawingState.cs`, `src/AcLayerStandardizer/Core/IpcProtocol.cs`
- Test: `tests/AcLayerStandardizer.Tests/IpcProtocolTests.cs`

**Interfaces:**
- Produces (namespace `AcLayerStandardizer.Core`):
  - `public sealed record ActiveDrawingState(string DrawingId, string DisplayName, string LayerFingerprint, long Revision)`
  - `public static class LayerFingerprint { public static string Compute(IEnumerable<string> layerNames); }` (drops names where `LayerHelper.ShouldSkip`, case-insensitive, order-independent, SHA-256 hex, first 16 chars)
  - `public static class IpcProtocol` with `CurrentVersion = 3`, `IsSupportedVersion(int)` (true for 2 and 3), `CheckDrawingTarget(string snapshotId, string snapshotName, string? requestId, string requestName)` returning an error string or `null`, and `BuildActiveDrawingResponse(ActiveDrawingState? state, long? knownRevision)` returning the JSON line for `ActiveDrawing`, `ActiveDrawingUnchanged` (when `knownRevision == state.Revision`) or `NoActiveDrawing` (state null).

- [ ] **Step 1: Write failing tests** (`IpcProtocolTests`):
  - `Fingerprint_ignores_order_and_case`: `Compute(["A-WALL","a-door"]) == Compute(["A-DOOR","a-wall"])`.
  - `Fingerprint_ignores_system_and_xref_layers`: adding `"Defpoints"` and `"X|LAYER"` leaves it unchanged.
  - `Fingerprint_changes_when_a_layer_is_added`.
  - `Supports_versions_2_and_3_only`: `IsSupportedVersion(2)`, `(3)` true; `(1)`, `(4)` false.
  - `Same_name_different_id_is_rejected`: `CheckDrawingTarget("doc-1","A.dwg","doc-2","A.dwg")` is non-null.
  - `Matching_id_is_accepted_even_if_names_differ`: `("doc-1","A.dwg","doc-1","a.dwg")` is null.
  - `Missing_id_falls_back_to_name`: `("doc-1","A.dwg",null,"A.DWG")` is null; `(…, null, "B.dwg")` is non-null.
  - `Active_drawing_response_shapes`: with a state and `knownRevision` null → `type == "ActiveDrawing"` and payload has `drawing_id`, `display_name`, `layer_fingerprint`, `revision`; with `knownRevision == state.Revision` → `"ActiveDrawingUnchanged"`; with null state → `"NoActiveDrawing"`.
- [ ] **Step 2:** Run `dotnet test tests/AcLayerStandardizer.Tests`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the types above. Use `System.Text.Json` to match `IpcBridgeServer`'s serialization style.
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests`. Expected: PASS on net48, net8.0-windows, net10.0-windows.
- [ ] **Step 5: Commit** `feat(ipc): add active-drawing state, fingerprint, and protocol rules`.

### Task 3: AutoCAD tracker and server handlers

**Files:**
- Create: `src/AcLayerStandardizer/Core/ActiveDrawingTracker.cs`
- Modify: `src/AcLayerStandardizer/Core/IpcBridgeServer.cs` (`HandleMessageAsync` switch; `DrawingSnapshot` record, `SetDrawingSnapshot`, `SerializeSnapshot`, `ApplyPlanAsync`, `PurgeEmptyLayersAsync`, `LoadStandardAsync` version checks), `src/AcLayerStandardizer/EntryPoint.cs` (`Initialize`, `Terminate`)
- Test: `tests/AcLayerStandardizer.Tests/IpcBridgeServerTests.cs`

**Interfaces:**
- Consumes: `ActiveDrawingState`, `LayerFingerprint.Compute`, `IpcProtocol` (Task 2).
- Produces: `public static class ActiveDrawingTracker { public static void Start(); public static void Stop(); public static string GetDrawingId(Document document); public static ActiveDrawingState? Current { get; } }`; wire request `{"type":"GetActiveDrawing","payload":{"known_revision":N|null}}`; `drawing_id` field on the snapshot payload and (optional) on ApplyPlan/PurgeEmptyLayers payloads.

- [ ] **Step 1: Write failing tests** in `IpcBridgeServerTests` (pipe client, as in `Server_responds_to_ping`):
  - `GetActiveDrawing_with_no_tracked_drawing_returns_NoActiveDrawing`: with no state published, response `type == "NoActiveDrawing"`.
  - `ApplyPlan_with_unsupported_version_is_rejected`: version `1` → `type == "Error"`.
  - Tracker state itself needs AutoCAD, so it is covered by the manual checklist in Task 4, not here.
- [ ] **Step 2:** Run `dotnet test tests/AcLayerStandardizer.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement:
  - `ActiveDrawingTracker`: `GetDrawingId` assigns `"doc-N"` from a monotonically increasing counter, stored in a `ConditionalWeakTable<Document, string>`. `Start()` subscribes `DocumentManager.DocumentActivated`, `DocumentCreated` (attach `Document.CommandEnded`), and `DocumentToBeDestroyed` (clear `Current` if it is the destroyed document, then refresh to the new active document); it also attaches to already-open documents and refreshes once. `Refresh(Document?)` reads layer names inside a transaction (same pattern as `GetActiveLayerNames` in `StandardizeCommand`), builds the state with `DisplayName = Path.GetFileName(doc.Name)`, and increments `Revision` only when drawing id or fingerprint changed. `Current` is published under a lock.
  - `IpcBridgeServer`: new `"GetActiveDrawing"` case returning `IpcProtocol.BuildActiveDrawingResponse(ActiveDrawingTracker.Current, known_revision)`; replace the `ApplyPlanProtocolVersion` equality checks with `IpcProtocol.IsSupportedVersion`; replies report `IpcProtocol.CurrentVersion`; the `DrawingSnapshot` record gains `DrawingId` (set from `ActiveDrawingTracker.GetDrawingId(document)` in `SetDrawingSnapshot`) and `SerializeSnapshot` emits `drawing_id`; Apply and Purge call `IpcProtocol.CheckDrawingTarget` using the optional `drawing_id` in the payload, returning its error text when non-null.
  - `EntryPoint`: call `ActiveDrawingTracker.Start()` after `IpcBridgeServer.Start()` in `Initialize` (inside the existing try/catch) and `ActiveDrawingTracker.Stop()` in `Terminate`.
- [ ] **Step 4:** Run `dotnet test tests/AcLayerStandardizer.Tests`. Expected: PASS on all three TFMs.
- [ ] **Step 5: Commit** `feat(ipc): serve active-drawing state and target Apply/Purge by drawing id`.

### Task 4: Rust UI passes the drawing id; verify end to end

**Files:**
- Modify: `rust/crates/acad_layer_ui/src/main.rs` (struct field, `set_snapshot`, the Apply and Purge call sites)
- Modify: `docs/plan-1.3.md` (Phase 1 status), `docs/rust-rearchitecture-plan.md` (one-paragraph protocol note)

**Interfaces:**
- Consumes: `DrawingSnapshot.drawing_id`, `apply_plan(..., drawing_id, ...)`, `purge_empty_layers(..., drawing_id, ...)` (Task 1).

- [ ] **Step 1:** Add `drawing_id: String` to `LayerStandardizerApp`, set it in `set_snapshot`, and pass `self.drawing_id.clone()` at the two IPC call sites.
- [ ] **Step 2:** Run `cargo test --workspace` and `cargo check -p acad_layer_ui`. Expected: PASS, no warnings introduced.
- [ ] **Step 3:** Run `.\build.ps1`. Expected: all .NET tests pass on net48, net8.0-windows and net10.0-windows; installer builds.
- [ ] **Step 4: Manual check in AutoCAD** (requires the user; do not launch GUI apps without asking). Install the build, open two drawings that share a file name from different folders, run `LSTDR` in one and press Apply with mappings: it must apply only in that drawing; switch to the other drawing and press Apply in the stale window: expect the "different drawing" error, not a change. Check `%TEMP%\AcLayerStandardizer-ipc.log` shows `GetActiveDrawing` requests answered.
- [ ] **Step 5:** Update the two docs and **commit** `docs: record Phase 1 contract and status`.
