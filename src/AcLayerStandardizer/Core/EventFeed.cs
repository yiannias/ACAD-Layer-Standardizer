namespace AcLayerStandardizer.Core;

public sealed record FeedEvent(long Seq, string Type, object Payload);

public sealed record FeedPage(long Head, bool Reset, IReadOnlyList<FeedEvent> Events);

// Bounded, thread-safe ring of recent connector events. Published from the
// AutoCAD thread, polled by the pipe thread with the last sequence it saw.
public sealed class EventFeed
{
    public const int DefaultCapacity = 200;

    private readonly object _gate = new();
    private readonly List<FeedEvent> _events = new();
    private readonly int _capacity;
    private long _head;

    public EventFeed(int capacity = DefaultCapacity, long baseSequence = 0)
    {
        _capacity = Math.Max(1, capacity);
        _head = baseSequence;
    }

    // Random per-process base: a since from another AutoCAD process must not
    // land inside this process's window.
    public static EventFeed Shared { get; } =
        new EventFeed(DefaultCapacity, (long)(Guid.NewGuid().GetHashCode() & 0x3FFFFFFF) << 20);

    public long Head
    {
        get { lock (_gate) return _head; }
    }

    public long Publish(string type, object payload)
    {
        lock (_gate)
        {
            _head++;
            _events.Add(new FeedEvent(_head, type, payload));
            if (_events.Count > _capacity)
                _events.RemoveRange(0, _events.Count - _capacity);
            return _head;
        }
    }

    public FeedPage Read(long? since)
    {
        lock (_gate)
        {
            if (since is null || since > _head)
                return new FeedPage(_head, true, Array.Empty<FeedEvent>());
            if (since == _head)
                return new FeedPage(_head, false, Array.Empty<FeedEvent>());

            // Oldest retained seq is _head - count + 1; since must be at least one before it.
            var oldest = _head - _events.Count + 1;
            if (since < oldest - 1)
                return new FeedPage(_head, true, Array.Empty<FeedEvent>());

            var newer = _events.Where(e => e.Seq > since).ToList();
            return new FeedPage(_head, false, newer);
        }
    }
}
