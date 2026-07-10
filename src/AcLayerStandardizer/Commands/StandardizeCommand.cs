using System.IO;
using System.Windows.Interop;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.Matching;
using AcLayerStandardizer.UI;

namespace AcLayerStandardizer.Commands;

public static class StandardizeCommand
{
    [CommandMethod("LSR", CommandFlags.Modal)]
    [CommandMethod("ACLAYERSTD", "StandardizeLayers", CommandFlags.Modal)]
    public static void StandardizeLayers()
    {
        WelcomeCommand.ShowWelcome();
    }

    [CommandMethod("ACLAYERSTD", "ApplyStandardization", CommandFlags.Modal)]
    public static void ApplyStandardization()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var ctx = SetupPipeline(ed);
        if (ctx is null) return;

        var results = ctx.Engine.ClassifyAll(ctx.ActiveLayerNames);
        PrintResults(ed, results, ctx.StandardLayers);

        var toApply = results
            .Where(r => r.TargetLayer is not null && r.SourceLayer != r.TargetLayer)
            .ToList();

        if (toApply.Count == 0)
        {
            ed.WriteMessage("\nNo layers need standardization.");
            return;
        }

        var previewItems = toApply.Select(r => new PreviewItem
        {
            SourceLayer = r.SourceLayer,
            TargetLayer = r.TargetLayer!,
            Confidence = r.Confidence.ToString("P0"),
        }).ToList();

        var unmatched = results
            .Where(r => r.Source == MatchSource.Unmatched)
            .Select(r => r.SourceLayer)
            .ToList();

        var dialog = new PreviewDialog(previewItems, unmatched);
        new WindowInteropHelper(dialog) { Owner = Application.MainWindow.Handle };

        if (dialog.ShowDialog() != true)
        {
            ed.WriteMessage("\nCancelled.");
            return;
        }

        toApply = dialog.SelectedItems
            .Select(i => results.FirstOrDefault(r => r.SourceLayer == i.SourceLayer))
            .Where(r => r is not null)
            .ToList()!;

        var db = doc.Database;
        var mappings = toApply
            .Where(r => r.TargetLayer is not null)
            .ToDictionary(r => r.SourceLayer, r => r.TargetLayer!);

        var result = ApplyMappings(db, mappings, ctx.StandardLayers);

        ed.WriteMessage($"\n  Renamed/merged: {result.Renamed}");
        ed.WriteMessage($"\n  Properties synced: {result.Synced}");

        var newMappings = toApply
            .Where(r => r.Source == MatchSource.Heuristic && r.TargetLayer is not null)
            .ToDictionary(r => r.SourceLayer, r => r.TargetLayer!);

        SaveNewMappings(newMappings, ctx.Memory, ctx.Store, ed);

        ed.WriteMessage($"\nAcLayerStandardizer: Standardization complete.");
        ed.WriteMessage("\n  Snapshot saved (use ACLAYERSTD.UNDOSTANDARDIZATION to revert).");
    }

    [CommandMethod("ACLAYERSTD", "UndoStandardization", CommandFlags.Modal)]
    public static void UndoStandardization()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;

        var snapshot = RollbackSnapshot.Load();
        if (snapshot is null)
        {
            ed.WriteMessage("\nNo standardization snapshot found to revert.");
            return;
        }

        ed.WriteMessage($"\nSnapshot from {snapshot.Timestamp:g}:");
        ed.WriteMessage($"\n  {snapshot.RenamedLayers.Count} renamed layers");
        ed.WriteMessage($"\n  {snapshot.ErasedLayers.Count} erased layers");

        var kwOpts = new PromptKeywordOptions(
            "\nRevert these changes?", "Yes No");
        kwOpts.Keywords.Default = "No";
        var pr = ed.GetKeywords(kwOpts);
        if (pr.Status != PromptStatus.OK || pr.StringResult != "Yes")
        {
            ed.WriteMessage("\nCancelled.");
            return;
        }

        var db = doc.Database;
        var restoredRenames = 0;
        var restoredErased = 0;

        using (var tr = doc.TransactionManager.StartTransaction())
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

            foreach (var renamed in snapshot.RenamedLayers)
            {
                if (!lt.Has(renamed.NewName)) continue;

                var ltr = (LayerTableRecord)tr.GetObject(lt[renamed.NewName], OpenMode.ForWrite);
                ltr.Name = renamed.OriginalName;
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)renamed.ColorIndex);
                ltr.LinetypeObjectId = GetLinetypeId(db, tr, renamed.Linetype);
                ltr.LineWeight = ParseLineWeight(renamed.LineWeight);
                ltr.IsPlottable = renamed.IsPlottable;
                if (renamed.Description is not null)
                    ltr.Description = renamed.Description;
                restoredRenames++;
            }

            foreach (var erased in snapshot.ErasedLayers)
            {
                if (lt.Has(erased.OriginalName)) continue;

                var ltr = new LayerTableRecord
                {
                    Name = erased.OriginalName,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, (short)erased.ColorIndex),
                    LinetypeObjectId = GetLinetypeId(db, tr, erased.Linetype),
                    LineWeight = ParseLineWeight(erased.LineWeight),
                    IsPlottable = erased.IsPlottable,
                };
                if (erased.Description is not null)
                    ltr.Description = erased.Description;

                var newId = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);

                if (erased.TransferredEntityHandles.Count > 0)
                {
                    var blk = (BlockTableRecord)tr.GetObject(
                        db.CurrentSpaceId, OpenMode.ForWrite);
                    foreach (var handleVal in erased.TransferredEntityHandles)
                    {
                        var h = new Handle(handleVal);
                        if (!db.TryGetObjectId(h, out var entId)) continue;
                        var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                        if (ent is null) continue;
                        ent.UpgradeOpen();
                        ent.LayerId = newId;
                    }
                }
                restoredErased++;
            }

            tr.Commit();
        }

        RollbackSnapshot.Delete();
        ed.WriteMessage($"\n  Renames restored: {restoredRenames}");
        ed.WriteMessage($"\n  Erased layers restored: {restoredErased}");
        ed.WriteMessage($"\n  Note: Entity transfers may not fully revert if entities were modified since.");
        ed.WriteMessage($"\nAcLayerStandardizer: Undo complete.");
    }

    public sealed record ApplyMappingsResult(int Renamed, int Synced);

    public static ApplyMappingsResult ApplyMappings(
        Database db,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, LayerProperties> standardLayers,
        PropertyMatchSettings? propSettings = null)
    {
        var snapshot = new RollbackSnapshot();
        var renamed = 0;
        var synced = 0;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            foreach (var (source, target) in mappings)
            {
                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) continue;

                var sourceId = GetLayerId(lt, tr, source);
                if (sourceId is null) continue;

                EnsureNotCurrentLayer(db, tr, source);

                ObjectId targetId;

                if (lt.Has(target))
                {
                    targetId = lt[target];

                    var srcLtr = (LayerTableRecord)tr.GetObject(sourceId.Value, OpenMode.ForRead);
                    var erased = new ErasedLayerBackup
                    {
                        OriginalName = source,
                        ColorIndex = srcLtr.Color.ColorIndex,
                        Linetype = GetLinetypeName(tr, srcLtr),
                        LineWeight = srcLtr.LineWeight.ToString(),
                        IsPlottable = srcLtr.IsPlottable,
                        Description = srcLtr.Description,
                        TransferredEntityHandles = TransferEntities(db, tr, sourceId.Value, targetId),
                    };
                    snapshot.ErasedLayers.Add(erased);

                    var wipLtr = (LayerTableRecord)tr.GetObject(sourceId.Value, OpenMode.ForWrite);
                    try
                    {
                        wipLtr.Erase(true);
                    }
                    catch (System.Exception ex)
                    {
                        var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                        ed.WriteMessage($"\n  Warning: could not erase layer '{source}' ({ex.Message}). Renaming instead.");
                        wipLtr.Name = target;
                        targetId = sourceId.Value;
                    }
                }
                else
                {
                    var srcLtr = (LayerTableRecord)tr.GetObject(sourceId.Value, OpenMode.ForRead);
                    var backup = new RenamedLayerBackup
                    {
                        OriginalName = source,
                        NewName = target,
                        ColorIndex = srcLtr.Color.ColorIndex,
                        Linetype = GetLinetypeName(tr, srcLtr),
                        LineWeight = srcLtr.LineWeight.ToString(),
                        IsPlottable = srcLtr.IsPlottable,
                        Description = srcLtr.Description,
                    };
                    snapshot.RenamedLayers.Add(backup);

                    var wipLtr = (LayerTableRecord)tr.GetObject(sourceId.Value, OpenMode.ForWrite);
                    wipLtr.Name = target;
                    targetId = sourceId.Value;
                }

                if (standardLayers.TryGetValue(target, out var props))
                {
                    var ltr = (LayerTableRecord)tr.GetObject(targetId, OpenMode.ForWrite);

                    var settings = propSettings ?? new PropertyMatchSettings();

                    if (settings.MatchColor)
                        ltr.Color = props.Color;

                    if (settings.MatchLinetype)
                        ltr.LinetypeObjectId = GetLinetypeId(db, tr, props.Linetype);

                    if (settings.MatchLineweight)
                        ltr.LineWeight = props.LineWeight;

                    ltr.IsPlottable = props.IsPlottable;

                    if (!string.IsNullOrEmpty(props.Description))
                        ltr.Description = props.Description;

                    synced++;
                }

                renamed++;
            }

            tr.Commit();
        }

        snapshot.Save();
        return new ApplyMappingsResult(renamed, synced);
    }

    private static void EnsureNotCurrentLayer(Database db, Transaction tr, string layerName)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc.Database != db) return;

        var curLtr = (LayerTableRecord)tr.GetObject(doc.Database.Clayer, OpenMode.ForRead);
        if (!string.Equals(curLtr.Name, layerName, StringComparison.OrdinalIgnoreCase)) return;

        curLtr.UpgradeOpen();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has("0"))
            doc.Database.Clayer = lt["0"];

        var ed = doc.Editor;
        ed.WriteMessage($"\n  Switched current layer from '{layerName}' to '0'.");
    }

    private sealed record PipelineContext(
        Editor Editor,
        IReadOnlyDictionary<string, LayerProperties> StandardLayers,
        List<string> ActiveLayerNames,
        MemoryStore Store,
        Data.TranslationMemory Memory,
        MatchingEngine Engine);

    private static PipelineContext? SetupPipeline(Editor ed)
    {
        var config = PluginConfig.Load();

        if (string.IsNullOrEmpty(config.TemplateDwgPath))
        {
            ed.WriteMessage("\nNo template DWG configured. Run STD_SetTemplate first.");
            return null;
        }

        if (!File.Exists(config.TemplateDwgPath))
        {
            ed.WriteMessage($"\nTemplate DWG not found: {config.TemplateDwgPath}");
            return null;
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
            return null;
        }

        ed.WriteMessage($"\n  Found {standardLayers.Count} standard layers.");

        ed.WriteMessage("\n  Reading active drawing layers...");
        var activeLayerNames = GetActiveLayerNames(ed);

        ed.WriteMessage($"\n  Found {activeLayerNames.Count} user layers in active drawing.");

        ed.WriteMessage("\n  Loading translation memory...");
        var store = new MemoryStore(memPath);
        var memory = store.Load();
        ed.WriteMessage($"\n  Memory has {memory.Mappings.Count} known mappings.");

        ed.WriteMessage("\n  Running 3-tier matching engine...");
        var memoryMatcher = new MemoryMatcher(memory);
        var heuristicMatcher = new HeuristicMatcher(standardLayers.Keys.ToArray(), config.HeuristicThreshold);
        var engine = new MatchingEngine(memoryMatcher, heuristicMatcher);

        return new PipelineContext(ed, standardLayers, activeLayerNames, store, memory, engine);
    }

    private static void SaveNewMappings(
        Dictionary<string, string> newMappings,
        Data.TranslationMemory memory,
        MemoryStore store,
        Editor ed)
    {
        if (newMappings.Count == 0) return;

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

    private static List<string> GetActiveLayerNames(Editor ed)
    {
        var doc = ed.Document;
        var names = new List<string>();

        using var tr = doc.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (IsStandardizationCandidate(ltr.Name))
            {
                names.Add(ltr.Name);
            }
        }

        tr.Commit();
        return names;
    }

    private static bool IsStandardizationCandidate(string name)
    {
        return !Core.LayerHelper.IsSystemLayer(name) && name != "0";
    }

    private static ObjectId? GetLayerId(LayerTable lt, Transaction tr, string name)
    {
        if (lt.Has(name))
            return lt[name];
        return null;
    }

    private static ObjectId GetLinetypeId(Database db, Transaction tr, string name)
    {
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (lt.Has(name))
            return lt[name];
        return lt["Continuous"];
    }

    private static string GetLinetypeName(Transaction tr, LayerTableRecord ltr)
    {
        if (!ltr.LinetypeObjectId.IsValid)
            return "Continuous";
        var ltrLt = (LinetypeTableRecord)tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead);
        return ltrLt.Name;
    }

    private static LineWeight ParseLineWeight(string value) =>
        Enum.TryParse<LineWeight>(value, out var result)
            ? result
            : LineWeight.LineWeight000;

    private static List<long> TransferEntities(Database db, Transaction tr, ObjectId sourceLayerId, ObjectId targetLayerId)
    {
        var handles = new List<long>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent is not null && ent.LayerId == sourceLayerId)
                {
                    handles.Add(entId.Handle.Value);
                    ent.UpgradeOpen();
                    ent.LayerId = targetLayerId;
                }
            }
        }

        return handles;
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
