using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Core;

internal static class RustUiLauncher
{
    public static bool TryLaunchFromActiveAutoCad(
        Document document,
        string drawingName,
        double heuristicThreshold,
        IReadOnlyList<string> sourceLayers,
        IReadOnlyList<string> standardLayers,
        IEnumerable<string> emptyLayers,
        IReadOnlyDictionary<string, LayerProperties> standardLayerProperties,
        IReadOnlyDictionary<string, string> memoryMappings,
        string memoryFilePath,
        IEnumerable<(string Name, string SortGroup, IEnumerable<string> Layers)> targetFilters,
        string templatePath,
        IEnumerable<string> alwaysHiddenTargets)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return false;

        var executable = FindUiExecutable();
        if (executable is null) return false;

        // Initialize may have attempted to start the pipe before AutoCAD was
        // ready. Recheck it in the command context before launching the client.
        IpcBridgeServer.Start();
        IpcBridgeServer.SetDrawingSnapshot(document, drawingName, heuristicThreshold, sourceLayers, standardLayers,
            emptyLayers, standardLayerProperties, memoryMappings, memoryFilePath, targetFilters, templatePath,
            alwaysHiddenTargets);

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
        return false;
    }

    private static string? FindUiExecutable()
    {
        // AutoCAD's AppContext.BaseDirectory points at acad.exe, not the
        // folder from which NETLOAD loaded this plugin.
        var assemblyDirectory = Path.GetDirectoryName(typeof(RustUiLauncher).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory)) return null;

        var directory = new DirectoryInfo(assemblyDirectory);
        while (directory is not null)
        {
            // Installed payloads place the UI beside the plugin DLL.
            var packagedCandidate = Path.Combine(directory.FullName, "acad_layer_ui.exe");
            if (File.Exists(packagedCandidate)) return packagedCandidate;

            // Development builds can run from the repository's Rust target dir.
            foreach (var profile in new[] { "release", "debug" })
            {
                var candidate = Path.Combine(directory.FullName, "rust", "target", profile, "acad_layer_ui.exe");
                if (File.Exists(candidate)) return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
