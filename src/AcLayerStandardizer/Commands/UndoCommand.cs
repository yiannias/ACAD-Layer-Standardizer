using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Commands;

public static class UndoCommand
{
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
                ltr.LinetypeObjectId = LayerApplier.GetLinetypeId(db, tr, renamed.Linetype);
                ltr.LineWeight = LayerApplier.ParseLineWeight(renamed.LineWeight);
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
                    LinetypeObjectId = LayerApplier.GetLinetypeId(db, tr, erased.Linetype),
                    LineWeight = LayerApplier.ParseLineWeight(erased.LineWeight),
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
}
