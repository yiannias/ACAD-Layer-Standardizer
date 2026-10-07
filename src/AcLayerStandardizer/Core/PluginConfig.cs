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
    public bool InstallRibbon { get; set; } = true;
    public bool InstallMenu { get; set; } = true;

    public static string ConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AcLayerStandardizer");

    public static string ConfigPath =>
        Path.Combine(ConfigDirectory, "config.json");

    // A blank setting means the default file in the config folder; a relative one is
    // relative to the config folder too (the Rust window applies the same rule).
    public string GetEffectiveMemoryPath()
    {
        if (string.IsNullOrWhiteSpace(MemoryFilePath))
            return Path.Combine(ConfigDirectory, "standards_memory.json");
        return Path.IsPathRooted(MemoryFilePath)
            ? MemoryFilePath
            : Path.Combine(ConfigDirectory, MemoryFilePath);
    }

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
