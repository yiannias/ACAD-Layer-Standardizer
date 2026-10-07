# Node mode: header text should match the node text size

Markup: `2026-10-07_node-header-text-size.png` (annotated screenshot of the Source and Target panels in node mode).

Chris's note on the image: "fixed text size. match text size of nodes", with arrows from the drawing-name subtitles ("A-FP-003-000-0.dwg", "STANDARD TEMPLATE.dws") and from both filter boxes ("Filter source layers", "Filter target layers") to a node label ("A-FL-CASEWORK").

Interpretation: the subtitles and the two filter boxes must use exactly the same text size as the node labels, and that size should scale with zoom together with the nodes (a fixed size relative to the nodes, matching the earlier request that the filter box be "STATIC size relative to all of the other elements").

What the code did: node labels were 12 x zoom, the subtitles 11 x zoom, and the filter box text a fixed 13 px, so the three drifted apart when zooming.

Done: one shared `NODE_LABEL_SIZE` (12) and `node_label_px(zoom)` in `rust/crates/acad_layer_ui/src/mapping_editor.rs`, used by node labels, panel subtitles and the node-mode filter boxes. Column mode keeps its own 13 px filter text.
