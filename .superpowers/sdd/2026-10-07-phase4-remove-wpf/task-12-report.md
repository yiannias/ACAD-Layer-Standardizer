# Task 12 report

## Changes
- csproj: removed the `Nodify` 7.3.0 PackageReference ItemGroup. Nothing else pointed at deleted files (no Page/Resource/ApplicationDefinition/Compile items; the two ribbon PNG EmbeddedResources, AutoCAD.NET, System.Text.Json, PolySharp, Microsoft.CSharp all kept).
- UseWPF experiment: removed it, built all three TFMs: FAILED (CS0234/CS0246 in RibbonSetup.cs: System.Windows.Controls, Media, BitmapImage; EntryPoint.cs: System.Windows.Threading / DispatcherTimer). Restored, with a one-line XML comment (RibbonSetup.cs, EntryPoint.cs, LsrCommandHandler.cs).
- installer .iss line 567: comment now says "the Rust mapping window writes it under LocalAppData". No behavior change (LF file, line endings kept).
- Grep of build.ps1 / installer / csproj for Nodify, UserPreferences, acad_layer_ffi, NodeGraph, Welcome, STD_Settings: only the csproj Nodify line and the .iss UserPreferences comment (both fixed). The `.iss` `wpWelcome` hit is Inno's built-in wizard page, unrelated. build.ps1 needed no change.

## Verification
- dotnet test: 116 on net48, net8.0, net10.0.
- cargo test --workspace: core 40, categorizer 1, parity 4 (+1 ignored), ipc 24, ui 117.
- build.ps1: exit 0 (log in scratchpad).

## Payload (from .iss Source: lines and bundle folder)
- R24: AcLayerStandardizer.dll, Microsoft.Bcl.AsyncInterfaces, System.Buffers, System.Memory, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe, System.Text.Encodings.Web, System.Text.Json, System.Threading.Tasks.Extensions, System.ValueTuple (.dll), acad_layer_ui.exe
- R25: AcLayerStandardizer.dll, acad_layer_ui.exe
- R26: AcLayerStandardizer.dll, acad_layer_ui.exe
- Nodify.dll is gone from all three (previously in every bin\Release\<tfm>, so in every payload dir). No System.Windows.* helper DLLs were ever in the payload. Remaining R24 DLLs are System.Text.Json's net48 dependencies.

## Sizes
- Installer: 6,115,242 (BETA-1.3.0 built earlier today, 16:18) -> 6,003,785 bytes (-111,457). (The 6,185,435 figure in the brief matches neither dist file; closest is BETA-1.2.4 at 6,184,922.)
- Plug-in DLL: unchanged (Nodify was a reference only): net48 119,808; net8.0 110,592; net10.0 110,080. Nodify.dll sizes removed: 279,552 / 280,064 / 280,576 bytes.

## Concerns
- None. Untracked files (ChatGPT png, archify/, dist zip) left alone.
