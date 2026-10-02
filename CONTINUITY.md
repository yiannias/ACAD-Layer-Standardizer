# Continuity Handoff

## Current checkout

- Repository: `ACAD-Layer-Standardizer`
- Branch: `feature/rust-core`
- Purpose: experimental Rust core and Spatial UI Kit mapping editor alongside the existing AutoCAD .NET plugin. This is not a production migration or release.
- Architecture plan and prior decisions: [`docs/rust-rearchitecture-plan.md`](docs/rust-rearchitecture-plan.md)
- User constraint: do not launch or manipulate GUI applications, or send synthetic input, without asking first and waiting. State the number and purpose of GUI runs in each approval request. Headless builds, tests, edits, and Git work are allowed.

## Work in this handoff

- Rust core matching/categorization/memory refinements and tests, plus workspace UI work in `rust/`.
- Versioned IPC data types and a Windows named-pipe client in `rust/crates/acad_layer_ipc`.
- `IpcBridgeServer` starts with the AutoCAD plugin, serves a cached drawing snapshot, and handles `Ping`, `GetDrawingLayers`, and `GetDrawingSnapshot`. Snapshot capture happens from the active AutoCAD command context; the worker only serves the immutable copy.
- `LSTDR` gathers source/standard/empty layers, translation memory, and target category filters, then tries the Rust UI before falling back to the existing WPF editor.
- In Debug builds, `RustUiLauncher` starts the UI with AutoCAD's HWND as owner. The UI connects to the pipe and displays the drawing in the Rust mapping editor.
- `docs/rust-rearchitecture-plan.md` records the architecture, boundaries, milestones, and review decisions.

## Known incomplete work

- Rust editor Apply and Apply & Remember buttons are disabled. The IPC enum declares `ApplyPlan`, but the C# server has no handler and no AutoCAD-safe apply dispatch yet.
- Rust UI launch is compiled under `DEBUG` only. The normal `build.ps1` bundle/installer does not build or package Rust artifacts.
- `acad_layer_ffi` has no production call sites; decide whether it remains useful given the companion UI's direct Rust core use.
- Rust-owned window behavior, focus/modality, close/reopen, and the complete AutoCAD flow have not been visually verified. No GUI app was launched for this handoff.
- The current mapping editor is a functional visual scaffold, not feature parity with the WPF editor; check the plan for required workflow details.
- Builds and tests were not run as part of this handoff.

## Recommended pickup sequence

1. Read the full architecture plan and inspect the branch status/history.
2. Ask the user for one bounded GUI batch to validate the owned Rust window from AutoCAD (state the exact number and purpose of runs); wait for their reply before launching anything. Report any remaining UI/window state after the run.
3. If hosting passes, design and implement a versioned apply-plan protocol. Ensure all AutoCAD database access and transactions run in valid AutoCAD document/command context, validate proposed names/operations, and preserve explicit user approval. Add headless protocol tests.
4. Wire build/package artifacts and establish the .NET/Rust verification matrix and core parity evidence described in the plan.
5. Reassess the WPF fallback, FFI role, and production adoption only after evidence is recorded.

## Handoff verification

At handoff, the working changes were committed on `feature/rust-core` and pushed to `origin/feature/rust-core`. No builds, tests, or GUI validation were run for this handoff.
