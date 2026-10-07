using System.Text.Json;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class IpcProtocolTests
{
    [Fact]
    public void Fingerprint_ignores_order_and_case()
    {
        Assert.Equal(
            LayerFingerprint.Compute(["A-WALL", "a-door"]),
            LayerFingerprint.Compute(["A-DOOR", "a-wall"]));
    }

    [Fact]
    public void Fingerprint_ignores_system_and_xref_layers()
    {
        Assert.Equal(
            LayerFingerprint.Compute(["A-WALL"]),
            LayerFingerprint.Compute(["A-WALL", "Defpoints", "X|LAYER"]));
    }

    [Fact]
    public void Fingerprint_changes_when_a_layer_is_added()
    {
        Assert.NotEqual(
            LayerFingerprint.Compute(["A-WALL"]),
            LayerFingerprint.Compute(["A-WALL", "A-DOOR"]));
    }

    [Fact]
    public void Version_4_is_supported_and_5_is_not()
    {
        Assert.True(IpcProtocol.IsSupportedVersion(2));
        Assert.True(IpcProtocol.IsSupportedVersion(3));
        Assert.True(IpcProtocol.IsSupportedVersion(4));
        Assert.False(IpcProtocol.IsSupportedVersion(1));
        Assert.False(IpcProtocol.IsSupportedVersion(5));
        Assert.Equal(4, IpcProtocol.CurrentVersion);
    }

    [Fact]
    public void Events_response_has_head_reset_and_events()
    {
        var page = new FeedPage(12, false, new[] { new FeedEvent(12, "Saved", new { name = "A.dwg" }) });
        using var doc = JsonDocument.Parse(IpcProtocol.BuildEventsResponse(page));
        Assert.Equal("Events", doc.RootElement.GetProperty("type").GetString());
        var payload = doc.RootElement.GetProperty("payload");
        Assert.Equal(12, payload.GetProperty("head").GetInt64());
        Assert.False(payload.GetProperty("reset").GetBoolean());
        var ev = payload.GetProperty("events")[0];
        Assert.Equal(12, ev.GetProperty("seq").GetInt64());
        Assert.Equal("Saved", ev.GetProperty("type").GetString());
        Assert.Equal("A.dwg", ev.GetProperty("payload").GetProperty("name").GetString());
    }

    [Fact]
    public void Same_name_different_id_is_rejected()
    {
        Assert.NotNull(IpcProtocol.CheckDrawingTarget("doc-1", "A.dwg", "doc-2", "A.dwg"));
    }

    [Fact]
    public void Matching_id_is_accepted_even_if_names_differ()
    {
        Assert.Null(IpcProtocol.CheckDrawingTarget("doc-1", "A.dwg", "doc-1", "a.dwg"));
    }

    [Fact]
    public void Missing_id_falls_back_to_name()
    {
        Assert.Null(IpcProtocol.CheckDrawingTarget("doc-1", "A.dwg", null, "A.DWG"));
        Assert.NotNull(IpcProtocol.CheckDrawingTarget("doc-1", "A.dwg", null, "B.dwg"));
    }

    [Fact]
    public void Active_drawing_response_shapes()
    {
        var state = new ActiveDrawingState("doc-1", "A.dwg", "ab12", 3);

        using (var full = JsonDocument.Parse(IpcProtocol.BuildActiveDrawingResponse(state, null)))
        {
            Assert.Equal("ActiveDrawing", full.RootElement.GetProperty("type").GetString());
            var payload = full.RootElement.GetProperty("payload");
            Assert.Equal("doc-1", payload.GetProperty("drawing_id").GetString());
            Assert.Equal("A.dwg", payload.GetProperty("display_name").GetString());
            Assert.Equal("ab12", payload.GetProperty("layer_fingerprint").GetString());
            Assert.Equal(3, payload.GetProperty("revision").GetInt64());
        }

        using (var unchanged = JsonDocument.Parse(IpcProtocol.BuildActiveDrawingResponse(state, 3)))
            Assert.Equal("ActiveDrawingUnchanged", unchanged.RootElement.GetProperty("type").GetString());

        using (var none = JsonDocument.Parse(IpcProtocol.BuildActiveDrawingResponse(null, 3)))
            Assert.Equal("NoActiveDrawing", none.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Drawing_ids_are_unique_and_share_a_per_process_nonce()
    {
        var first = ActiveDrawingIds.Next();
        var second = ActiveDrawingIds.Next();
        Assert.NotEqual(first, second);
        Assert.Equal(first.Substring(0, first.IndexOf("-doc-")), second.Substring(0, second.IndexOf("-doc-")));
        Assert.True(first.IndexOf("-doc-") >= 8, "id needs a nonce prefix so two AutoCAD processes cannot collide");
    }

    [Fact]
    public void Refresh_gate_reports_dirty_once_per_mark()
    {
        var gate = new RefreshGate();
        Assert.False(gate.TryConsume());
        gate.MarkDirty();
        gate.MarkDirty();
        Assert.True(gate.TryConsume());
        Assert.False(gate.TryConsume());
    }

    private static IReadOnlyList<PendingDrawing> ParsePending(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return IpcProtocol.ParsePending(document.RootElement);
    }

    [Fact]
    public void ParsePending_reads_well_formed_entries()
    {
        var pending = ParsePending("""{"pending":[{"drawing_id":"d1","count":2},{"drawing_id":"d2","count":0}]}""");
        Assert.Equal(new[] { new PendingDrawing("d1", 2), new PendingDrawing("d2", 0) }, pending);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"pending":null}""")]
    [InlineData("""{"pending":"d1"}""")]
    [InlineData("""{"pending":{"drawing_id":"d1","count":2}}""")]
    [InlineData("""{"pending":7}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void ParsePending_treats_a_missing_or_non_array_report_as_empty(string payloadJson)
    {
        Assert.Empty(ParsePending(payloadJson));
    }

    [Fact]
    public void ParsePending_skips_malformed_entries_and_keeps_the_rest()
    {
        var pending = ParsePending("""
            {"pending":[
              {"drawing_id":"ok","count":3},
              {"count":1},
              {"drawing_id":null,"count":1},
              {"drawing_id":5,"count":1},
              {"drawing_id":"","count":1},
              {"drawing_id":"neg","count":-1},
              {"drawing_id":"frac","count":1.5},
              {"drawing_id":"str","count":"2"},
              {"drawing_id":"huge","count":99999999999},
              {"drawing_id":"nocount"},
              "d1", null, 4, []
            ]}
            """);
        Assert.Equal(new[] { new PendingDrawing("ok", 3) }, pending);
    }

    [Fact]
    public void Replay_target_maps_only_a_missing_drawing_to_no_longer_open()
    {
        Assert.Equal("The drawing is no longer open.", IpcProtocol.CheckReplayTarget(found: false, isActive: false));
        Assert.Equal("Switch to that drawing and close it again.", IpcProtocol.CheckReplayTarget(found: true, isActive: false));
        Assert.Null(IpcProtocol.CheckReplayTarget(found: true, isActive: true));
    }
}
