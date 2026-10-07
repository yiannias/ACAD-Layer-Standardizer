using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AcLayerStandardizer.Core;

// Count of unapplied connections the mapping window holds for one drawing.
public sealed record PendingDrawing(string DrawingId, int Count);

// The window's last pending report and when it arrived. Written on the pipe
// thread (every PollEvents and ReplayClose), read on AutoCAD's thread by the
// close handler. Deliberately free of AutoCAD types.
public static class PendingRegistry
{
    private sealed record Snapshot(IReadOnlyList<PendingDrawing> Pending, DateTime? LastCheckInUtc);

    private static Snapshot _current = new(Array.Empty<PendingDrawing>(), null);

    // Replaces the whole previous report: an empty list clears everything pending.
    public static void Report(IReadOnlyList<PendingDrawing> pending, DateTime nowUtc)
    {
        var copy = pending is null ? Array.Empty<PendingDrawing>() : pending.ToArray();
        Volatile.Write(ref _current, new Snapshot(copy, nowUtc));
    }

    public static (IReadOnlyList<PendingDrawing> Pending, DateTime? LastCheckInUtc) Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _current);
            return (snapshot.Pending, snapshot.LastCheckInUtc);
        }
    }
}

public enum CloseKind { Drawing, Quit }

// Decides, instantly and without waiting for the window, whether a close must
// be cancelled. AutoCAD's thread is blocked while it asks, so this never calls
// anything that could wait.
public static class CloseGuard
{
    public static readonly TimeSpan CheckInFreshness = TimeSpan.FromSeconds(5);

    // AutoCAD closes drawings one at a time during QUIT, so the command still
    // running when a drawing closes tells a quit from a plain drawing close.
    public static CloseKind ClassifyClose(string? commandInProgress)
    {
        if (commandInProgress is null) return CloseKind.Drawing;
        var name = commandInProgress.Trim().TrimStart('_', '.', '\'');
        return string.Equals(name, "QUIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "EXIT", StringComparison.OrdinalIgnoreCase)
            ? CloseKind.Quit
            : CloseKind.Drawing;
    }

    // drawingId == null means a quit: veto if ANY drawing has pending connections.
    public static bool ShouldVeto(IReadOnlyList<PendingDrawing> pending, DateTime? lastCheckInUtc, DateTime nowUtc, string? drawingId)
    {
        if (pending is null || lastCheckInUtc is not { } checkedIn) return false;
        if (nowUtc - checkedIn >= CheckInFreshness) return false;
        // A check-in "from the future" (the clock stepped back) still counts as fresh
        // when it is only slightly ahead; further ahead it is stale, or a big step
        // would keep an old report fresh for as long as the step.
        if (checkedIn - nowUtc > CheckInFreshness) return false;
        return pending.Any(entry => entry is not null
            && entry.Count > 0
            && (drawingId is null || string.Equals(entry.DrawingId, drawingId, StringComparison.Ordinal)));
    }

    // Tells the window about the blocked close first and cancels the close only
    // once that worked: a close nobody will ask about must not stay cancelled.
    // Never throws; returns whether the close was cancelled.
    public static bool BlockClose(Func<bool> tryPublish, Action veto)
    {
        try
        {
            if (!tryPublish()) return false;
            veto();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
