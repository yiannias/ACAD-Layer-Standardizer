# 1.3 Plan: Live Drawing Sync and Rust Core Migration

**Status:** Draft for review
**Builds on:** `docs/rust-rearchitecture-plan.md` (goals and architecture still apply)
**Baseline:** Beta 1.2.2

## Goals

1. **Rust owns the plugin's logic and UI.** The only non-Rust code is the AutoCAD connector: it exposes AutoCAD's drawing access, applies approved changes, and launches the Rust window.
2. **The mapping window follows the active drawing.** With the Standardizer open beside AutoCAD, switching drawings updates the Source side to match.
3. **Unapplied mappings are never lost silently.** Closing the window, the drawing, or AutoCAD warns first.

## Where things stand (1.2.2)

| Area | Rust | C# |
|---|---|---|
| Mapping editor (node and column modes) | Yes | WPF editor remains as a fallback |
| Heuristic matching | Yes (`acad_layer_core`) | `MatchingEngine`, `HeuristicMatcher`, `MemoryMatcher` still used by `LSR` |
| Categorization / layer dictionary | Implemented, not used by the bridge | `LayerCategorizer` builds every snapshot |
| Translation memory file | Implemented, not used by the bridge | `MemoryStore` reads and writes it |
| Config and preferences | No | `PluginConfig`, `UserPreferences`, `LayerDictionaryDefinition` |
| `LSR` preview flow and dialogs | No | `StandardizeCommand`, `PreviewDialog`, `WelcomeDialog`, others |
| `RustNativeBridge` (P/Invoke) | Exists | Not called by anything |

## Sequencing and rationale

Live-sync makes snapshots repeated work. Today each snapshot makes C# categorize ~320 target names, read the memory file, load the dictionary, and send the standard layers. Doing that once a second is wasteful, and building live-sync on it would mean reworking it again when that logic moves. So the thin connector contract and the data-side migration come first.

### Phase 1: Thin connector contract

**Status:** implemented on `main`, pending manual verification in AutoCAD (see `docs/superpowers/plans/2026-10-06-phase1-thin-connector-contract.md`, Task 4 step 4). Protocol version is now 3; versions 2 and 3 are accepted.

Define the slim IPC surface between the Rust app and the AutoCAD connector.

- **Active drawing identity:** document handle plus display name, and a **change token** (drawing identity + layer-table hash) so "has anything changed?" is a cheap call.
- **Drawing layers:** names, properties, empty layers. No standard layers, categories, or memory.
- **Drawing-changed notification:** the connector hooks `DocumentManager.DocumentActivated` and signals the Rust app on its next poll (the pipe is client-initiated, so a poll with a change token is the simplest transport).
- **Apply plan, purge empty layers, load standard:** keep, but identify the target drawing by handle, not name alone (two open drawings can share a name).
- Version the protocol; keep the existing messages working until Phase 4.

**Done when:** the connector can answer "what is the active drawing and has it changed?" without building a full snapshot, and the existing UI still works against it.

### Phase 2: Move categorization, memory, and config to Rust

**Status:** implemented on `main`, pending manual verification in AutoCAD (see `docs/superpowers/plans/2026-10-06-phase2-rust-owns-data.md`, Task 5 step 2). The C# categorizer, memory store, config, and WPF editor are intentionally still present for the WPF fallback, `LSR`, and `SettingsCommand`; they are removed in Phase 4. Cross-language parity is pinned by goldens in `tests/parity/`.

- Rust loads the layer dictionary, categorizes the standard's layers, and owns the translation-memory file (read/write, import/export used by the Settings command).
- Rust reads and writes plugin config and user preferences.
- The connector stops sending categories, memory mappings, and hidden-target lists.

**Parity gate (required before removing C# equivalents):**
- Run the C# and Rust implementations over the same fixtures (representative standards, dictionaries, memory files) and diff outputs.
- Cover categorization, memory file round-trips (including files written by 1.2.x), and heuristic scores.
- Keep the C# code in place until the diff is clean on every supported TFM test run (net48, net8, net10).
- Memory-file format must remain backward compatible; a user upgrading must keep their translation memory.

**Done when:** the Rust app builds its full view from the thin contract plus its own files, and parity tests pass.

### Phase 3: Live-sync and unsaved-mapping protection

**Live-sync**
- Rust polls the change token about once a second while the window is open and visible; the connector also raises a "changed" flag from `DocumentActivated` so drawing switches show up immediately.
- On change, fetch the new drawing's layers, replace the Source side, and recompute matches. The Target (standard) side is unchanged.
- Per-drawing state: manual overrides are kept per drawing so switching away and back restores them.
- No refresh while an Apply, Purge, or Load Standard is in flight; refresh after it completes.
- Apply always targets the drawing the window is displaying; if that drawing is no longer reachable, show a clear message instead of a protocol error.
- Edge cases to handle: unsaved `Drawing1.dwg`, duplicate filenames, no open drawing, drawing busy in a command (quiet retry), layers added or deleted in the active drawing (the change token catches this).

**Unsaved-mapping warning**
- Closing the Standardizer with unapplied mappings prompts: Apply, Discard, or Cancel (Rust side only).
- Closing a drawing or quitting AutoCAD: the connector hooks the close/quit events, asks the Rust app whether it has unsaved mappings for that drawing, and holds the close until the user responds.
- **To verify early:** whether AutoCAD's .NET `BeginQuit` (and the document-close event) can be cancelled in every supported release. If not, fall back to showing the warning in the Rust window as AutoCAD closes and keeping mappings recoverable.

**Done when:** switching drawings updates the Source side within about a second, per-drawing mappings survive switching, and every close path prompts when mappings are unapplied.

### Phase 4: Move the `LSR` flow to Rust and remove WPF

- Move the `LSR` match-preview flow (`StandardizeCommand`, `PreviewDialog`) and its supporting dialogs to Rust, or fold them into the mapping window.
- Remove the WPF mapping editor, its fallback path, `LayerEditorViewModel`, `NodeGraphWindow`, the other WPF dialogs, `Nodify`, and the unused `RustNativeBridge` P/Invoke wrapper unless a concrete need appears.
- The `LSR` welcome notice may stay in .NET if that is still preferred (the earlier plan allowed it).
- What remains in C#: entry point, command registration, ribbon/menu setup, layer-table access, apply/purge, the IPC server, and the launcher.

**Done when:** no logic or UI outside the connector remains in C#.

## Risks

| Risk | Mitigation |
|---|---|
| Phase 2 regressions in matching or memory | Parity gate; keep C# until diffs are clean; test with real memory files |
| `BeginQuit` / close events not cancellable | Verify first in Phase 1/3; documented fallback |
| Per-second polling cost or UI churn | Cheap change-token call; full refresh only on change |
| Wrong-drawing apply | Handle-based drawing identity, plus the existing active-document guards |
| Release size or startup regressions | Keep the size-optimized release profile; measure installer size each phase |

## Release shape

- Ship in stages as 1.3 betas: Phase 1+2 (invisible but verified), Phase 3 (the user-visible feature), Phase 4 (cleanup).
- Update `docs/rust-rearchitecture-plan.md` status as phases land.
- Each phase ends with a full `build.ps1` run (tests on net48, net8, net10) before tagging.

## Open questions

1. Should unsaved mappings persist across an AutoCAD restart, or only warn on close?
2. On a drawing switch with unapplied mappings in the drawing being left: keep silently per-drawing (current proposal), or also show a small indicator on the window?
3. Poll interval: 1 second is the starting point; is a slower rate acceptable when the window is not focused?

## Wanted, not yet scheduled

- Import and export of the translation memory (and a choose-memory-file picker) from the mapping window with a file browser. Today these are typed commands (`STD_ExportMemory`, `STD_ImportMemory`, `STD_SetMemoryFile`) that prompt for a path at the AutoCAD command line.
