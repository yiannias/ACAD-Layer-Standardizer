using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;

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

    [CommandMethod("ACLAYERSTD", "STD_ExportMemory", CommandFlags.Modal)]
    public static void ExportMemory()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var config = PluginConfig.Load();
        var memPath = string.IsNullOrEmpty(config.MemoryFilePath)
            ? Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json")
            : config.MemoryFilePath;

        if (!File.Exists(memPath))
        {
            ed.WriteMessage("\nNo translation memory file found to export.");
            return;
        }

        var pr = ed.GetString("\nEnter export path (e.g. C:/shared/my_memory.json): ");
        if (pr.Status != PromptStatus.OK) return;

        var exportPath = pr.StringResult.Trim('"');
        if (string.IsNullOrEmpty(exportPath))
        {
            ed.WriteMessage("\nExport cancelled.");
            return;
        }

        try
        {
            File.Copy(memPath, exportPath, overwrite: true);
            ed.WriteMessage($"\nTranslation memory exported to: {exportPath}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError exporting memory: {ex.Message}");
        }
    }

    [CommandMethod("ACLAYERSTD", "STD_ImportMemory", CommandFlags.Modal)]
    public static void ImportMemory()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var config = PluginConfig.Load();
        var memPath = string.IsNullOrEmpty(config.MemoryFilePath)
            ? Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json")
            : config.MemoryFilePath;

        var pr = ed.GetString("\nEnter path to memory JSON file to import: ");
        if (pr.Status != PromptStatus.OK) return;

        var importPath = pr.StringResult.Trim('"');
        if (!File.Exists(importPath))
        {
            ed.WriteMessage($"\nError: File not found: {importPath}");
            return;
        }

        try
        {
            var store = new MemoryStore(memPath);
            var current = store.Load();
            var imported = new MemoryStore(importPath).Load();

            var before = current.Mappings.Count;
            store.Merge(current, imported);
            store.Save(current);

            var added = current.Mappings.Count - before;
            ed.WriteMessage($"\nImported {imported.Mappings.Count} mappings from file.");
            ed.WriteMessage($"\nAdded {added} new mappings. Total: {current.Mappings.Count}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError importing memory: {ex.Message}");
        }
    }
}
