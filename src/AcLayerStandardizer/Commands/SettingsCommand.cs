using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Commands;

public static class SettingsCommand
{
    [CommandMethod("ACLAYERSTD", "STD_Settings", CommandFlags.Modal)]
    public static void ShowSettings()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var config = PluginConfig.Load();

        ed.WriteMessage($"\n--- AcLayerStandardizer Settings ---");
        ed.WriteMessage($"\n  Template DWG  : {(string.IsNullOrEmpty(config.TemplateDwgPath) ? "(not set)" : config.TemplateDwgPath)}");
        ed.WriteMessage($"\n  Memory File   : {(string.IsNullOrEmpty(config.MemoryFilePath) ? "(not set)" : config.MemoryFilePath)}");
        ed.WriteMessage($"\n  Heuristic Threshold: {config.HeuristicThreshold:P0}");
        ed.WriteMessage($"\n------------------------------------");
    }

    [CommandMethod("ACLAYERSTD", "STD_SetTemplate", CommandFlags.Modal)]
    public static void SetTemplate()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var pr = ed.GetString("\nEnter path to template DWG: ");
        if (pr.Status != PromptStatus.OK) return;

        var path = pr.StringResult.Trim('"');
        if (!File.Exists(path))
        {
            ed.WriteMessage($"\nError: File not found: {path}");
            return;
        }

        var config = PluginConfig.Load();
        config.TemplateDwgPath = path;
        config.Save();

        ed.WriteMessage($"\nTemplate DWG set to: {path}");
    }

    [CommandMethod("ACLAYERSTD", "STD_SetMemoryFile", CommandFlags.Modal)]
    public static void SetMemoryFile()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var pr = ed.GetString("\nEnter path to memory JSON file (or BLANK for default): ");
        if (pr.Status != PromptStatus.OK) return;

        var path = pr.StringResult.Trim('"');

        var config = PluginConfig.Load();
        config.MemoryFilePath = string.IsNullOrEmpty(path)
            ? Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json")
            : path;
        config.Save();

        ed.WriteMessage($"\nMemory file set to: {config.MemoryFilePath}");
    }
}
