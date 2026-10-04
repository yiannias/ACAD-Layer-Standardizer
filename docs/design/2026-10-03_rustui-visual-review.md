# RustUI visual review

This is the RustUI Node mode after installing the RustUI package. Compared with the earlier WPF reference, match fills are much darker and muddy because the match colors are blended into charcoal. The bottom status and mode controls are crowded into a small centered strip and overlap the graph. Layer nodes are square and use subdued borders and light text instead of the WPF reference's rounded, outlined rows with dark text on bright match fills. The window title says “Spatial Edition” rather than “Layer Mapping Editor.” Review should preserve the Source Drawing and Target Filter panels, but bring the overall screen closer to WPF visual parity. Pairing feedback in Column mode remains a separate acceptance item: visible match colors, selected target highlighting paired sources, and a gray count or source names.

## Additional requested behavior

- The bottom bar should be a wide floating panel, inset from the window edges and using the same translucent dark treatment as the side panels. Help/status/version information stays on the left; the Node/Column switch and Apply actions stay on the right. The current compact centered strip crowds the graph, and the opaque black band is incorrect.
- Clicking the target drawing filename under the Target heading should open the standard chooser. The current filename label is not interactive; choosing a standard is only available through the separate button in Target Filter.
- Target Filter should be resizable. The current Rust panel has a fixed width and no resize handling. The earlier red markup indicates its resize grip belongs at the panel's lower-right corner.
