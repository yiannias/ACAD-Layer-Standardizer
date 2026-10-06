using System.Text.Json;

namespace AcLayerStandardizer.Core;

// Pure protocol rules shared by the pipe server; no AutoCAD dependency.
public static class IpcProtocol
{
    public const int CurrentVersion = 3;
    public const int MinSupportedVersion = 2;

    public static bool IsSupportedVersion(int version) =>
        version >= MinSupportedVersion && version <= CurrentVersion;

    // Returns an error message when the request targets a different drawing
    // than the snapshot, or null when it is acceptable. A drawing id wins when
    // the client sent one; older clients fall back to the display name.
    public static string? CheckDrawingTarget(
        string snapshotDrawingId, string snapshotDrawingName, string? requestDrawingId, string requestDrawingName)
    {
        var matches = string.IsNullOrEmpty(requestDrawingId)
            ? string.Equals(requestDrawingName, snapshotDrawingName, StringComparison.OrdinalIgnoreCase)
            : string.Equals(requestDrawingId, snapshotDrawingId, StringComparison.Ordinal);
        return matches ? null : "The mapping window belongs to a different drawing. Close it and run LSTDR again.";
    }

    public static string BuildActiveDrawingResponse(ActiveDrawingState? state, long? knownRevision)
    {
        if (state is null)
            return JsonSerializer.Serialize(new { type = "NoActiveDrawing" });
        if (knownRevision == state.Revision)
            return JsonSerializer.Serialize(new { type = "ActiveDrawingUnchanged" });
        return JsonSerializer.Serialize(new
        {
            type = "ActiveDrawing",
            payload = new
            {
                drawing_id = state.DrawingId,
                display_name = state.DisplayName,
                layer_fingerprint = state.LayerFingerprint,
                revision = state.Revision
            }
        });
    }
}
