using System.IO;
using System.Text.Json;

namespace AcLayerStandardizer.Core;

public class UserPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public double MappingEditorWidth { get; set; } = 1200;
    public double MappingEditorHeight { get; set; } = 650;
    public bool MappingEditorMaximized { get; set; }
    public double MappingEditorZoom { get; set; } = 1.0;
    public double MappingEditorViewportX { get; set; }
    public double MappingEditorViewportY { get; set; }

    // Machine-specific (window size/position depend on the local monitor
    // setup), so this lives under LocalAppData rather than the Roaming
    // config.json used for template/memory paths.
    public static string PreferencesDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AcLayerStandardizer");

    public static string PreferencesPath =>
        Path.Combine(PreferencesDirectory, "ui_preferences.json");

    public static UserPreferences Load()
    {
        if (!File.Exists(PreferencesPath))
        {
            return new UserPreferences();
        }

        try
        {
            var json = File.ReadAllText(PreferencesPath);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(PreferencesDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(PreferencesPath, json);
    }
}
