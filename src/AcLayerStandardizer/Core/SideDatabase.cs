using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AcLayerStandardizer.Core;

public static class SideDatabase
{
    public static IReadOnlyDictionary<string, LayerProperties> LoadStandardLayers(string templatePath)
    {
        var layers = new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase);

        using var sideDb = OpenStandard(templatePath);

        using var tr = sideDb.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(sideDb.LayerTableId, OpenMode.ForRead);

        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!LayerHelper.ShouldSkip(ltr.Name))
            {
                layers[ltr.Name] = LayerProperties.FromLayerTableRecord(ltr, tr);
            }
        }

        tr.Commit();
        return layers;
    }

    // Read-only use: we never save the standard. Share read AND write so another session
    // holding it open for editing doesn't fail our open (the old read-only share denied writers).
    private static Database OpenStandard(string path)
    {
        try
        {
            return ReadDwg(path);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.FileSharingViolation)
        {
            // Fallback if AutoCAD still refuses: read a private copy and delete it once read.
            var copy = StandardFileCopy.CopyToTemp(path);
            try { return ReadDwg(copy); }
            finally { File.Delete(copy); }
        }
    }

    private static Database ReadDwg(string path)
    {
        var db = new Database(false, true);
        try
        {
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }
}
