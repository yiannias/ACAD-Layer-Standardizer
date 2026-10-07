# Rust Re-architecture: Goals and Execution Plan

**Status:** Draft for review
**Working branch:** `feature/rust-core`
**Last reviewed against repository:** 2026-10-01

## Purpose

Explore moving as much reusable application logic and companion UI as practical into Rust, while retaining a reliable AutoCAD integration layer. This branch is an architectural experiment; it must not destabilize the released C# plugin or imply that the Rust path is production-ready before its integration and packaging are proven.

The effort also tests whether capabilities and UI patterns from Spatial Drawing Board, Spatial Sketchpad, and Spatial UI Kit can be reused around AutoCAD. Those projects remain independent applications; this project should depend on their shared UI kit only where it provides a concrete benefit.

## Goals

1. **Keep AutoCAD access safe and narrow.** AutoCAD drawing/database operations remain in code loaded by `acad.exe`. The .NET adapter owns document access, transactions, and applying user-approved changes.
2. **Move reusable domain logic to Rust.** Matching, categorization, and translation-memory behavior should live in a pure Rust core that has no AutoCAD API dependency.
3. **Keep standards data-driven.** Company or template-specific layer names and rules must come from user-selected standards/configuration or test fixtures, never from production matching logic.
4. **Preserve current behavior during migration.** Keep the managed C# implementation available as a fallback until Rust parity is demonstrated for representative cases and supported .NET targets.
5. **Preserve the familiar user flow.** The user runs the AutoCAD command and gets one layer-matching window. The new Spatial UI Kit interface should replace the current mapping window; it must not appear alongside the old mapping window as an extra companion.
6. **Choose integration modes to fit that flow.** Use in-process C ABI/P/Invoke if the existing .NET UI calls Rust logic. Use local IPC if AutoCAD launches a Rust UI process that replaces the old UI and needs drawing data or must submit an approved plan. Avoid adding both bridges without a demonstrated need.
7. **Make changes reviewable and reversible.** Build and package the existing plugin without requiring the companion UI. Do not silently rename, merge, or otherwise modify drawing layers.

## Architecture

```text
AutoCAD process
  Existing AutoCAD .NET plugin (net48 / net8 / net10)
    - remains the command entry point and owns AutoCAD API/database access
    - launches the replacement mapping UI when its command is invoked
    - exchanges drawing data and approved plans over a narrow local IPC protocol

One visible mapping window
  Rust app + Spatial UI Kit
    - replaces the current WPF mapping window; do not show both
    - displays and edits a proposed mapping plan
    - sends the plan back only after explicit user approval
    - calls acad_layer_core directly for matching/categorization

Shared Rust crates
  acad_layer_core: domain algorithms, configuration models, memory
  acad_layer_ffi: (removed in Phase 4 because nothing consumed it)
  acad_layer_ipc: shared request/response schema and transport support
  acad_layer_ui: optional companion application
```

For the preferred full Rust UI path, IPC is the likely first bridge: the Rust UI can call the Rust core directly, while the .NET plugin remains the AutoCAD adapter. P/Invoke is useful if we retain a .NET-hosted UI or need AutoCAD commands to invoke Rust calculations in-process. Do not assume both are required.

## User experience requirement

The plugin is **AutoCAD-hosted only**. The user already has an active AutoCAD session with at least one drawing loaded, then invokes `LSR` or the equivalent menu/ribbon command. Only then does the layer-matching UI appear. The Rust UI must never start with Windows, at AutoCAD startup, or as an independent companion workflow.

The target experience is: invoke the familiar AutoCAD command, see one polished layer-matching window in the SDB/SSP visual style, complete the same matching workflow, and return to AutoCAD. The old WPF mapping window must not open underneath or alongside the replacement. A separate Rust process is acceptable only as an implementation detail for that one command-launched mapping window (owned/associated with AutoCAD, positioned and activated appropriately, and closed cleanly); it must not feel like an unrelated extra application.

Current source routes `LSR` through a .NET welcome dialog; after the user selects Open Mappings, that dialog closes and the mappings editor opens. The user is comfortable keeping that first notification/setup window in .NET if useful. The Rust/Spatial UI Kit window should replace the mapping editor on both paths: after the welcome dialog closes for `LSR`, and directly for `LSTDR`. Menu/ribbon entry points should preserve their current command routing. Never show the WPF mappings editor at the same time as the Rust replacement.

## Early hosting feasibility assessment

The local Spatial UI Kit is a library of reusable `egui` UI components; it is not itself a windowing toolkit or a WPF control. Its manifest depends on `egui` 0.35. The current Rust prototype uses `eframe::run_native`, which creates a native desktop app window. The existing mapping editor is a WPF `Window`.

This means direct reuse of Spatial UI Kit inside the existing WPF window is not turnkey. It would require a custom egui input/rendering host inside a WPF surface. The lower-risk route to the desired one-window experience is for the AutoCAD command to launch the Rust/eframe mapping UI as the sole replacement window, with IPC to the .NET plugin. That is architecturally plausible, but window ownership, focus/modality, shutdown, and AutoCAD document-context dispatch still need a small proof of concept. A true in-process WPF embed remains an alternative only if the replacement-window route fails the polish requirement.

An initial headless source/build spike now accepts an optional `--owner-hwnd=<decimal HWND>` argument, creates the eframe root window hidden, attaches it as an owned top-level window on first frame, and then reveals it. `cargo check --manifest-path rust/Cargo.toml -p acad_layer_ui` passed with this code. This establishes that the selected libraries expose enough hooks to compile an owned-window path; it does not prove runtime ownership, focus/modality, startup visibility, or visual polish.

No AutoCAD or GUI application has been launched to validate visible behavior. Source inspection and compilation cannot establish that the replacement window feels native or behaves correctly with AutoCAD focus and modality.

## Scope and boundaries

### Included

- Port and verify matching, categorization, and translation-memory behavior.
- A stable, versioned Rust-to-.NET interface with explicit ownership/error handling.
- An optional companion UI and a narrow, versioned local IPC protocol.
- Build, test, and package changes needed to ship the native library with each supported plugin payload.

### Not included in this experiment

- Replacing AutoCAD's .NET API or moving database access into Rust.
- Rewriting every existing WPF screen before the engine and bridge are validated.
- Automatically applying mappings, changing standards, or deleting/merging layers without a review step.
- Making Spatial Drawing Board or Spatial Sketchpad depend on this plugin.

## Milestones and acceptance criteria

### 0. Prove the one-window hosting experience first

- Build the smallest headless prototype that starts the Rust/eframe window from a host process using an AutoCAD-style owner handle and shuts it down cleanly.
- Determine whether eframe/winit can provide the required owner, modality, focus, and placement through supported hooks, or whether a narrow Win32 integration is needed.
- Compare this with the cost of embedding egui rendering inside the existing WPF mapping window.
- Before any GUI/AutoCAD run, define the exact batch and obtain approval under the workspace screen-ownership rule.

**Done when:** the approach is demonstrated in an approved GUI batch with only one visible mapping window and acceptable activation, focus, close, and reopen behavior. If it cannot meet that bar, stop and reassess the embedding approach before migrating more UI.

### 1. Record baseline and preserve the current plugin

- Capture current C# behavior with existing tests and representative fixtures.
- Identify the precise AutoCAD runtime targets and release/package layouts.
- Keep this work isolated on `feature/rust-core`; do not merge or publish the experiment as a release by default.

**Done when:** the baseline is documented, current C# tests/build are reproducible, and the branch can still produce the existing plugin package without Rust installed (unless the team explicitly chooses otherwise).

### 2. Stabilize the Rust core and parity contract

- Keep `acad_layer_core` independent of AutoCAD and production-specific layer lists.
- Load categorization rules from the shipped/user-selected dictionary schema.
- Define expected behavior for matching, categorization, memory precedence, errors, and serialization.
- Run Rust tests against external fixtures and differential tests against the current C# implementation.

**Done when:** representative inputs produce agreed results; edge cases and intentional behavior differences are documented; all layer-name examples live in test fixtures rather than production algorithms.

### 3. Complete the bridge needed by the chosen UI host

- For a Rust replacement UI, define/version the IPC data contract and launch/close lifecycle; keep AutoCAD API calls on a valid AutoCAD document/command context.
- If the existing .NET UI or commands still need the Rust core, define/document ABI versioning, UTF-8/error/memory rules, build the Windows x64 DLL, package it, and make its use opt-in with a C# fallback.
- Do not package or require `acad_layer_ffi.dll` if the chosen architecture does not call it.

**Done when:** the selected bridge supports the one-window flow, clean packaging, error handling, and testable fallbacks; all three .NET runtime targets remain supported.

### 4. Complete the AutoCAD-side IPC service safely

- Version the protocol and define connection, timeout, malformed-request, and disconnect behavior.
- Support reading the active drawing's eligible layers and returning a proposed plan result.
- Marshal every AutoCAD API operation into a valid AutoCAD document/command context; a background pipe worker must not directly access the database just because it holds a document lock.
- Validate requested layer names and operations; execute approved changes in a transaction with a clear rollback/error response.
- Restrict the local endpoint to the current user/session as appropriate.

**Done when:** protocol tests cover request/response and failures, plus a separately approved AutoCAD integration check verifies read and apply behavior without affecting unrelated drawings or layers.

### 5. Replace the mapping UI without changing the workflow

- Connect/disconnect cleanly and identify which AutoCAD drawing supplied the data.
- Load/select the target standard independently of source drawing layers.
- Show proposed mappings, unmatched items, confidence, and a preview of every change.
- Require an explicit Apply action; report per-operation success/failure and refresh from AutoCAD afterward.

**Done when:** UI and protocol behavior can be tested headlessly where possible, then visually and inside AutoCAD only with user approval for the GUI test batch.

### 6. Evaluate adoption and decide the product direction

- Compare reliability, startup/load time, installer size, support burden, and maintenance across the three AutoCAD .NET eras.
- Decide whether to keep the Rust engine optional, make it the default, keep the companion separate, or stop the experiment.
- Update user/developer documentation and release notes only after that decision.

**Done when:** the decision and evidence are recorded. No production migration or release is implied before this review.

## Verification gates

- Rust workspace formatting, build, and tests pass.
- .NET build and tests pass for net48, net8, and net10.
- Differential tests cover current behavior and known edge cases.
- Bundle/installer inspection confirms native dependency placement and absence of development-only files.
- IPC tests exercise protocol compatibility, bad input, timeouts, cancellation, and client disconnects.
- AutoCAD database behavior is validated only in an explicitly approved GUI batch; headless tests do not count as AutoCAD integration proof.

## Current branch snapshot

The branch contains the Rust workspace bootstrap, fixture/data cleanup, and C# P/Invoke bridge commits, plus work-in-progress snapshot IPC and a Rust mapping editor. The current implementation captures a drawing snapshot during `LSTDR`'s valid AutoCAD command context, serves the immutable snapshot from a named-pipe worker, and launches the Rust UI in Debug builds with an optional AutoCAD owner HWND. The Rust UI requests and displays the snapshot and has mapping/filter controls.

The FFI wrapper has no production call sites, and the normal `build.ps1` packaging path is not wired to build/package the Rust native library. Apply controls in the Rust UI are disabled; the IPC server does not handle `ApplyPlan`. The launcher falls back to the existing WPF editor if the Rust executable is unavailable or launch fails. The owned-window path, AutoCAD focus/modality, shutdown, and full user flow remain unverified in a GUI session. Tests and builds for this handoff have not been run.

## Decisions to confirm in review

1. **Window hosting:** Can the Rust UI launch as the single replacement mapping window with acceptable owner/focus/modality behavior, or must the egui UI render inside a WPF host? The user accepts retaining the .NET welcome/notification window if it appears sequentially before the Rust mapping editor.
2. **Bridge choice:** If the UI is a Rust process and matching also runs there, IPC may be sufficient; reserve P/Invoke for remaining .NET-hosted operations.
3. **Apply semantics:** Should the companion eventually apply layer renames only, or also merge/reassign entities and remove empty layers? These operations have different risk and transaction requirements.
4. **Migration threshold:** What parity/evidence is sufficient before any Rust implementation becomes the default for existing plugin commands?
5. **Shared UI kit boundary:** Which Spatial UI Kit components are required for the first mapping window? Avoid coupling to the full Spatial Drawing Board or Sketchpad applications unless a specific capability requires it.

## Immediate next action after review

First run the owned-window feasibility spike in a specifically approved GUI batch; workspace screen-ownership instructions require asking and waiting before launching AutoCAD or the Rust UI, and approval covers one stated batch only. If it passes, implement the UI's manual override/Apply flow through a versioned IPC request that marshals drawing changes to a valid AutoCAD command context. If it fails, investigate an egui-in-WPF host before a broad UI rewrite. Then lock the existing plugin baseline and port the core with behavior parity.

## IPC protocol note (protocol version 3)

The Rust app and the AutoCAD connector exchange one JSON object per line over the named pipe `acad_layer_standardizer`. Besides the snapshot, apply, purge, and load-standard messages, `GetActiveDrawing { known_revision }` returns the active drawing's session `drawing_id`, display name, and a layer fingerprint with a revision counter (`ActiveDrawing`), `ActiveDrawingUnchanged` when the revision matches, or `NoActiveDrawing`. Apply and Purge may carry `drawing_id`; when present it takes precedence over the drawing name, which remains the fallback for older clients. The connector replies with the protocol version the request used, and accepts versions 2 and 3.

## Data ownership (Phase 2)

The Rust window reads `%APPDATA%\AcLayerStandardizer\config.json`, `layer_dictionary.json`, and the translation memory itself, builds the target filters from the dictionary, and persists the chosen standard and "Apply & Remember" mappings. The connector only supplies the active drawing's layers (`GetDrawingSnapshot`, now without standards, categories, or memory) and the standard-layer names read from a template DWG (`GetStandardLayers`, which also caches the layers' properties for Apply). Behaviour parity with the C# implementations is pinned by shared golden files in `tests/parity/`, asserted by both test suites.

