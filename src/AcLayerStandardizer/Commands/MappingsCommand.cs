using System.IO;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.Matching;
using AcLayerStandardizer.UI;

namespace AcLayerStandardizer.Commands;

public static class MappingsCommand
{
    [CommandMethod("LSTDR")]
    public static void LaunchStandardizer()
    {
        ShowMappingsEditor();
    }

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

        var configThreshold = config.HeuristicThreshold;

        var sortedSource = activeLayers.OrderBy(n => n).ToList();
        var sortedStandard = standardLayers.Keys
            .OrderBy(n => n == "0" ? 0 : 1)
            .ThenBy(n => n)
            .ToList();

        // Detect empty source layers
        var emptyLayers = GetEmptyLayers(doc.Database);

        // Run heuristic matching for all source layers not already in memory
        var heuristicMatcher = new HeuristicMatcher(sortedStandard, configThreshold);
        var heuristicResults = new List<MatchResult>();
        foreach (var layer in sortedSource)
        {
            if (memory.Mappings.ContainsKey(layer)) continue;
            var result = heuristicMatcher.TryMatch(layer);
            if (result is not null)
                heuristicResults.Add(result);
        }

        NodeGraphWindow dialog;
        try
        {
            dialog = new NodeGraphWindow(
                sortedSource,
                sortedStandard,
                memory.Mappings,
                heuristicResults,
                emptyLayers,
                names =>
                {
                    using var purgeTr = doc.Database.TransactionManager.StartTransaction();
                    var purgeLt = (LayerTable)purgeTr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                    foreach (var name in names)
                    {
                        if (!purgeLt.Has(name)) continue;
                        var ltr = (LayerTableRecord)purgeTr.GetObject(purgeLt[name], OpenMode.ForWrite);
                        try { ltr.Erase(true); }
                        catch { }
                    }
                    purgeTr.Commit();
                },
                sourceFileName: Path.GetFileName(doc.Name),
                templatePath: config.TemplateDwgPath,
                standardLayerProperties: standardLayers,
                onTemplateChanged: newPath =>
                {
                    // Remember the switch for next launch too, not just this session.
                    config.TemplateDwgPath = newPath;
                    config.Save();
                });
        }
        catch (System.Exception ex)
        {
            var logPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "std_mappings_error.log");
            System.IO.File.WriteAllText(logPath,
                $"=== STD_Mappings Error ===\nTime: {DateTime.UtcNow:O}\n\n{ex}\n");
            ed.WriteMessage($"\nError creating editor — details written to {logPath}");
            ed.WriteMessage($"\n  Exception: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                ed.WriteMessage($"\n  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return;
        }

        new WindowInteropHelper(dialog) { Owner = Application.MainWindow.Handle };

        if (dialog.ShowDialog() != true)
        {
            ed.WriteMessage("\nCancelled.");
            return;
        }

        var resultMappings = dialog.ResultMappings;
        var action = dialog.ResultAction;

        if (action is MappingEditorAction.ApplyAndSave)
        {
            var beforeCount = memory.Mappings.Count;
            memory.Mappings.Clear();
            foreach (var kvp in resultMappings)
                memory.Mappings[kvp.Key] = kvp.Value;
            try
            {
                store.Save(memory);
                var diff = memory.Mappings.Count - beforeCount;
                if (diff != 0)
                    ed.WriteMessage($"\nTranslation memory synced ({memory.Mappings.Count} mappings, Δ={diff:+0;-0}).");
                else
                    ed.WriteMessage($"\nTranslation memory unchanged ({memory.Mappings.Count} mappings).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFailed to save translation memory to {store.FilePath}: {ex.Message}");
            }
        }

        if (action is MappingEditorAction.Apply or MappingEditorAction.ApplyAndSave)
        {
            var result = StandardizeCommand.ApplyMappings(
                doc.Database, resultMappings, dialog.StandardLayerProperties, dialog.PropertySettings);
            ed.WriteMessage($"\n  Renamed/merged: {result.Renamed}");
            ed.WriteMessage($"\n  Properties synced: {result.Synced}");
            ed.WriteMessage("\nAcLayerStandardizer: Standardization complete.");
            ed.WriteMessage("\n  Snapshot saved (use ACLAYERSTD.UNDOSTANDARDIZATION to revert).");
        }
    }

    private static HashSet<string> GetEmptyLayers(Database db)
    {
        var layerCounts = new Dictionary<ObjectId, int>();

        using var tr = db.TransactionManager.StartTransaction();

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in lt)
            layerCounts[id] = 0;

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent is null || ent.IsErased) continue;
                if (layerCounts.ContainsKey(ent.LayerId))
                    layerCounts[ent.LayerId]++;
            }
        }

        tr.Commit();

        var emptyIds = layerCounts.Where(kvp => kvp.Value == 0)
            .Select(kvp => kvp.Key).ToHashSet();

        var emptyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var tr2 = db.TransactionManager.StartTransaction();
        var lt2 = (LayerTable)tr2.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var id in emptyIds)
        {
            var ltr = (LayerTableRecord)tr2.GetObject(id, OpenMode.ForRead);
            emptyNames.Add(ltr.Name);
        }
        tr2.Commit();

        return emptyNames;
    }

    private static List<string> GetActiveLayerNames(Database db)
    {
        var names = new List<string>();

        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!Core.LayerHelper.IsSystemLayer(ltr.Name))
                names.Add(ltr.Name);
        }

        tr.Commit();
        return names;
    }

}
