using System.IO;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.UI;

namespace AcLayerStandardizer.Commands;

public static class MappingsCommand
{
    [CommandMethod("ACLAYERSTD", "STD_Mappings", CommandFlags.Modal)]
    public static void ShowMappingsEditor()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var config = PluginConfig.Load();

        if (string.IsNullOrEmpty(config.TemplateDwgPath))
        {
            ed.WriteMessage("\nNo template DWG configured. Run STD_SetTemplate first.");
            return;
        }

        if (!File.Exists(config.TemplateDwgPath))
        {
            ed.WriteMessage($"\nTemplate DWG not found: {config.TemplateDwgPath}");
            return;
        }

        var memPath = string.IsNullOrEmpty(config.MemoryFilePath)
            ? Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json")
            : config.MemoryFilePath;

        IReadOnlyDictionary<string, LayerProperties> standardLayers;
        try
        {
            standardLayers = SideDatabase.LoadStandardLayers(config.TemplateDwgPath);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError reading template DWG: {ex.Message}");
            return;
        }

        var activeLayers = GetActiveLayerNames(doc.Database);

        var store = new MemoryStore(memPath);
        var memory = store.Load();

        var existingMappings = new Dictionary<string, string>(
            memory.Mappings, StringComparer.OrdinalIgnoreCase);

        var dialog = new NodeGraphWindow(
            activeLayers,
            [.. standardLayers.Keys],
            existingMappings);

        new WindowInteropHelper(dialog) { Owner = Application.MainWindow.Handle };

        if (dialog.ShowDialog() == true)
        {
            var newMappings = dialog.ResultMappings;
            var added = 0;

            foreach (var kvp in newMappings)
            {
                if (memory.Mappings.TryAdd(kvp.Key, kvp.Value))
                    added++;
            }

            if (added > 0)
            {
                store.Save(memory);
                ed.WriteMessage($"\nAdded {added} new mappings to translation memory.");
            }

            ed.WriteMessage($"\nTotal mappings: {memory.Mappings.Count}");
            ed.WriteMessage("\nAcLayerStandardizer: Mappings updated.");
        }
        else
        {
            ed.WriteMessage("\nCancelled.");
        }
    }

    private static List<string> GetActiveLayerNames(Database db)
    {
        var names = new List<string>();

        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!IsSystemLayer(ltr.Name))
                names.Add(ltr.Name);
        }

        tr.Commit();
        return names;
    }

    private static bool IsSystemLayer(string name)
    {
        return name is "0" or "Defpoints" or "AsBuilt"
            || name.StartsWith("*") || name.StartsWith("_");
    }
}
