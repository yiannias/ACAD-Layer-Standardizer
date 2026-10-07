# Phase 3 design: live-sync and unsaved-mapping protection

Status: draft for review. Roadmap context: `docs/plan-1.3.md`, Phase 3. Builds on the Phase 1 drawing identity and change token (`GetActiveDrawing`) and the Phase 2 Rust-owned data.

## Goal

While the Rust mapping window is open:

1. Its **Source** side follows the drawing that is active in AutoCAD, within about a second of a switch or a layer change. The **Target** (standard) side does not change.
2. Each drawing keeps its own unapplied connections, so switching away and back restores them.
3. Closing the window, closing a drawing, or quitting AutoCAD with unapplied connections asks the user: Apply, Discard, or Cancel.

Done when: switching drawings updates the Source side within about a second, per-drawing connections survive switching, and every close path (window, drawing, quit) prompts when connections are unapplied, or, for quit, degrades as described under "Quit".

## Decisions already made

| Question | Decision |
|---|---|
| Unapplied connections across an AutoCAD restart | No. Warn on close only; nothing is written to disk. |
| Switching away from a drawing with unapplied connections | Keep them per drawing, and show a footer note. Text only, no click-to-switch. |
| Poll rate | Once a second always, whether or not the window is focused. Pause only when the window is minimized or hidden. |
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

New request `GetDrawingLayers {protocol_version, drawing_id}` returns the drawing's name, Source layers, and empty layers. The connector reads them at a safe moment (`ExecuteInCommandContextAsync`), as `GetStandardLayers` does. If the drawing is not open it answers with a clear "drawing is no longer open" message. If AutoCAD is busy in a command, the window retries quietly on its next poll.

`ApplyPlan` and `PurgeEmptyLayers` already target a drawing by id and refuse a drawing that is not the active document. That guard stays; the window just always targets the drawing it is displaying. If that drawing is no longer active when Apply is clicked, the window shows a plain message, not a protocol error.

### 4. Window behavior

- The one-second poll runs whenever the window is open and not minimized or hidden.
- No refresh while an Apply, Purge or Load Standard is in flight; a change that arrives meanwhile is applied right after it finishes.
- Apply still closes the window afterwards, as today.
- No drawing open: the Source side shows "No drawing is open" and Apply is disabled; it recovers when a drawing is activated.
- Unsaved drawings such as `Drawing1` work because identity is the connector-assigned id, not the file name.
- The Target side does not change when drawings switch.

## Close protection

### Why AutoCAD never waits

An early idea was for the connector to hold a close request open and wait for the window to answer. That deadlocks: while AutoCAD's thread is stuck inside the close handler, it cannot run the window's Apply (which needs that thread). So the connector never waits for anything. It decides instantly from the last report.

### The rule

On every `PollEvents`, the window reports its pending counts. On a drawing close or a quit request the connector:

- allows it immediately if no window has checked in within the last 5 seconds, or if the drawing (or, for quit, every drawing) has nothing pending;
- otherwise cancels it (`Veto()` for a drawing close) and raises `CloseBlocked`.

The window picks up `CloseBlocked` within a second and shows the dialog for the affected drawing.

### The dialog

Apply, Discard, Cancel.

- **Apply** applies that drawing's connections (plain Apply, without "remember").
- **Discard** drops that drawing's pending connections.
- **Cancel** leaves everything as it is.

After Apply or Discard, the connector replays the user's original action (a normal `CLOSE` or `QUIT` command), so AutoCAD's own save prompt appears as usual. Nothing happens behind the user's back.

### Closing the window itself

If the displayed drawing has unapplied connections, the same dialog appears; no connector involvement. The text also lists other drawings whose pending connections would be lost. Apply applies the displayed drawing only.

### Quit

Closing a drawing is cancellable (`Document.BeginDocumentClose` plus `Veto()`, documented in Autodesk's .NET guide). Whether a quit can be cancelled from .NET is unconfirmed: the .NET `Application.BeginQuit` is documented without a cancel mechanism, forum reports say it has none while the older COM `BeginQuit` has a Cancel flag, and quitting closes each drawing in turn, so vetoing at that point might stop the quit. Plan:

1. A small, throwaway test inside AutoCAD (I will ask before launching anything) tries, in order: the COM `BeginQuit` cancel, then a veto of the per-drawing close during a quit.
2. Use whichever works, with the same rule and dialog as above.
3. If neither works, quit cannot be blocked. AutoCAD closes normally and the window tells the user which pending connections were lost. If this turns out to be the case, we revisit whether that is acceptable before relying on it.

## Errors

- Pipe down or request fails: the window keeps its state, shows "AutoCAD connection lost", and keeps retrying quietly. On reconnect it re-reads the active drawing.
- `reset` from the feed: re-read the active drawing, keep each drawing's pending connections, continue.
- AutoCAD busy in a command: quiet retry on the next poll.
- Any failure in the connector's close decision means the close is allowed. The warning is a courtesy and must never trap the user in AutoCAD.

## Build order and testing

Each step ends with all tests green before the next begins, and the whole suite is run (`.\build.ps1`) at the end of each.

1. **Event feed.** C# `EventFeed` (ring buffer, reset rules, sequence base) and the `PollEvents` request, tested like the existing `IpcBridgeServer` tests; Rust `FeedClient` and request/response types. Protocol version 4.
2. **Per-drawing sessions.** `DrawingSessions` in Rust: save/restore, pending counts, footer text, layer-change reconciliation. Pure logic, unit tests only.
3. **Integration.** `GetDrawingLayers`, the poll loop in the window, drawing switching, the no-drawing state, the footer note.
4. **Close protection.** The pending report, the connector's veto rule (a pure function: pending map, last check-in time, now), `Veto()` on drawing close and replay, the window-close dialog, and the quit handling per the test above.

Parts that need a live AutoCAD (switching drawings, editing layers in the Layer Properties palette, closing with pending work, quitting) are verified by Chris from a written checklist; nothing GUI is launched without asking first.

## Out of scope

- Keeping connections across an AutoCAD restart.
- Click-to-switch on the footer note.
- The WPF fallback window: it gets none of this and is removed in Phase 4.
- Releasing or tagging: version files stay at 1.2.4.
- Plug-ins other than the Standardizer using the feed (the feed is built general so they can; nothing else is built now).
