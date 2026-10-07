# Footer hint shows an empty box instead of an arrow

Markup: Chris pasted an annotated screenshot of the footer bar with a red circle around an empty box in the hint line ("Drag from a source [box] standard layer to map. Click a connection to remove it...") and the note "what is this box?" with the request "please clean this up".

The raw image could not be saved to disk (the session gave no file for that paste), so only this description is kept.

Interpretation: the hint text contained a right-arrow character ("→") that the window's font does not have, so egui drew its "missing character" box. Other symbols in the window (bullets, the middle dot, the ellipsis) render fine; the arrow was the only one affected.

Done: the hint now reads "Drag from a source layer to a standard layer to map it. Click a connection to remove it. Scroll to zoom. Middle-drag to pan." (`rust/crates/acad_layer_ui/src/mapping_editor.rs`). The window source no longer contains the arrow character.
