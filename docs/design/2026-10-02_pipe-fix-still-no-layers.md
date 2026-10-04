# IPC fix still not showing layers

The screenshot shows the Rust mapping window still stuck at “Connecting to AutoCAD…” with zero visible source layers. The selected AutoCAD drawing is visible behind the window. The Rust build label remains `v0.1.1 · Build 1790982338571`, which identifies the Rust UI binary but does not identify which .NET plugin DLL AutoCAD loaded.

At report time, the latest AutoCAD IPC log recorded a snapshot with 119 source layers and 321 standards, then “Client connected,” followed by `IOException: Pipe is broken`. It recorded no “Request received” event. This places the remaining failure after source-layer extraction and at the pipe request exchange. The next check is to confirm the exact loaded plugin module and capture the Rust client error before selecting another transport change.
