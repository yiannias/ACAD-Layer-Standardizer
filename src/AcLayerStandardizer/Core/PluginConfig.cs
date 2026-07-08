using System.IO;
using System.Text.Json;

namespace AcLayerStandardizer.Core;

public class PluginConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string TemplateDwgPath { get; set; } = string.Empty;
    public string MemoryFilePath { get; set; } = string.Empty;
    public double HeuristicThreshold { get; set; } = 0.6;

    public static string ConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AcLayerStandardizer");

    public static string ConfigPath =>
        Path.Combine(ConfigDirectory, "config.json");

    public static PluginConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new PluginConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<PluginConfig>(json) ?? new PluginConfig();
        }
        catch
        {
            return new PluginConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
