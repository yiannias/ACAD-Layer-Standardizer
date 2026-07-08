using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.Matching;

namespace AcLayerStandardizer.Commands;

public static class StandardizeCommand
{
    [CommandMethod("ACLAYERSTD", "StandardizeLayers", CommandFlags.Modal)]
    public static void StandardizeLayers()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var db = doc.Database;

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

        ed.WriteMessage("\nAcLayerStandardizer: Loading standard layers from template...");

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

        ed.WriteMessage($"\n  Found {standardLayers.Count} standard layers.");

        ed.WriteMessage("\n  Reading active drawing layers...");
        var activeLayerNames = GetActiveLayerNames(db);

        ed.WriteMessage($"\n  Found {activeLayerNames.Count} user layers in active drawing.");

        ed.WriteMessage("\n  Loading translation memory...");
        var store = new MemoryStore(memPath);
        var memory = store.Load();
        ed.WriteMessage($"\n  Memory has {memory.Mappings.Count} known mappings.");

        ed.WriteMessage("\n  Running 3-tier matching engine...");
        var memoryMatcher = new MemoryMatcher(memory);
        var heuristicMatcher = new HeuristicMatcher(standardLayers.Keys.ToArray(), config.HeuristicThreshold);
        var engine = new MatchingEngine(memoryMatcher, heuristicMatcher);

        var results = engine.ClassifyAll(activeLayerNames);

        PrintResults(ed, results, standardLayers);

        var newMappings = results
            .Where(r => r.Source == MatchSource.Memory && r.TargetLayer is not null)
            .ToDictionary(r => r.SourceLayer, r => r.TargetLayer!);

        if (newMappings.Count > 0)
        {
            var updated = false;
            foreach (var kvp in newMappings)
            {
                if (memory.Mappings.TryAdd(kvp.Key, kvp.Value))
                    updated = true;
            }

            if (updated)
            {
                store.Save(memory);
                ed.WriteMessage($"\n  Added {newMappings.Count} new mappings to memory.");
            }
        }

        ed.WriteMessage("\nAcLayerStandardizer: Analysis complete.");
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
            {
                names.Add(ltr.Name);
            }
        }

        tr.Commit();
        return names;
    }

    private static bool IsSystemLayer(string name)
    {
        return name is "0" or "Defpoints" or "AsBuilt"
            || name.StartsWith("*") || name.StartsWith("_");
    }

    private static void PrintResults(Editor ed, IReadOnlyList<MatchResult> results, IReadOnlyDictionary<string, LayerProperties> standardLayers)
    {
        var matched = results.Count(r => r.Source == MatchSource.Memory);
        var suggested = results.Count(r => r.Source == MatchSource.Heuristic);
        var unmatched = results.Count(r => r.Source == MatchSource.Unmatched);

        ed.WriteMessage($"\n\n=== Results ===");
        ed.WriteMessage($"\n  Memory Match : {matched}");
        ed.WriteMessage($"\n  Suggested    : {suggested}");
        ed.WriteMessage($"\n  Unmatched    : {unmatched}");
        ed.WriteMessage($"\n  Total        : {results.Count}");

        if (suggested > 0)
        {
            ed.WriteMessage($"\n\n--- Suggested Matches (verify these) ---");
            foreach (var r in results.Where(r => r.Source == MatchSource.Heuristic))
            {
                ed.WriteMessage($"\n  {r.SourceLayer}  -->  {r.TargetLayer}  ({r.Confidence:P0})");
            }
        }

        if (unmatched > 0)
        {
            ed.WriteMessage($"\n\n--- Unmatched Layers (need manual mapping) ---");
            foreach (var r in results.Where(r => r.Source == MatchSource.Unmatched))
            {
                ed.WriteMessage($"\n  {r.SourceLayer}");
            }
        }
    }
}
