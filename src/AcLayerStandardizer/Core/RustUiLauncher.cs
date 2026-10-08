using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Core;

internal static class RustUiLauncher
{
    // True when the Rust window is installed; callers can then skip work only the
    // WPF fallback needs (reading the template, categorizing, loading memory).
    public static bool IsAvailable() => FindUiExecutable() is not null;

    // The mapping window this AutoCAD session launched, if any. Only touched from
    // AutoCAD commands (one thread).
    private static Process? _window;

    // When the window from an earlier LSTDR is still running, brings it forward
    // (restoring it if minimized) and says so on the command line instead of
    // launching a second one. Returns true when it did. Never throws.
    public static bool TryFocusExistingWindow(Document document)
    {
        if (WindowInstance.Decide(IsAlive(_window)) != LaunchDecision.FocusExisting) return false;
        try
        {
            var handle = FindWindowOf(_window!);
            if (handle != IntPtr.Zero)
            {
                if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(handle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Layer Herder could not bring its window forward: {ex.Message}");
        }
        try { document.Editor.WriteMessage($"\n{WindowInstance.AlreadyOpenMessage}"); }
        catch (Exception) { /* the command line is unavailable: nothing else to do */ }
        return true;
    }

    private static bool IsAlive(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; }
        catch (Exception) { return false; }
    }

    // Process.MainWindowHandle skips owned windows, and the mapping window is owned
    // by AutoCAD's main window, so look for the process's visible top-level window.
    private static IntPtr FindWindowOf(Process process)
    {
        var processId = (uint)process.Id;
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var owner);
            if (owner != processId || !NativeMethods.IsWindowVisible(hWnd)) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero) return found;
        process.Refresh();
        return process.MainWindowHandle;
    }

    private static class NativeMethods
    {
        public const int SW_RESTORE = 9;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

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

        // One window only; its snapshot is not replaced under it.
        if (TryFocusExistingWindow(doc)) return true;

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
            _window = Process.Start(startInfo);
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
