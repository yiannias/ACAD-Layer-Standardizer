using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcLayerStandardizer.Core;

// Runs on AutoCAD's thread: watches document events and publishes the active
// drawing's identity and layer fingerprint into ActiveDrawingRegistry, so the
// pipe thread never has to call AutoCAD APIs.
public static class ActiveDrawingTracker
{
    private static readonly ConditionalWeakTable<Document, string> Ids = new();
    private static int _nextId;
    private static bool _started;

    // Unique per open document for this AutoCAD session (file names can collide).
    public static string GetDrawingId(Document document) =>
        Ids.GetValue(document, _ => "doc-" + Interlocked.Increment(ref _nextId));

    public static void Start()
    {
        if (_started) return;
        _started = true;

        var documents = Application.DocumentManager;
        documents.DocumentActivated += OnDocumentActivated;
        documents.DocumentCreated += OnDocumentCreated;
        documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        foreach (Document document in documents)
            document.CommandEnded += OnCommandEnded;
        Refresh(documents.MdiActiveDocument);
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;

        var documents = Application.DocumentManager;
        documents.DocumentActivated -= OnDocumentActivated;
        documents.DocumentCreated -= OnDocumentCreated;
        documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
        foreach (Document document in documents)
            document.CommandEnded -= OnCommandEnded;
        ActiveDrawingRegistry.Clear();
    }

    private static void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e) => Refresh(e.Document);

    private static void OnDocumentCreated(object? sender, DocumentCollectionEventArgs e) =>
        e.Document.CommandEnded += OnCommandEnded;

    private static void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        e.Document.CommandEnded -= OnCommandEnded;
        var current = ActiveDrawingRegistry.Current;
        if (current is not null && current.DrawingId == GetDrawingId(e.Document))
            ActiveDrawingRegistry.Clear();
    }

    // A command may have added, deleted, or renamed layers.
    private static void OnCommandEnded(object? sender, CommandEventArgs e)
    {
        if (sender is Document document && ReferenceEquals(document, Application.DocumentManager.MdiActiveDocument))
            Refresh(document);
    }

    private static void Refresh(Document? document)
    {
        try
        {
            if (document is null)
            {
                ActiveDrawingRegistry.Clear();
                return;
            }

            ActiveDrawingRegistry.Publish(
                GetDrawingId(document),
                Path.GetFileName(document.Name),
                LayerFingerprint.Compute(ReadLayerNames(document.Database)));
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Layer Standardizer could not refresh the active drawing: {ex.Message}");
        }
    }

    private static List<string> ReadLayerNames(Database database)
    {
        var names = new List<string>();
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in layers)
            names.Add(((LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name);
        return names;
    }
}
