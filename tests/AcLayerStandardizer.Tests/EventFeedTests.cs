using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class EventFeedTests
{
    [Fact]
    public void Read_with_no_since_asks_for_a_reset()
    {
        var feed = new EventFeed();
        feed.Publish("A", new { });
        var page = feed.Read(null);
        Assert.True(page.Reset);
        Assert.Empty(page.Events);
        Assert.Equal(feed.Head, page.Head);
    }

    [Fact]
    public void Read_at_head_returns_nothing()
    {
        var feed = new EventFeed();
        feed.Publish("A", new { });
        var page = feed.Read(feed.Head);
        Assert.False(page.Reset);
        Assert.Empty(page.Events);
        Assert.Equal(feed.Head, page.Head);
    }

    [Fact]
    public void Read_returns_only_newer_events_in_order()
    {
        var feed = new EventFeed();
        var first = feed.Publish("A", new { });
        var second = feed.Publish("B", new { });
        var third = feed.Publish("C", new { });
        var page = feed.Read(first);
        Assert.False(page.Reset);
        Assert.Equal(new[] { second, third }, page.Events.Select(e => e.Seq).ToArray());
        Assert.Equal(new[] { "B", "C" }, page.Events.Select(e => e.Type).ToArray());
    }

    [Fact]
    public void Capacity_drops_the_oldest_and_an_old_since_resets()
    {
        var feed = new EventFeed(capacity: 3);
        var first = feed.Publish("E", new { });
        for (var i = 0; i < 4; i++) feed.Publish("E", new { });
        var page = feed.Read(first);
        Assert.True(page.Reset);
        Assert.Empty(page.Events);
        // retained are first+2..first+4; since == first+1 is still contiguous
        var ok = feed.Read(first + 1);
        Assert.False(ok.Reset);
        Assert.Equal(3, ok.Events.Count);
    }

    [Fact]
    public void A_since_from_another_process_resets()
    {
        var feed = new EventFeed();
        feed.Publish("A", new { });
        var page = feed.Read(feed.Head + 5000);
        Assert.True(page.Reset);
        Assert.Empty(page.Events);
    }

    [Fact]
    public void Sequence_numbers_start_from_the_base()
    {
        var feed = new EventFeed(baseSequence: 1000);
        Assert.Equal(1000, feed.Head);
        Assert.Equal(1001, feed.Publish("A", new { }));
    }
}
