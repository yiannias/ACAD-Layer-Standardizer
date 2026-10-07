# Phase 3 design: live-sync and unsaved-mapping protection

Status: draft for review. Roadmap context: `docs/plan-1.3.md`, Phase 3. Builds on the Phase 1 drawing identity and change token (`GetActiveDrawing`) and the Phase 2 Rust-owned data.

## Goal

While the Rust mapping window is open:

1. Its **Source** side follows the drawing that is active in AutoCAD, within about a second of a switch or a layer change. The **Target** (standard) side does not change.
2. Each drawing keeps its own unapplied connections, so switching away and back restores them.
3. Closing the window, closing a drawing, or quitting AutoCAD with unapplied connections asks the user: Apply, Discard, or Cancel.

Done when: switching drawings updates the Source side within about a second, per-drawing connections survive switching, and every close path (window, drawing, quit) prompts when connections are unapplied.

## Decisions already made

| Question | Decision |
|---|---|
| Unapplied connections across an AutoCAD restart | No. Warn on close only; nothing is written to disk. |
| Switching away from a drawing with unapplied connections | Keep them per drawing, and show a footer note. Text only, no click-to-switch. |
| Poll rate | Once a second always, whether or not the window is focused. Pause only while the window is minimized AND no drawing has unapplied connections. |
| Architecture | A general-purpose polling event feed (approach A). Other plug-ins may reuse it later, so the feed knows nothing about layers or mapping. |

## Architecture

Rust remains the pipe client and the C# connector the pipe server (newline-delimited JSON on `acad_layer_standardizer`). Protocol version goes from 3 to 4; versions 2, 3 and 4 are accepted. Everything is additive: `GetActiveDrawing`, `GetDrawingSnapshot`, `GetStandardLayers`, `ApplyPlan` and `PurgeEmptyLayers` keep working.

### 1. The event feed (general, no mapping knowledge)

**Connector side** (`Core/`, AutoCAD-free where possible):

- An `EventFeed` keeps the last 200 events, each with a sequence number. Sequence numbers start from a random per-process base (as `ActiveDrawingRegistry` revisions do), so a number from another AutoCAD process never looks valid.
- Event types in this phase: `DrawingActivated {drawing_id, display_name}`, `LayersChanged {drawing_id, fingerprint}`, `DrawingClosed {drawing_id}`, `CloseBlocked {kind: "drawing" | "quit", drawing_id?}`.
- `ActiveDrawingTracker` (already watching document and layer-table events) publishes these into the feed.
- New request `PollEvents {protocol_version, since, pending}` returns `Events {head, events, reset}`.
  - `since` is the last sequence number the client saw (or absent on the first call).
  - `reset: true` means the client's `since` is unknown (older than the buffer, or from another process): the client must re-read the active drawing and continue from `head`.
  - `pending` is the client's report of unapplied connections: `[{drawing_id, count}]`. Its use is described under "Close protection". Every `PollEvents` call also counts as a check-in.

**Rust side** (a separate module in `acad_layer_ipc`, no UI types):

- A `FeedClient` that holds `since`, issues `PollEvents` once a second on a worker thread, and delivers events to the UI thread over the existing channel plus a repaint request.
- Connection failures are reported as a status, not an error dialog (see "Errors").

The feed has no layer or mapping vocabulary beyond the event payloads above. A future plug-in subscribes to the types it needs.

### 2. Per-drawing state in the window (pure logic, fully unit-testable)

A `DrawingSessions` structure in the Rust window, independent of egui:

- Keyed by `drawing_id`; each entry holds the display name, Source layers, empty layers, the manual connections (`overrides`), and the undo and redo stacks.
- **Unapplied** means connections the user made or removed by hand. Automatic suggestions never count, so opening and closing the window does not warn.
- Switching: save the current drawing's entry, load the new drawing's entry, or create a fresh one (automatic suggestions plus memory, as at launch). The Target side, zoom and pan are untouched.
- Layers changed in the displayed drawing: replace the Source list, recompute matches, keep connections whose Source layer still exists, drop the rest, and set the status line to say the list was refreshed.
- A drawing that closes (`DrawingClosed`) has its entry discarded.
- Footer note: one other drawing with pending connections gives "Beds.dwg has 3 unapplied connections"; several give "2 other drawings have unapplied connections". Nothing is shown when no other drawing has any.

### 3. Reading a drawing's layers by id

New request `GetLayersForDrawing {protocol_version, drawing_id}` (the name `GetDrawingLayers` is already taken by an older request) returns the drawing's name, Source layers, and empty layers. The connector reads them at a safe moment (`ExecuteInCommandContextAsync`), as `GetStandardLayers` does. If the drawing is not open it answers with a clear "drawing is no longer open" message. If AutoCAD is busy in a command, the window retries quietly on its next poll.

`ApplyPlan` and `PurgeEmptyLayers` already target a drawing by id and refuse a drawing that is not the active document. That guard stays; the window just always targets the drawing it is displaying. If that drawing is no longer active when Apply is clicked, the window shows a plain message, not a protocol error.

### 4. Window behavior

- The one-second poll runs whenever the window is open. It pauses only while the window is minimized AND no drawing (displayed or stashed) has unapplied connections; a minimized window with pending connections keeps polling, follows the active drawing and still protects closes.
- One mapping window per AutoCAD session: typing LSTDR again while it is open opens nothing; it restores the window if minimized, brings it to the front and prints "The Layer Standardizer window is already open; it follows the active drawing." on the command line.
- No refresh while an Apply, Purge or Load Standard is in flight; a change that arrives meanwhile is applied right after it finishes.
- Apply still closes the window afterwards, as today, unless other drawings still have unapplied connections: then the window stays open and the status line says "Applied N mappings. Other drawings still have unapplied connections, so this window stays open."
- No drawing open: the Source side shows "No drawing is open" and Apply is disabled; it recovers when a drawing is activated.
- Unsaved drawings such as `Drawing1` work because identity is the connector-assigned id, not the file name.
- The Target side does not change when drawings switch.

## Close protection

### Why AutoCAD never waits

An early idea was for the connector to hold a close request open and wait for the window to answer. That deadlocks: while AutoCAD's thread is stuck inside the close handler, it cannot run the window's Apply (which needs that thread). So the connector never waits for anything. It decides instantly from the last report.

### The rule

On every `PollEvents`, the window reports its pending counts. On a drawing close or a quit request the connector:

- allows it immediately if no window has checked in within the last 5 seconds, or if the drawing (or, for quit, every drawing) has nothing pending;
- otherwise cancels it (`Veto()` of the drawing close, which for a quit is the first drawing closed) and raises `CloseBlocked`.

The window picks up `CloseBlocked` within a second and shows the dialog for the affected drawing.

### The dialog

Apply, Discard, Cancel.

- **Apply** applies that drawing's connections (plain Apply, without "remember").
- **Discard** drops that drawing's pending connections.
- **Cancel** leaves everything as it is.

After Apply or Discard, the window asks the connector to replay the user's original action (new request `ReplayClose {kind, drawing_id, pending}`, which runs a normal `CLOSE` or `QUIT` command), so AutoCAD's own save prompt appears as usual. Nothing happens behind the user's back.

### Closing the window itself

If ANY drawing (displayed or stashed) has unapplied connections, the same dialog appears; no connector involvement. The text also lists other drawings whose pending connections would be lost. Apply applies the displayed drawing only. When only other drawings have unapplied connections, the dialog names them and does not offer Apply; it offers Discard and Cancel.

### Quit

Measured, not assumed: a probe was run in AutoCAD 2027 on 2026-10-07 (log: `.superpowers/sdd/2026-10-06-phase3-live-sync/quitprobe-result.log`). When the user types QUIT, AutoCAD closes the open drawings one at a time first (`Document.BeginDocumentClose` fires per drawing). The application-level `BeginQuit` (COM and .NET) and `QuitWillStart` fire only after all drawings are closed, so they are too late to cancel anything. Vetoing `BeginDocumentClose` during a QUIT aborted the whole quit (AutoCAD stayed open), so a quit can be blocked by the same per-drawing veto used for CLOSE.

Rule implemented:

- In `BeginDocumentClose` the connector reads `Document.CommandInProgress`; `CloseGuard.ClassifyClose` treats `QUIT` and `EXIT` (ignoring case, whitespace and a leading `_`, `.` or `'`) as a quit and anything else as a drawing close.
- For a quit it vetoes at the first drawing closed if any drawing has pending connections (so no earlier drawing is closed before the veto), and publishes `CloseBlocked {kind: "quit", drawing_id: <the drawing being closed>}`.
- After Apply or Discard, `ReplayClose` with kind `quit` re-issues `QUIT`.
- Any failure reading the command or deciding means the close is allowed; if detection fails the behavior degrades to a drawing close and the user re-issues QUIT.

Not yet verified: the QUIT detection itself (that `CommandInProgress` reads QUIT during the per-drawing close) has not been seen in a live AutoCAD; it is on the manual checklist.

## Errors

- Pipe down or request fails: the window keeps its state, shows "AutoCAD connection lost" only after 3 failed polls in a row, and keeps retrying quietly. On reconnect it does not re-read by itself: it catches up through the event list, and a `reset` (for example after a connector restart) re-reads the active drawing.
- `reset` from the feed: re-read the active drawing, keep each drawing's pending connections, continue.
- AutoCAD busy in a command: quiet retry on the next poll.
- Any failure in the connector's close decision means the close is allowed. The warning is a courtesy and must never trap the user in AutoCAD.

## Build order and testing

Each step ends with all tests green before the next begins, and the whole suite is run (`.\build.ps1`) at the end of each.

1. **Event feed.** C# `EventFeed` (ring buffer, reset rules, sequence base) and the `PollEvents` request, tested like the existing `IpcBridgeServer` tests; Rust `FeedClient` and request/response types. Protocol version 4.
2. **Per-drawing sessions.** `DrawingSessions` in Rust: save/restore, pending counts, footer text, layer-change reconciliation. Pure logic, unit tests only.
3. **Integration.** `GetDrawingLayers`, the poll loop in the window, drawing switching, the no-drawing state, the footer note.
4. **Close protection.** The pending report, the connector's veto rule (a pure function: pending map, last check-in time, now), `Veto()` on drawing close and replay, the window-close dialog, and quit handling (detected from `CommandInProgress`, per "Quit").

Parts that need a live AutoCAD (switching drawings, editing layers in the Layer Properties palette, closing with pending work, quitting) are verified by Chris from a written checklist; nothing GUI is launched without asking first.

## Out of scope

- Keeping connections across an AutoCAD restart.
- Click-to-switch on the footer note.
- The WPF fallback window: it gets none of this and is removed in Phase 4.
- Releasing or tagging: version files stay at 1.2.4.
- Plug-ins other than the Standardizer using the feed (the feed is built general so they can; nothing else is built now).
