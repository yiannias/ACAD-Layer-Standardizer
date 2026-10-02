using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;

namespace AcLayerStandardizer.Core;

internal static class RustUiLauncher
{
    public static bool TryLaunchFromActiveAutoCad(
        string drawingName,
        IReadOnlyList<string> sourceLayers,
        IReadOnlyList<string> standardLayers,
        IEnumerable<string> emptyLayers,
        IReadOnlyDictionary<string, string> memoryMappings,
        IEnumerable<(string Name, string SortGroup, IEnumerable<string> Layers)> targetFilters)
    {
#if DEBUG
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return false;

        var executable = FindUiExecutable();
        if (executable is null) return false;

        IpcBridgeServer.SetDrawingSnapshot(drawingName, sourceLayers, standardLayers,
            emptyLayers, memoryMappings, targetFilters);

        var owner = Application.MainWindow.Handle;
        if (owner == IntPtr.Zero) return false;

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            Arguments = $"--owner-hwnd={owner.ToInt64()}"
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            doc.Editor.WriteMessage($"\nCould not launch the Rust mappings UI: {ex.Message}");
        }
#endif
        return false;
    }

#if DEBUG
    private static string? FindUiExecutable()
    {
        // AutoCAD's AppContext.BaseDirectory points at acad.exe, not the
        // folder from which NETLOAD loaded this plugin.
        var assemblyDirectory = Path.GetDirectoryName(typeof(RustUiLauncher).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory)) return null;

        var directory = new DirectoryInfo(assemblyDirectory);
        while (directory is not null)
        {
            // Prefer the GUI-subsystem Release binary so launching the Rust
            // window cannot flash a console. Fall back to Debug for iteration.
            foreach (var profile in new[] { "release", "debug" })
            {
                var candidate = Path.Combine(directory.FullName, "rust", "target", profile, "acad_layer_ui.exe");
                if (File.Exists(candidate)) return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
#endif
}
