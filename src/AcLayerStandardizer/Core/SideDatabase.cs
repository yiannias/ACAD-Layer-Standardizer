using Autodesk.AutoCAD.DatabaseServices;

namespace AcLayerStandardizer.Core;

public static class SideDatabase
{
    public static IReadOnlyDictionary<string, LayerProperties> LoadStandardLayers(string templatePath)
    {
        var layers = new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase);

        using var sideDb = new Database(false, true);
        sideDb.ReadDwgFile(templatePath, FileOpenMode.OpenForReadAndReadShare, true, "");

        using var tr = sideDb.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(sideDb.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!IsSystemLayer(ltr))
            {
                layers[ltr.Name] = LayerProperties.FromLayerTableRecord(ltr, tr);
            }
        }

        tr.Commit();
        return layers;
    }

    private static bool IsSystemLayer(LayerTableRecord ltr)
    {
        var name = ltr.Name;
        return name is "0" or "Defpoints" or "AsBuilt"
            || name.StartsWith("*") || name.StartsWith("_");
    }
}
