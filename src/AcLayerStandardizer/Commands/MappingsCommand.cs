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
        if (doc is null) return;

        var ed = doc.Editor;

        var config = PluginConfig.Load();

        var templatePath = !string.IsNullOrEmpty(config.TemplateDwgPath) && File.Exists(config.TemplateDwgPath)
            ? config.TemplateDwgPath
            : string.Empty;
        if (templatePath.Length == 0)
            ed.WriteMessage("\nReference file unavailable. Opening without targets; click the Target header to choose one.");

        var memPath = config.GetEffectiveMemoryPath();

        // Rust window first: it reads the template, categorizes, and loads memory
        // itself, so only the active drawing's layers are captured here. The full
        // load below is for the WPF fallback.
        var rustAttempted = RustUiLauncher.IsAvailable();
        if (rustAttempted)
        {
            // The window is already open: bring it forward before reading any layers.
            if (RustUiLauncher.TryFocusExistingWindow(doc)) return;
            var rustSource = GetActiveLayerNames(doc.Database)
                .OrderBy(n => n, Core.NaturalSortComparer.Instance).ToList();
            if (RustUiLauncher.TryLaunchFromActiveAutoCad(
                    doc, Path.GetFileName(doc.Name), config.HeuristicThreshold, rustSource, Array.Empty<string>(),
                    GetEmptyLayers(doc.Database),
                    new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(), memPath,
                    Array.Empty<(string Name, string SortGroup, IEnumerable<string> Layers)>(),
                    templatePath, Array.Empty<string>())) return;
        }

        IReadOnlyDictionary<string, LayerProperties> standardLayers =
            new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase);
        if (templatePath.Length > 0)
        {
            try
            {
                standardLayers = SideDatabase.LoadStandardLayers(templatePath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCould not read saved standard: {ex.Message}. Choose a standard in the editor.");
            }
        }

        var activeLayers = GetActiveLayerNames(doc.Database);

        var store = new MemoryStore(memPath);
        TranslationMemory memory;
        var memoryLoaded = true;
        try
        {
            memory = store.Load();
        }
        catch (System.Exception ex)
        {
            memory = new TranslationMemory();
            memoryLoaded = false;
            ed.WriteMessage($"\nTranslation memory could not be read and will not be overwritten: {memPath}");
            ed.WriteMessage($"\n  {ex.GetType().Name}: {ex.Message}");
        }

        var configThreshold = config.HeuristicThreshold;

        var sortedSource = activeLayers.OrderBy(n => n, Core.NaturalSortComparer.Instance).ToList();
        var sortedStandard = standardLayers.Keys
            .OrderBy(n => n == "0" ? 0 : 1)
            .ThenBy(n => n, Core.NaturalSortComparer.Instance)
            .ToList();

        var emptyLayers = GetEmptyLayers(doc.Database);

        var categorized = LayerCategorizer.Classify(sortedStandard, LayerDictionaryDefinition.Load());
        var targetFilters = categorized.VisibleCategories.Select(category => (
            Name: category,
            SortGroup: categorized.SortGroupByTag.GetValueOrDefault(category, "Specific"),
            Layers: (IEnumerable<string>)categorized.LayerTags
                .Where(pair => pair.Value.Contains(category))
                .Select(pair => pair.Key)
                .ToArray()));

        // Capture drawing data in this AutoCAD command context. The IPC worker
        // only serves this snapshot and never accesses the drawing database.
        // A Rust launch that already failed above is not retried (it would only fail,
        // and print its error, a second time); go straight to the WPF fallback.
        if (!rustAttempted && RustUiLauncher.TryLaunchFromActiveAutoCad(
                doc, Path.GetFileName(doc.Name), configThreshold, sortedSource, sortedStandard,
                emptyLayers, standardLayers, memory.Mappings, store.FilePath, targetFilters,
                templatePath, categorized.AlwaysHidden)) return;

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
                templatePath: templatePath,
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
            if (!memoryLoaded)
            {
                ed.WriteMessage("\nTranslation memory was not saved because the existing file could not be read. Apply the mappings, then repair or choose another memory file before remembering them.");
            }
            else
            {
                var beforeCount = memory.Mappings.Count;

                // Only touch the source layers this session actually loaded and
                // judged -- clearing the whole dictionary here used to wipe out
                // every mapping remembered from other drawings, since
                // resultMappings only ever covers layers present in *this*
                // drawing. A layer the session loaded but left unmatched (e.g.
                // explicitly un-matched) is forgotten; everything else survives.
                foreach (var sourceLayer in dialog.SourceLayerNames)
                {
                    if (resultMappings.TryGetValue(sourceLayer, out var target))
                        memory.Mappings[sourceLayer] = target;
                    else
                        memory.Mappings.Remove(sourceLayer);
                }
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

    internal static HashSet<string> GetEmptyLayers(Database db)
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
            if (!Core.LayerHelper.ShouldSkip(ltr.Name))
                emptyNames.Add(ltr.Name);
        }
        tr2.Commit();

        return emptyNames;
    }

    internal static List<string> GetActiveLayerNames(Database db)
    {
        var names = new List<string>();

        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!Core.LayerHelper.ShouldSkip(ltr.Name))
                names.Add(ltr.Name);
        }

        tr.Commit();
        return names;
    }

}
