using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcLayerStandardizer.Core;

// Runs on AutoCAD's thread: watches document events and publishes the active
// drawing's identity and layer fingerprint into ActiveDrawingRegistry, so the
// pipe thread never has to call AutoCAD APIs.
public static class ActiveDrawingTracker
{
    private static readonly ConditionalWeakTable<Document, string> Ids = new();
    private static readonly RefreshGate Gate = new();
    private static bool _started;

    // Unique per open document for this AutoCAD session (file names can collide).
    public static string GetDrawingId(Document document) =>
        Ids.GetValue(document, _ => ActiveDrawingIds.Next());

    public static void Start()
    {
        if (_started) return;
        _started = true;

        var documents = Application.DocumentManager;
        documents.DocumentActivated += OnDocumentActivated;
        documents.DocumentCreated += OnDocumentCreated;
        documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        Application.Idle += OnIdle;
        foreach (Document document in documents)
            Watch(document);
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
        Application.Idle -= OnIdle;
        foreach (Document document in documents)
            Unwatch(document);
        ActiveDrawingRegistry.Clear();
    }

    private static void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e) => Refresh(e.Document);

    private static void OnDocumentCreated(object? sender, DocumentCollectionEventArgs e) => Watch(e.Document);

    private static void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        Unwatch(e.Document);
        var current = ActiveDrawingRegistry.Current;
        if (current is not null && current.DrawingId == GetDrawingId(e.Document))
            ActiveDrawingRegistry.Clear();
    }

    // Layers can change without a command ending (the Layer Properties palette,
    // this plugin's own Apply and Purge, LISP/ActiveX), so layer-table-record
    // events also mark the state dirty; the refresh itself waits for idle time.
    private static void Watch(Document document)
    {
        document.CommandEnded += OnCommandEnded;
        document.Database.ObjectAppended += OnLayerObjectChanged;
        document.Database.ObjectModified += OnLayerObjectChanged;
        document.Database.ObjectErased += OnLayerObjectErased;
    }

    private static void Unwatch(Document document)
    {
        document.CommandEnded -= OnCommandEnded;
        document.Database.ObjectAppended -= OnLayerObjectChanged;
        document.Database.ObjectModified -= OnLayerObjectChanged;
        document.Database.ObjectErased -= OnLayerObjectErased;
    }

    private static void OnCommandEnded(object? sender, CommandEventArgs e) => Gate.MarkDirty();

    private static void OnLayerObjectChanged(object? sender, ObjectEventArgs e)
    {
        if (e.DBObject is LayerTableRecord) Gate.MarkDirty();
    }

    private static void OnLayerObjectErased(object? sender, ObjectErasedEventArgs e)
    {
        if (e.DBObject is LayerTableRecord) Gate.MarkDirty();
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        if (Gate.TryConsume()) Refresh(Application.DocumentManager.MdiActiveDocument);
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
