using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

// PollEvents and ReplayClose write the process-wide PendingRegistry.
[Collection("Pending registry")]
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

    private static object PollRequest(long? since, int version = 4) => new
    {
        type = "PollEvents",
        payload = new { protocol_version = version, since, pending = Array.Empty<object>() }
    };

    [Fact]
    public async Task PollEvents_without_since_returns_a_reset()
    {
        var response = await SendAsync(PollRequest(null));
        Assert.Equal("Events", response.GetProperty("type").GetString());
        var payload = response.GetProperty("payload");
        Assert.True(payload.GetProperty("reset").GetBoolean());
        Assert.Equal(EventFeed.Shared.Head, payload.GetProperty("head").GetInt64());
    }

    [Fact]
    public async Task PollEvents_returns_events_published_after_since()
    {
        var before = EventFeed.Shared.Head;
        var seq = EventFeed.Shared.Publish("LayersChanged", new { drawing_id = "poll-doc", fingerprint = "ab12" });
        var response = await SendAsync(PollRequest(before));
        Assert.Equal("Events", response.GetProperty("type").GetString());
        var payload = response.GetProperty("payload");
        Assert.False(payload.GetProperty("reset").GetBoolean());
        var events = payload.GetProperty("events").EnumerateArray().ToList();
        var ours = events.Single(e => e.GetProperty("seq").GetInt64() == seq);
        Assert.Equal("LayersChanged", ours.GetProperty("type").GetString());
        Assert.Equal("poll-doc", ours.GetProperty("payload").GetProperty("drawing_id").GetString());
    }

    [Fact]
    public async Task PollEvents_rejects_an_unsupported_version()
    {
        var response = await SendAsync(PollRequest(null, 1));
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("Unsupported", response.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task PollEvents_is_not_written_to_the_ipc_log()
    {
        // The log is shared with other test processes and earlier runs, so only
        // lines stamped after this test started are considered.
        var started = DateTime.UtcNow;
        for (var i = 0; i < 3; i++) await SendAsync(PollRequest(null));
        await Task.Delay(200);

        var logPath = Path.Combine(Path.GetTempPath(), "AcLayerStandardizer-ipc.log");
        string all;
        using (var stream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            all = reader.ReadToEnd();
        var tail = all.Length > 50000 ? all.Substring(all.Length - 50000) : all;

        var recent = tail.Split('\n')
            .Where(l => l.Contains("PollEvents"))
            .Where(l => l.Length >= 28
                && DateTime.TryParse(l.Substring(0, 28), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                && at >= started)
            .ToList();
        Assert.Empty(recent);
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

    private static object LayersForDrawingRequest(int version, string drawingId) => new
    {
        type = "GetLayersForDrawing",
        payload = new { protocol_version = version, drawing_id = drawingId }
    };

    [Fact]
    public async Task GetLayersForDrawing_rejects_an_unsupported_version()
    {
        var response = await SendAsync(LayersForDrawingRequest(1, "x"));
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("Unsupported", response.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task GetLayersForDrawing_reports_an_unknown_drawing()
    {
        var response = await SendAsync(LayersForDrawingRequest(4, "no-such-drawing"));
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("no longer open", response.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task GetLayersForDrawing_rejects_malformed_payloads_without_dropping_the_connection()
    {
        var noPayload = await SendAsync(new { type = "GetLayersForDrawing" });
        Assert.Equal("Error", noPayload.GetProperty("type").GetString());
        var noId = await SendAsync(new { type = "GetLayersForDrawing", payload = new { protocol_version = 4 } });
        Assert.Equal("Error", noId.GetProperty("type").GetString());
    }

    private static object PollWithPending(object pending) => new
    {
        type = "PollEvents",
        payload = new { protocol_version = 4, since = (long?)null, pending }
    };

    [Fact]
    public async Task PollEvents_records_the_pending_report()
    {
        var before = DateTime.UtcNow;
        var response = await SendAsync(PollWithPending(new object[]
        {
            new { drawing_id = "rec-1", count = 3 },
            new { drawing_id = "rec-2", count = 0 }
        }));
        Assert.Equal("Events", response.GetProperty("type").GetString());

        var (pending, at) = PendingRegistry.Current;
        Assert.Equal(new[] { new PendingDrawing("rec-1", 3), new PendingDrawing("rec-2", 0) }, pending);
        Assert.NotNull(at);
        Assert.True(at >= before, "the poll counts as a check-in");

        // The next report replaces the whole previous one.
        await SendAsync(PollWithPending(Array.Empty<object>()));
        Assert.Empty(PendingRegistry.Current.Pending);
    }

    public static IEnumerable<object[]> MalformedPendingReports() =>
    [
        [null],
        ["rec-1"],
        [42],
        [new { drawing_id = "rec-1", count = 3 }],
        [new object[] { "rec-1", null, 4, new { count = 2 }, new { drawing_id = 9, count = 2 }, new { drawing_id = "neg", count = -2 }, new { drawing_id = "frac", count = 1.5 } }],
    ];

    [Theory]
    [MemberData(nameof(MalformedPendingReports))]
    public async Task PollEvents_treats_a_malformed_pending_report_as_an_empty_check_in(object pending)
    {
        PendingRegistry.Report(new[] { new PendingDrawing("stale", 5) }, DateTime.UtcNow - TimeSpan.FromMinutes(1));
        var before = DateTime.UtcNow;

        var response = await SendAsync(PollWithPending(pending));

        Assert.Equal("Events", response.GetProperty("type").GetString());
        var (recorded, at) = PendingRegistry.Current;
        Assert.Empty(recorded);
        Assert.True(at >= before, "a malformed report still counts as a check-in");
    }

    [Fact]
    public async Task PollEvents_without_a_pending_field_is_an_empty_check_in()
    {
        PendingRegistry.Report(new[] { new PendingDrawing("stale", 5) }, DateTime.UtcNow - TimeSpan.FromMinutes(1));
        var before = DateTime.UtcNow;

        var response = await SendAsync(new { type = "PollEvents", payload = new { protocol_version = 4 } });

        Assert.Equal("Events", response.GetProperty("type").GetString());
        Assert.Empty(PendingRegistry.Current.Pending);
        Assert.True(PendingRegistry.Current.LastCheckInUtc >= before);
    }

    private static object ReplayRequest(int version, string kind, string drawingId, object pending) => new
    {
        type = "ReplayClose",
        payload = new { protocol_version = version, kind, drawing_id = drawingId, pending }
    };

    [Fact]
    public async Task ReplayClose_reports_an_unknown_drawing()
    {
        // No AutoCAD in this process: the command context cannot run, so the
        // request must come back as an Error (not a crash or a dropped pipe), and
        // the carried pending report must already be recorded.
        PendingRegistry.Report(new[] { new PendingDrawing("no-such-drawing", 4) }, DateTime.UtcNow);
        var response = await SendAsync(ReplayRequest(4, "drawing", "no-such-drawing", Array.Empty<object>()));

        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.StartsWith("Could not close the drawing", response.GetProperty("payload").GetString());
        Assert.Empty(PendingRegistry.Current.Pending);

        // The connection still serves requests afterwards.
        var ping = await SendAsync(new { type = "Ping" });
        Assert.Equal("Pong", ping.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ReplayClose_rejects_an_unsupported_version_before_touching_the_registry()
    {
        PendingRegistry.Report(new[] { new PendingDrawing("keep", 2) }, DateTime.UtcNow);
        var response = await SendAsync(ReplayRequest(1, "drawing", "d1", Array.Empty<object>()));
        Assert.Equal("Error", response.GetProperty("type").GetString());
        Assert.Contains("Unsupported", response.GetProperty("payload").GetString());
        Assert.Equal(new[] { new PendingDrawing("keep", 2) }, PendingRegistry.Current.Pending);
    }

    [Fact]
    public async Task ReplayClose_rejects_malformed_payloads_without_dropping_the_connection()
    {
        PendingRegistry.Report(new[] { new PendingDrawing("keep", 2) }, DateTime.UtcNow);
        var requests = new object[]
        {
            new { type = "ReplayClose" },
            new { type = "ReplayClose", payload = "nope" },
            new { type = "ReplayClose", payload = new { kind = "drawing", drawing_id = "d1" } },
            new { type = "ReplayClose", payload = new { protocol_version = "4", kind = "drawing", drawing_id = "d1" } },
            ReplayRequest(4, "explode", "d1", null),
            new { type = "ReplayClose", payload = new { protocol_version = 4, drawing_id = "d1" } },
            new { type = "ReplayClose", payload = new { protocol_version = 4, kind = 3, drawing_id = "d1" } },
            ReplayRequest(4, "drawing", null, null),
            ReplayRequest(4, "drawing", "", null),
            new { type = "ReplayClose", payload = new { protocol_version = 4, kind = "drawing", drawing_id = 12 } },
        };
        foreach (var request in requests)
        {
            var response = await SendAsync(request);
            Assert.Equal("Error", response.GetProperty("type").GetString());
        }
        // A rejected request does not change what the close guard decides from.
        Assert.Equal(new[] { new PendingDrawing("keep", 2) }, PendingRegistry.Current.Pending);
    }
}
