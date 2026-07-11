# Third-Party Notices

ACAD Layer Standardizer is MIT-licensed (see [LICENSE](LICENSE)). It ships one
compiled third-party binary as part of its AutoCAD bundle: `Nodify.dll`, the
node-graph editor control used by the Layer Mapping Editor. Nodify is also
MIT-licensed; its notice is reproduced below per that license's terms.

## Nodify

<https://github.com/miroiu/nodify>

```
MIT License

Copyright (c) Miroiu Emanuel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## AutoCAD SDK assemblies (not redistributed)

`AcDbMgd.dll`, `AcCoreMgd.dll`, `AcMgd.dll`, `AdWindows.dll`,
`Autodesk.AutoCAD.Interop.dll`, and `Autodesk.AutoCAD.Interop.Common.dll` are
referenced at compile time only (`<Private>False</Private>` in the project
file) and are never copied into the installed bundle. They are proprietary
Autodesk assemblies supplied by the user's own AutoCAD installation, not
part of this software's distribution.
