using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class IpcBridgeServerTests
{
    static IpcBridgeServerTests()
    {
        // dotnet test runs each target framework in its own process at the same
        // time; give this process a private pipe so they cannot cross-talk.
        IpcBridgeServer.PipeName = "acad_layer_standardizer_tests_" + System.Diagnostics.Process.GetCurrentProcess().Id;
    }

    [Fact]
    public async Task Server_responds_to_ping()
    {
        IpcBridgeServer.Start();
        Assert.True(IpcBridgeServer.IsRunning);

        try
        {
            using var client = new NamedPipeClientStream(".", IpcBridgeServer.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(3000);

            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "Ping" }));

            string? response = await reader.ReadLineAsync();
            Assert.NotNull(response);

            using var doc = JsonDocument.Parse(response);
            Assert.Equal("Pong", doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            // Left running on purpose; see SendAsync.
        }
    }

    private static async Task<JsonElement> SendAsync(object request)
    {
        // The server is left running for the whole test process: stopping and
        // restarting it between requests races the old pipe instance's shutdown.
        IpcBridgeServer.Start();
        using var client = new NamedPipeClientStream(".", IpcBridgeServer.PipeName, PipeDirection.InOut);
        await client.ConnectAsync(3000);

        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        string? response = await reader.ReadLineAsync();
        Assert.NotNull(response);
        return JsonDocument.Parse(response).RootElement.Clone();
    }

    private static object ApplyPlanRequest(int version) => new
    {
        type = "ApplyPlan",
        payload = new
        {
            protocol_version = version,
            drawing_name = "A.dwg",
            mappings = Array.Empty<object>(),
            remember = false,
            properties = new { match_color = true, match_linetype = true, match_lineweight = true, make_by_layer = false }
        }
    };

    [Fact]
    public async Task GetActiveDrawing_with_no_tracked_drawing_returns_NoActiveDrawing()
    {
        ActiveDrawingRegistry.Clear();
        var response = await SendAsync(new { type = "GetActiveDrawing", payload = new { known_revision = (long?)null } });
        Assert.Equal("NoActiveDrawing", response.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ApplyPlan_rejects_unsupported_protocol_version()
    {
        var response = await SendAsync(ApplyPlanRequest(1));
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("Unsupported", response.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task GetActiveDrawing_returns_published_state_then_Unchanged()
    {
        ActiveDrawingRegistry.Clear();
        var state = ActiveDrawingRegistry.Publish("doc-9", "B.dwg", "ff00");
        try
        {
            var full = await SendAsync(new { type = "GetActiveDrawing", payload = new { known_revision = (long?)null } });
            Assert.Equal("ActiveDrawing", full.GetProperty("type").GetString());
            Assert.Equal("doc-9", full.GetProperty("payload").GetProperty("drawing_id").GetString());

            var same = await SendAsync(new { type = "GetActiveDrawing", payload = new { known_revision = state.Revision } });
            Assert.Equal("ActiveDrawingUnchanged", same.GetProperty("type").GetString());
        }
        finally { ActiveDrawingRegistry.Clear(); }
    }

    [Fact]
    public void Registry_bumps_revision_only_when_something_changes()
    {
        ActiveDrawingRegistry.Clear();
        var first = ActiveDrawingRegistry.Publish("doc-1", "A.dwg", "aa");
        var same = ActiveDrawingRegistry.Publish("doc-1", "A.dwg", "aa");
        var layersChanged = ActiveDrawingRegistry.Publish("doc-1", "A.dwg", "bb");
        var otherDrawing = ActiveDrawingRegistry.Publish("doc-2", "A.dwg", "bb");
        Assert.Equal(first.Revision, same.Revision);
        Assert.Equal(first.Revision + 1, layersChanged.Revision);
        Assert.Equal(layersChanged.Revision + 1, otherDrawing.Revision);
        ActiveDrawingRegistry.Clear();
        Assert.Null(ActiveDrawingRegistry.Current);
    }

    [Fact]
    public void Registry_revisions_start_far_from_a_small_constant_so_processes_do_not_collide()
    {
        ActiveDrawingRegistry.Clear();
        var state = ActiveDrawingRegistry.Publish("p-doc-1", "A.dwg", "aa");
        Assert.True(state.Revision > 1_000_000, "known_revision from another AutoCAD process must not look current");
        ActiveDrawingRegistry.Clear();
    }

    [Fact]
    public async Task GetStandardLayers_with_missing_file_returns_Error_without_touching_AutoCAD()
    {
        var response = await SendAsync(new
        {
            type = "GetStandardLayers",
            payload = new { protocol_version = 3, path = @"C:\definitely\not\here.dwg" }
        });
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("could not be found", response.GetProperty("payload").GetString());
    }
}
