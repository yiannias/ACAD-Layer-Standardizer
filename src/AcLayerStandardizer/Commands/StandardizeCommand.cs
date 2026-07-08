using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcLayerStandardizer.Commands;

public static class StandardizeCommand
{
    [CommandMethod("ACLAYERSTD", "StandardizeLayers", CommandFlags.Modal)]
    public static void StandardizeLayers()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        var db = doc.Database;

        ed.WriteMessage("\nAcLayerStandardizer: Analyzing layers...");

        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

        var count = 0;
        foreach (ObjectId id in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!ltr.Name.StartsWith("*") && ltr.Name != "0" && ltr.Name != "Defpoints")
            {
                ed.WriteMessage($"\n  Layer: {ltr.Name}");
                count++;
            }
        }

        tr.Commit();

        ed.WriteMessage($"\nFound {count} user-defined layers.");
    }
}
