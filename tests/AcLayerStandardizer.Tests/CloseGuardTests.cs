using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

// PendingRegistry is process-wide; the pipe tests write it too.
[Collection("Pending registry")]
public class CloseGuardTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<PendingDrawing> Pending(params (string Id, int Count)[] entries) =>
        entries.Select(e => new PendingDrawing(e.Id, e.Count)).ToList();

    [Fact]
    public void no_check_in_means_allow()
    {
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 3)), null, Now, "d1"));
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 3)), null, Now, null));
    }

    [Fact]
    public void a_stale_check_in_means_allow()
    {
        var sixSecondsAgo = Now - TimeSpan.FromSeconds(6);
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 3)), sixSecondsAgo, Now, "d1"));
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 3)), sixSecondsAgo, Now, null));
    }

    [Fact]
    public void a_fresh_check_in_with_pending_for_that_drawing_vetoes()
    {
        Assert.True(CloseGuard.ShouldVeto(Pending(("d1", 3)), Now - TimeSpan.FromSeconds(1), Now, "d1"));
    }

    [Fact]
    public void a_check_in_just_inside_the_freshness_window_vetoes()
    {
        var edge = Now - CloseGuard.CheckInFreshness + TimeSpan.FromMilliseconds(1);
        Assert.True(CloseGuard.ShouldVeto(Pending(("d1", 1)), edge, Now, "d1"));
    }

    [Fact]
    public void pending_in_another_drawing_does_not_veto_a_drawing_close()
    {
        Assert.False(CloseGuard.ShouldVeto(Pending(("d2", 3)), Now, Now, "d1"));
    }

    [Fact]
    public void a_quit_vetoes_when_any_drawing_is_pending()
    {
        Assert.True(CloseGuard.ShouldVeto(Pending(("d1", 0), ("d2", 2)), Now, Now, null));
        Assert.False(CloseGuard.ShouldVeto(Pending(), Now, Now, null));
    }

    [Fact]
    public void zero_counts_are_ignored()
    {
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 0)), Now, Now, "d1"));
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 0), ("d2", 0)), Now, Now, null));
    }

    [Fact]
    public void a_check_in_slightly_in_the_future_still_counts_as_fresh()
    {
        // A small clock step backwards keeps a just-made report fresh.
        Assert.True(CloseGuard.ShouldVeto(Pending(("d1", 1)), Now + TimeSpan.FromSeconds(2), Now, "d1"));
        Assert.True(CloseGuard.ShouldVeto(Pending(("d1", 1)), Now + CloseGuard.CheckInFreshness, Now, "d1"));
    }

    [Fact]
    public void a_check_in_far_in_the_future_is_stale()
    {
        // A big clock step backwards must not keep an old report fresh for as long
        // as the step: past the freshness window it no longer counts.
        var farAhead = Now + CloseGuard.CheckInFreshness + TimeSpan.FromMilliseconds(1);
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 1)), farAhead, Now, "d1"));
        Assert.False(CloseGuard.ShouldVeto(Pending(("d1", 1)), Now + TimeSpan.FromHours(1), Now, null));
    }

    [Fact]
    public void blocking_a_close_publishes_first_and_vetoes_only_after_a_successful_publish()
    {
        var order = new List<string>();
        Assert.True(CloseGuard.BlockClose(() => { order.Add("publish"); return true; }, () => order.Add("veto")));
        Assert.Equal(new[] { "publish", "veto" }, order);
    }

    [Fact]
    public void blocking_a_close_allows_it_when_the_publish_fails()
    {
        var vetoed = false;
        Assert.False(CloseGuard.BlockClose(() => false, () => vetoed = true));
        Assert.False(vetoed);
    }

    [Fact]
    public void blocking_a_close_allows_it_and_does_not_throw_when_the_publish_throws()
    {
        var vetoed = false;
        Assert.False(CloseGuard.BlockClose(() => throw new InvalidOperationException("feed broken"), () => vetoed = true));
        Assert.False(vetoed);
    }

    [Fact]
    public void blocking_a_close_does_not_throw_when_the_veto_throws()
    {
        Assert.False(CloseGuard.BlockClose(() => true, () => throw new InvalidOperationException("no veto")));
    }

    [Fact]
    public void a_null_pending_list_means_allow()
    {
        Assert.False(CloseGuard.ShouldVeto(null!, Now, Now, "d1"));
        Assert.False(CloseGuard.ShouldVeto(null!, Now, Now, null));
    }

    [Fact]
    public void registry_report_replaces_the_whole_previous_report()
    {
        PendingRegistry.Report(Pending(("d1", 2), ("d2", 1)), Now);
        var first = PendingRegistry.Current;
        Assert.Equal(2, first.Pending.Count);
        Assert.Equal(Now, first.LastCheckInUtc);

        var later = Now + TimeSpan.FromSeconds(1);
        PendingRegistry.Report(Pending(), later);
        var second = PendingRegistry.Current;
        Assert.Empty(second.Pending);
        Assert.Equal(later, second.LastCheckInUtc);
    }

    [Fact]
    public void registry_keeps_its_own_copy_of_the_report()
    {
        var list = new List<PendingDrawing> { new("d1", 2) };
        PendingRegistry.Report(list, Now);
        list.Add(new PendingDrawing("d2", 5));
        Assert.Single(PendingRegistry.Current.Pending);
    }

    [Fact]
    public void registry_treats_a_null_report_as_empty()
    {
        PendingRegistry.Report(Pending(("d1", 2)), Now);
        PendingRegistry.Report(null!, Now);
        Assert.Empty(PendingRegistry.Current.Pending);
        Assert.Equal(Now, PendingRegistry.Current.LastCheckInUtc);
    }

    [Fact]
    public async Task registry_survives_concurrent_reports_and_reads()
    {
        PendingRegistry.Report(Pending(("a", 1), ("b", 1)), Now);
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
                PendingRegistry.Report(Pending(($"w{w}", i % 3), ($"x{w}", 1)), Now);
        }));
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 4000; i++)
            {
                var (pending, at) = PendingRegistry.Current;
                // Each report has exactly two entries, so a torn read would show otherwise.
                if (at is not null) Assert.Equal(2, pending.Count);
            }
        });
        await Task.WhenAll(writers.Append(reader));
    }
}
