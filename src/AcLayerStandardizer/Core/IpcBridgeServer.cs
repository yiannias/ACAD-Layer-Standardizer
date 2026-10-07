using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using AcLayerStandardizer.Commands;
using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Core;

public static class IpcBridgeServer
{
    // Settable only so test processes (one per target framework, run in parallel)
    // can use private pipes; production always uses the default.
    public static string PipeName { get; set; } = "acad_layer_standardizer";
    private static CancellationTokenSource? _cts;
    private static Task? _serverTask;
    private static readonly object SnapshotLock = new();
    private static readonly object LogLock = new();
    private static DrawingSnapshot? _drawingSnapshot;

    private sealed record TargetFilterSnapshot(string Name, string SortGroup, string[] Layers);
    private sealed record DrawingSnapshot(
        Document Document,
        string DrawingId,
        string DrawingName,
        double HeuristicThreshold,
        string TemplateName,
        string TemplatePath,
        string[] SourceLayers,
        string[] StandardLayers,
        string[] EmptyLayers,
        IReadOnlyDictionary<string, LayerProperties> StandardLayerProperties,
        Dictionary<string, string> MemoryMappings,
        string MemoryFilePath,
        TargetFilterSnapshot[] TargetFilters,
        string[] AlwaysHiddenTargets);

    public static bool IsRunning => _serverTask is not null && !_serverTask.IsCompleted;

    // Called from an AutoCAD command while its document context is valid. The
    // pipe worker serves this immutable snapshot and never touches AutoCAD APIs.
    public static void SetDrawingSnapshot(
        Document document,
        string drawingName,
        double heuristicThreshold,
        IEnumerable<string> sourceLayers,
        IEnumerable<string> standardLayers,
        IEnumerable<string> emptyLayers,
        IReadOnlyDictionary<string, LayerProperties> standardLayerProperties,
        IReadOnlyDictionary<string, string> memoryMappings,
        string memoryFilePath,
        IEnumerable<(string Name, string SortGroup, IEnumerable<string> Layers)> targetFilters,
        string templatePath,
        IEnumerable<string> alwaysHiddenTargets)
    {
        var snapshot = new DrawingSnapshot(
            document,
            ActiveDrawingTracker.GetDrawingId(document),
            drawingName,
            heuristicThreshold,
            string.IsNullOrEmpty(templatePath) ? "" : Path.GetFileName(templatePath),
            templatePath,
            sourceLayers.ToArray(),
            standardLayers.ToArray(),
            emptyLayers.ToArray(),
            standardLayerProperties,
            memoryMappings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            memoryFilePath,
            targetFilters.Select(filter => new TargetFilterSnapshot(filter.Name, filter.SortGroup, filter.Layers.ToArray())).ToArray(),
            alwaysHiddenTargets.ToArray());
        lock (SnapshotLock) _drawingSnapshot = snapshot;
        Log($"Snapshot stored: sources={snapshot.SourceLayers.Length}; standards={snapshot.StandardLayers.Length}.");
    }

    public static void Start()
    {
        if (IsRunning)
        {
            Log("Start requested while server task is active.");
            return;
        }

        Log("Starting named-pipe server.");
        Log($"Server assembly loaded from: {typeof(IpcBridgeServer).Assembly.Location}");
        Log("Named-pipe buffers configured: input=4096, output=4096; UTF-8 response has no BOM.");
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
                    PipeOptions.Asynchronous,
                    4096,
                    4096);

                Log("Pipe listening.");
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                Log("Client connected.");

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                // Do not emit a UTF-8 BOM on the pipe. The Rust client expects
                // each response line to begin directly with JSON, and the
                // preamble can also block before the server starts reading.
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line)) break;

                    Log($"Request received: {GetMessageType(line)}.");
                    string responseJson = await HandleMessageAsync(line).ConfigureAwait(false);
                    await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
                    Log($"Response sent: {GetMessageType(responseJson)}.");
                }
#pragma warning restore CA1416
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Server error: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Layer Standardizer IPC pipe error: {ex}");
                // Delay slightly before retrying loop on pipe error
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    private static async Task<string> HandleMessageAsync(string requestJson)
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

                case "GetActiveDrawing":
                    long? knownRevision = null;
                    if (root.TryGetProperty("payload", out var activePayload)
                        && activePayload.ValueKind == JsonValueKind.Object
                        && activePayload.TryGetProperty("known_revision", out var known)
                        && known.ValueKind == JsonValueKind.Number)
                        knownRevision = known.GetInt64();
                    return IpcProtocol.BuildActiveDrawingResponse(ActiveDrawingRegistry.Current, knownRevision);

                case "GetDrawingSnapshot":
                    var snapshot = GetDrawingSnapshot();
                    if (snapshot is null)
                        return JsonSerializer.Serialize(new { type = "Error", payload = "No drawing snapshot is available. Run LSTDR from an active drawing." });
                    return SerializeSnapshot("DrawingSnapshot", snapshot);

                case "GetStandardLayers":
                    if (CheckProtocolVersion(root) is { } versionErrorGetStandard) return versionErrorGetStandard;
                    var standardPath = root.GetProperty("payload").GetProperty("path").GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(standardPath) || !File.Exists(standardPath))
                        return Error("The selected standard file could not be found.");
                    return await GetStandardLayersAsync(standardPath).ConfigureAwait(false);

                case "ApplyPlan":
                    if (CheckProtocolVersion(root) is { } versionErrorApplyPlan) return versionErrorApplyPlan;
                    return await ApplyPlanAsync(root).ConfigureAwait(false);

                case "PurgeEmptyLayers":
                    if (CheckProtocolVersion(root) is { } versionErrorPurgeEmptyLayers) return versionErrorPurgeEmptyLayers;
                    return await PurgeEmptyLayersAsync(root).ConfigureAwait(false);

                case "LoadStandard":
                    if (CheckProtocolVersion(root) is { } versionErrorLoadStandard) return versionErrorLoadStandard;
                    return await LoadStandardAsync(root).ConfigureAwait(false);

                default:
                    return JsonSerializer.Serialize(new { type = "Error", payload = $"Unknown request type: {msgType}" });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { type = "Error", payload = ex.Message });
        }
    }

    // Kept separate from the AutoCAD-touching handlers so an unsupported client
    // is rejected without running (or even loading) any AutoCAD code.
    private static string? CheckProtocolVersion(JsonElement root)
    {
        var version = root.GetProperty("payload").GetProperty("protocol_version").GetInt32();
        return IpcProtocol.IsSupportedVersion(version) ? null : Error($"Unsupported protocol version {version}.");
    }

    // Reads the standard (template) layer names for the Rust window and caches the
    // layers' properties in the snapshot for Apply. Unlike LoadStandard it neither
    // writes config nor touches memory/categories: the Rust app owns those.
    private static async Task<string> GetStandardLayersAsync(string path)
    {
        var current = GetDrawingSnapshot();
        if (current is null) return Error("No active drawing snapshot is available. Run LSTDR again.");

        var completion = new TaskCompletionSource<DrawingSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, current.Document))
                        throw new InvalidOperationException("The source drawing is no longer active. Run LSTDR again in that drawing.");

                    var properties = SideDatabase.LoadStandardLayers(path);
                    var names = properties.Keys.OrderBy(n => n == "0" ? 0 : 1)
                        .ThenBy(n => n, NaturalSortComparer.Instance).ToArray();
                    var updated = current with
                    {
                        TemplateName = Path.GetFileName(path),
                        TemplatePath = path,
                        StandardLayers = names,
                        StandardLayerProperties = properties
                    };
                    // Another LSTDR may have stored a newer snapshot while the standard was
                    // loading; replacing it with this one would resurrect a stale drawing.
                    lock (SnapshotLock)
                    {
                        if (!ReferenceEquals(_drawingSnapshot, current))
                            throw new InvalidOperationException("The drawing window was replaced while the standard was loading. Run LSTDR again.");
                        _drawingSnapshot = updated;
                    }
                    current.Document.Editor.WriteMessage($"\nStandard loaded: {Path.GetFileName(path)} ({names.Length} layers).");
                    completion.TrySetResult(updated);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                return Task.CompletedTask;
            }, null);
            var loaded = await completion.Task.ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                type = "StandardLayers",
                payload = new { template_name = loaded.TemplateName, template_path = loaded.TemplatePath, layers = loaded.StandardLayers }
            });
        }
        catch (Exception ex) { return Error($"Could not load the standard file: {ex.GetBaseException().Message}"); }
    }

    private static async Task<string> LoadStandardAsync(JsonElement root)
    {
        var payload = root.GetProperty("payload");
        var version = payload.GetProperty("protocol_version").GetInt32();
        if (!IpcProtocol.IsSupportedVersion(version)) return Error($"Unsupported standard-load protocol version {version}.");
        var path = payload.GetProperty("path").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Error("The selected standard file could not be found.");
        var current = GetDrawingSnapshot();
        if (current is null) return Error("No active drawing snapshot is available. Run LSTDR again.");

        var completion = new TaskCompletionSource<DrawingSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, current.Document))
                        throw new InvalidOperationException("The source drawing is no longer active. Run LSTDR again in that drawing.");

                    var properties = SideDatabase.LoadStandardLayers(path);
                    var names = properties.Keys.OrderBy(n => n == "0" ? 0 : 1)
                        .ThenBy(n => n, NaturalSortComparer.Instance).ToArray();
                    var categorized = LayerCategorizer.Classify(names, LayerDictionaryDefinition.Load());
                    var filters = categorized.VisibleCategories.Select(category => new TargetFilterSnapshot(
                        category,
                        categorized.SortGroupByTag.GetValueOrDefault(category, "Specific"),
                        categorized.LayerTags.Where(pair => pair.Value.Contains(category)).Select(pair => pair.Key).ToArray())).ToArray();
                    var config = PluginConfig.Load();
                    config.TemplateDwgPath = path;
                    config.Save();
                    var updated = current with
                    {
                        TemplateName = Path.GetFileName(path),
                        TemplatePath = path,
                        StandardLayers = names,
                        StandardLayerProperties = properties,
                        MemoryMappings = new MemoryStore(current.MemoryFilePath).Load().Mappings,
                        TargetFilters = filters,
                        AlwaysHiddenTargets = categorized.AlwaysHidden.ToArray()
                    };
                    lock (SnapshotLock) _drawingSnapshot = updated;
                    current.Document.Editor.WriteMessage($"\nStandard loaded: {Path.GetFileName(path)} ({names.Length} layers).");
                    completion.TrySetResult(updated);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                return Task.CompletedTask;
            }, null);
            var updated = await completion.Task.ConfigureAwait(false);
            return SerializeSnapshot("TemplateLoaded", updated);
        }
        catch (Exception ex) { return Error($"Could not load the standard file: {ex.GetBaseException().Message}"); }
    }

    private static string SerializeSnapshot(string type, DrawingSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        type,
        payload = new
        {
            drawing_id = snapshot.DrawingId,
            drawing_name = snapshot.DrawingName,
            heuristic_threshold = snapshot.HeuristicThreshold,
            template_name = snapshot.TemplateName,
            template_path = snapshot.TemplatePath,
            source_layers = snapshot.SourceLayers,
            standard_layers = snapshot.StandardLayers,
            empty_layers = snapshot.EmptyLayers,
            memory_mappings = snapshot.MemoryMappings,
            always_hidden_targets = snapshot.AlwaysHiddenTargets,
            target_filters = snapshot.TargetFilters.Select(filter => new
            {
                name = filter.Name,
                sort_group = filter.SortGroup,
                layers = filter.Layers
            }).ToArray()
        }
    });

    private static async Task<string> ApplyPlanAsync(JsonElement root)
    {
        var payload = root.GetProperty("payload");
        var version = payload.GetProperty("protocol_version").GetInt32();
        if (!IpcProtocol.IsSupportedVersion(version))
            return Error($"Unsupported apply-plan protocol version {version}.");

        var snapshot = GetDrawingSnapshot();
        if (snapshot is null)
            return Error("No drawing snapshot is available. Run LSTDR from an active drawing.");

        var drawingName = payload.GetProperty("drawing_name").GetString() ?? string.Empty;
        var requestDrawingId = payload.TryGetProperty("drawing_id", out var idElement) ? idElement.GetString() : null;
        var targetError = IpcProtocol.CheckDrawingTarget(snapshot.DrawingId, snapshot.DrawingName, requestDrawingId, drawingName);
        if (targetError is not null) return Error(targetError);

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.GetProperty("mappings").EnumerateArray())
        {
            var source = item.GetProperty("source_layer").GetString() ?? string.Empty;
            var target = item.GetProperty("target_layer").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                return Error("A mapping contains an empty layer name.");
            if (!snapshot.SourceLayers.Contains(source, StringComparer.OrdinalIgnoreCase))
                return Error($"The source layer '{source}' was not in the drawing snapshot.");
            if (!snapshot.StandardLayerProperties.ContainsKey(target))
                return Error($"The target layer '{target}' was not in the selected standard.");
            if (!mappings.TryAdd(source, target))
                return Error($"The source layer '{source}' appears more than once in the plan.");
        }

        if (mappings.Count == 0)
            return Error("There are no mappings to apply.");

        var remember = payload.GetProperty("remember").GetBoolean();
        var settings = payload.GetProperty("properties");
        var propertySettings = new PropertyMatchSettings(
            settings.GetProperty("match_color").GetBoolean(),
            settings.GetProperty("match_linetype").GetBoolean(),
            settings.GetProperty("match_lineweight").GetBoolean(),
            settings.GetProperty("make_by_layer").GetBoolean());

        var completion = new TaskCompletionSource<StandardizeCommand.ApplyMappingsResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? memoryWarning = null;
        var remembered = false;
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    var activeDocument = Application.DocumentManager.MdiActiveDocument;
                    if (!ReferenceEquals(activeDocument, snapshot.Document))
                    {
                        completion.TrySetException(new InvalidOperationException(
                            "The source drawing is no longer active. Run LSTDR again in that drawing."));
                    }
                    else
                    {
                        var result = StandardizeCommand.ApplyMappings(
                            snapshot.Document.Database, mappings,
                            snapshot.StandardLayerProperties, propertySettings);

                        if (remember)
                        {
                            try
                            {
                                SaveRememberedMappings(snapshot, mappings);
                                remembered = true;
                            }
                            catch (Exception ex)
                            {
                                memoryWarning = ex.GetBaseException().Message;
                            }
                        }

                        snapshot.Document.Editor.WriteMessage(
                            $"\nRust mappings applied: {result.Renamed} renamed/merged, {result.Synced} layer properties synced.");
                        if (memoryWarning is not null)
                            snapshot.Document.Editor.WriteMessage($"\nTranslation memory was not saved: {memoryWarning}");
                        completion.TrySetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }

                return Task.CompletedTask;
            }, null);

            var applied = await completion.Task.ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                type = "Applied",
                payload = new
                {
                    protocol_version = version,
                    count = applied.Renamed,
                    remembered,
                    warning = memoryWarning
                }
            });
        }
        catch (Exception ex)
        {
            return Error($"Could not apply the mapping plan: {ex.GetBaseException().Message}");
        }
    }

    private static string GetMessageType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("type").GetString() ?? "<empty>";
        }
        catch { return "<invalid-json>"; }
    }

    private static void Log(string message)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "AcLayerStandardizer-ipc.log"),
                    $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void SaveRememberedMappings(DrawingSnapshot snapshot, IReadOnlyDictionary<string, string> mappings)
    {
        var store = new MemoryStore(snapshot.MemoryFilePath);
        var memory = store.Load();
        foreach (var source in snapshot.SourceLayers)
        {
            if (mappings.TryGetValue(source, out var target))
                memory.Mappings[source] = target;
            else
                memory.Mappings.Remove(source);
        }
        store.Save(memory);
    }

    private static async Task<string> PurgeEmptyLayersAsync(JsonElement root)
    {
        var payload = root.GetProperty("payload");
        var version = payload.GetProperty("protocol_version").GetInt32();
        if (!IpcProtocol.IsSupportedVersion(version))
            return Error($"Unsupported purge protocol version {version}.");

        var snapshot = GetDrawingSnapshot();
        if (snapshot is null)
            return Error("No drawing snapshot is available. Run LSTDR from an active drawing.");

        var drawingName = payload.GetProperty("drawing_name").GetString() ?? string.Empty;
        var requestDrawingId = payload.TryGetProperty("drawing_id", out var idElement) ? idElement.GetString() : null;
        var targetError = IpcProtocol.CheckDrawingTarget(snapshot.DrawingId, snapshot.DrawingName, requestDrawingId, drawingName);
        if (targetError is not null) return Error(targetError);

        var requestedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.GetProperty("layers").EnumerateArray())
        {
            var name = item.GetString() ?? string.Empty;
            if (!snapshot.EmptyLayers.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Error($"The layer '{name}' was not empty in the drawing snapshot.");
            requestedNames.Add(name);
        }

        if (requestedNames.Count == 0)
            return Error("There are no empty layers selected for purging.");

        var completion = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    var activeDocument = Application.DocumentManager.MdiActiveDocument;
                    if (!ReferenceEquals(activeDocument, snapshot.Document))
                    {
                        completion.TrySetException(new InvalidOperationException(
                            "The source drawing is no longer active. Run LSTDR again in that drawing."));
                    }
                    else
                    {
                        var stillEmpty = MappingsCommand.GetEmptyLayers(snapshot.Document.Database);
                        var purged = new List<string>();
                        using var transaction = snapshot.Document.Database.TransactionManager.StartTransaction();
                        var table = (Autodesk.AutoCAD.DatabaseServices.LayerTable)transaction.GetObject(
                            snapshot.Document.Database.LayerTableId,
                            Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                        foreach (var name in requestedNames)
                        {
                            if (!stillEmpty.Contains(name) || !table.Has(name)) continue;
                            var id = table[name];
                            if (id == snapshot.Document.Database.Clayer) continue;
                            var layer = (Autodesk.AutoCAD.DatabaseServices.LayerTableRecord)transaction.GetObject(
                                id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                            try
                            {
                                layer.Erase(true);
                                purged.Add(name);
                            }
                            catch
                            {
                                // Match the existing WPF purge behavior: skip layers AutoCAD refuses to erase.
                            }
                        }
                        transaction.Commit();
                        snapshot.Document.Editor.WriteMessage($"\nPurged {purged.Count} empty layer(s).");
                        completion.TrySetResult(purged.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }

                return Task.CompletedTask;
            }, null);

            var purged = await completion.Task.ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                type = "Purged",
                payload = new { protocol_version = version, layers = purged }
            });
        }
        catch (Exception ex)
        {
            return Error($"Could not purge empty layers: {ex.GetBaseException().Message}");
        }
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { type = "Error", payload = message });

    private static DrawingSnapshot? GetDrawingSnapshot()
    {
        lock (SnapshotLock) return _drawingSnapshot;
    }
}
