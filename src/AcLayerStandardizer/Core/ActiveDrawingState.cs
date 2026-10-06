using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Text;

namespace AcLayerStandardizer.Core;

// Immutable summary of the drawing AutoCAD has active. Published by
// ActiveDrawingTracker on the AutoCAD thread and read by the pipe thread.
public sealed record ActiveDrawingState(string DrawingId, string DisplayName, string LayerFingerprint, long Revision);

// Latest active-drawing state, published by ActiveDrawingTracker on the AutoCAD
// thread and read by the pipe thread. Deliberately free of AutoCAD types.
public static class ActiveDrawingRegistry
{
    private static readonly object Gate = new();
    private static ActiveDrawingState? _current;
    // Random per-process base: known_revision from a client that talked to another
    // AutoCAD process must not accidentally equal this process's revision.
    private static long _revision = (long)(Guid.NewGuid().GetHashCode() & 0x3FFFFFFF) << 20;

    public static ActiveDrawingState? Current
    {
        get { lock (Gate) return _current; }
    }

    // Revision advances only when the drawing id, display name, or layer
    // fingerprint actually changed (or when nothing was published before).
    public static ActiveDrawingState Publish(string drawingId, string displayName, string layerFingerprint)
    {
        lock (Gate)
        {
            if (_current is not null
                && _current.DrawingId == drawingId
                && _current.DisplayName == displayName
                && _current.LayerFingerprint == layerFingerprint)
                return _current;

            _current = new ActiveDrawingState(drawingId, displayName, layerFingerprint, ++_revision);
            return _current;
        }
    }

    public static void Clear()
    {
        lock (Gate) _current = null;
    }
}

// Drawing ids carry a per-process nonce: several AutoCAD processes can listen on
// the same pipe name, and a bare counter ("doc-1") would collide between them.
public static class ActiveDrawingIds
{
    private static readonly string Nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
    private static int _next;

    public static string Next() => $"{Nonce}-doc-{Interlocked.Increment(ref _next)}";
}

// Set from any thread when something may have changed; consumed once by the
// tracker when AutoCAD is idle, so a burst of edits costs one refresh.
public sealed class RefreshGate
{
    private int _dirty;

    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    public bool TryConsume() => Interlocked.Exchange(ref _dirty, 0) == 1;
}

public static class LayerFingerprint
{
    // Order- and case-insensitive digest of the layers the tool can edit, so
    // "did the layer set change?" is a cheap string comparison.
    public static string Compute(IEnumerable<string> layerNames)
    {
        var canonical = layerNames
            .Where(name => !LayerHelper.ShouldSkip(name))
            .Select(name => name.ToUpperInvariant())
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", canonical)));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
    }
}
