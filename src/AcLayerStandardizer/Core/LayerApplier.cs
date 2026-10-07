using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Core;

public static class LayerApplier
{
    public sealed record ApplyMappingsResult(int Renamed, int Synced);

    public static ApplyMappingsResult ApplyMappings(
        Database db,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, LayerProperties> standardLayers,
        PropertyMatchSettings? propSettings = null)
    {
        if (LayerHelper.FindUnapplicableMapping(mappings) is { } problem)
            throw new InvalidOperationException(problem);

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
                        TransferredEntityHandles = TransferEntities(db, tr, sourceId.Value, targetId, propSettings?.MakeByLayer ?? false),
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

                    // Renamed layers keep the same ObjectId, so their entities
                    // are never touched by TransferEntities (which only runs
                    // for the merge/erase branch above) -- normalize here too.
                    if (propSettings?.MakeByLayer ?? false)
                        MakeEntitiesByLayer(db, tr, targetId);
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

    internal static ObjectId? GetLayerId(LayerTable lt, Transaction tr, string name)
    {
        if (lt.Has(name))
            return lt[name];
        return null;
    }

    internal static ObjectId GetLinetypeId(Database db, Transaction tr, string name)
    {
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (lt.Has(name))
            return lt[name];
        return lt["Continuous"];
    }

    internal static string GetLinetypeName(Transaction tr, LayerTableRecord ltr)
    {
        if (!ltr.LinetypeObjectId.IsValid)
            return "Continuous";
        var ltrLt = (LinetypeTableRecord)tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead);
        return ltrLt.Name;
    }

    internal static LineWeight ParseLineWeight(string value) =>
        Enum.TryParse<LineWeight>(value, out var result)
            ? result
            : LineWeight.LineWeight000;

    private static List<long> TransferEntities(Database db, Transaction tr, ObjectId sourceLayerId, ObjectId targetLayerId, bool makeByLayer = false)
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
                    if (makeByLayer)
                        SetEntityByLayer(ent);
                }
            }
        }

        return handles;
    }

    // Iterating every BlockTableRecord (model/paper space plus every block
    // definition, skipping xrefs) rather than just model space is what makes
    // this reach entities nested inside blocks -- AutoCAD "groups" have no
    // separate entity storage of their own (a group is just a named
    // selection of entities that already live in one of these spaces), so
    // group members are covered by the same pass with no special-casing.
    private static void MakeEntitiesByLayer(Database db, Transaction tr, ObjectId layerId)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent is not null && ent.LayerId == layerId)
                {
                    ent.UpgradeOpen();
                    SetEntityByLayer(ent);
                }
            }
        }
    }

    private static void SetEntityByLayer(Entity ent)
    {
        ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
        ent.Linetype = "ByLayer";
        ent.LineWeight = LineWeight.ByLayer;
        ent.Transparency = new Transparency(TransparencyMethod.ByLayer);
    }
}
