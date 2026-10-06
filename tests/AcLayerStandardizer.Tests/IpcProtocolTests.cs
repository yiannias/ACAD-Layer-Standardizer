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
    public void Supports_versions_2_and_3_only()
    {
        Assert.True(IpcProtocol.IsSupportedVersion(2));
        Assert.True(IpcProtocol.IsSupportedVersion(3));
        Assert.False(IpcProtocol.IsSupportedVersion(1));
        Assert.False(IpcProtocol.IsSupportedVersion(4));
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
}
