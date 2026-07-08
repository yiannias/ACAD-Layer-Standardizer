using System.Text.Json;

namespace AcLayerStandardizer.Data;

public sealed class RollbackSnapshot
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Core.PluginConfig.ConfigDirectory, "rollback_snapshot.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public DateTime Timestamp { get; set; }
    public List<RenamedLayerBackup> RenamedLayers { get; set; } = [];
    public List<ErasedLayerBackup> ErasedLayers { get; set; } = [];

    public static RollbackSnapshot? Load()
    {
        if (!System.IO.File.Exists(FilePath))
            return null;
        var json = System.IO.File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<RollbackSnapshot>(json);
    }

    public void Save()
    {
        Timestamp = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(this, JsonOptions);
        System.IO.File.WriteAllText(FilePath, json);
    }

    public static void Delete()
    {
        if (System.IO.File.Exists(FilePath))
            System.IO.File.Delete(FilePath);
    }
}

public sealed class RenamedLayerBackup
{
    public string OriginalName { get; set; } = "";
    public string NewName { get; set; } = "";
    public int ColorIndex { get; set; }
    public string Linetype { get; set; } = "";
    public string LineWeight { get; set; } = "";
    public bool IsPlottable { get; set; }
    public string? Description { get; set; }
}

public sealed class ErasedLayerBackup
{
    public string OriginalName { get; set; } = "";
    public int ColorIndex { get; set; }
    public string Linetype { get; set; } = "";
    public string LineWeight { get; set; } = "";
    public bool IsPlottable { get; set; }
    public string? Description { get; set; }
    public List<long> TransferredEntityHandles { get; set; } = [];
}
