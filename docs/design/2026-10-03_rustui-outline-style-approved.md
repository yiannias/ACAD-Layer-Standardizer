# RustUI outline style selection

Chris selected the “3. Neutral with outline” design. Keep the charcoal node fill and color-coded outline / connector for match status. The Node/Column selector and Apply / Apply & Remember buttons should keep the larger, clearer treatment shown here. Reduce the vertical height of Column rows modestly because the current spacing feels too padded. Apply styling consistently in RustUI and preserve match-state meanings. Reference is a design direction, not an exact scale target.

Implemented in RustUI: dark neutral node faces, brighter match-status outlines and dots in both modes, status-colored Column connectors, shorter Column rows, larger footer controls, and clearer pairing annotations without the unsupported arrow glyph. cargo check, optimized release build, installer compilation, and git diff --check succeeded. GUI appearance remains unverified until user-controlled AutoCAD review.
