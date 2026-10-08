# Third-Party Notices

Layer Herder is MIT-licensed (see [LICENSE](LICENSE)). Nodify is no
longer included. The mapping-window program (`acad_layer_ui.exe`) is built from
open-source Rust crates, listed in `rust/Cargo.lock`, whose licenses (MIT,
Apache-2.0 and similar) will be reproduced here.

A full per-crate license notice is still to be added.

## AutoCAD SDK assemblies (not redistributed)

`AcDbMgd.dll`, `AcCoreMgd.dll`, `AcMgd.dll`, `AdWindows.dll`,
`Autodesk.AutoCAD.Interop.dll`, and `Autodesk.AutoCAD.Interop.Common.dll` are
referenced at compile time only (`<Private>False</Private>` in the project
file) and are never copied into the installed bundle. They are proprietary
Autodesk assemblies supplied by the user's own AutoCAD installation, not
part of this software's distribution.
