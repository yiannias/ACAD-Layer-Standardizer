using Autodesk.AutoCAD.DatabaseServices;

namespace AcLayerStandardizer.Core;

internal static class LayerReader
{
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
