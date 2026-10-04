# IPC trace build still connecting

The screenshot shows the Rust mapping window still stuck at “Connecting to AutoCAD…” with zero visible source layers. The Rust build label confirms the diagnostic build: `v0.1.1 · Build 1790984132050`.

The Rust trace records “Named pipe opened” and “Writing 29 request bytes,” then stops. The AutoCAD log records “Client connected” but no “Request received” event. The client is blocked during the write, after AutoCAD captured the drawing layers and accepted the connection.

This is consistent with AutoCAD running a server instance without the new pipe buffers. The follow-up .NET build logs the loaded assembly path and configured buffer sizes, so the next trace can confirm whether the intended DLL is active.
