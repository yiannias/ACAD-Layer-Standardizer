using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AcLayerStandardizer.Core;

public static class IpcBridgeServer
{
    public const string PipeName = "acad_layer_standardizer";
    private static CancellationTokenSource? _cts;
    private static Task? _serverTask;
    private static readonly object SnapshotLock = new();
    private static DrawingSnapshot? _drawingSnapshot;

    private sealed record TargetFilterSnapshot(string Name, string SortGroup, string[] Layers);
    private sealed record DrawingSnapshot(
        string DrawingName,
        string[] SourceLayers,
        string[] StandardLayers,
        string[] EmptyLayers,
        Dictionary<string, string> MemoryMappings,
        TargetFilterSnapshot[] TargetFilters);

    public static bool IsRunning => _serverTask is not null && !_serverTask.IsCompleted;

    // Called from an AutoCAD command while its document context is valid. The
    // pipe worker serves this immutable snapshot and never touches AutoCAD APIs.
    public static void SetDrawingSnapshot(
        string drawingName,
        IEnumerable<string> sourceLayers,
        IEnumerable<string> standardLayers,
        IEnumerable<string> emptyLayers,
        IReadOnlyDictionary<string, string> memoryMappings,
        IEnumerable<(string Name, string SortGroup, IEnumerable<string> Layers)> targetFilters)
    {
        var snapshot = new DrawingSnapshot(
            drawingName,
            sourceLayers.ToArray(),
            standardLayers.ToArray(),
            emptyLayers.ToArray(),
            memoryMappings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            targetFilters.Select(filter => new TargetFilterSnapshot(filter.Name, filter.SortGroup, filter.Layers.ToArray())).ToArray());
        lock (SnapshotLock) _drawingSnapshot = snapshot;
    }

    public static void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoop(_cts.Token));
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _serverTask = null;
    }

    private static async Task ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
#pragma warning disable CA1416 // Validate platform compatibility (Windows only plugin)
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };

                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line)) break;

                    string responseJson = HandleMessage(line);
                    await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
                }
#pragma warning restore CA1416
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Delay slightly before retrying loop on pipe error
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    private static string HandleMessage(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            string msgType = root.GetProperty("type").GetString() ?? "";

            switch (msgType)
            {
                case "Ping":
                    return JsonSerializer.Serialize(new { type = "Pong" });

                case "GetDrawingLayers":
                    var layers = GetDrawingSnapshot()?.SourceLayers ?? Array.Empty<string>();
                    return JsonSerializer.Serialize(new { type = "Layers", payload = layers });

                case "GetDrawingSnapshot":
                    var snapshot = GetDrawingSnapshot();
                    if (snapshot is null)
                        return JsonSerializer.Serialize(new { type = "Error", payload = "No drawing snapshot is available. Run LSTDR from an active drawing." });
                    return JsonSerializer.Serialize(new
                    {
                        type = "DrawingSnapshot",
                        payload = new
                        {
                            drawing_name = snapshot.DrawingName,
                            source_layers = snapshot.SourceLayers,
                            standard_layers = snapshot.StandardLayers,
                            empty_layers = snapshot.EmptyLayers,
                            memory_mappings = snapshot.MemoryMappings,
                            target_filters = snapshot.TargetFilters.Select(filter => new
                            {
                                name = filter.Name,
                                sort_group = filter.SortGroup,
                                layers = filter.Layers
                            }).ToArray()
                        }
                    });

                default:
                    return JsonSerializer.Serialize(new { type = "Error", payload = $"Unknown request type: {msgType}" });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { type = "Error", payload = ex.Message });
        }
    }

    private static DrawingSnapshot? GetDrawingSnapshot()
    {
        lock (SnapshotLock) return _drawingSnapshot;
    }
}
