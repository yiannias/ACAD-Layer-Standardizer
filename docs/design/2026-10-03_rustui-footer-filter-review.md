# RustUI footer and filter review

This installed RustUI capture confirms the bottom panel now floats across the window with a translucent dark fill and rounded outline. The red circle marks the Target Filter resize grip appearing at the application window's lower-right, separate from the filter panel's lower-right corner. Fix the Rust resize container so its visible frame and resize grip share the same panel bounds. Keep the footer treatment and current dark palette.

Rust-side layout correction: the visible Target Filter frame now fills the resize allocation, keeping the resize grip at the frame's lower-right. cargo check and optimized release build succeeded; the updated installer compiled. This geometry still needs Chris's visual confirmation in AutoCAD.
