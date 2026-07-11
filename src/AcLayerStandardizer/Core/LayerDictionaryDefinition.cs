using System.IO;
using System.Text.Json;

namespace AcLayerStandardizer.Core;

public class LayerCategoryDefinition
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Tokens { get; set; } = [];
    public bool MatchAnywhere { get; set; }
    public bool Exclusive { get; set; }
    public string? FallbackGroup { get; set; }

    // Which tier this category sits in for the Target Filter panel's
    // ordering/color and filtering behavior: "Discipline"/"General" are
    // inclusive (a node matches if it carries the tag at all), "Specific"
    // is exclusive (a node matches only via its single rarest Specific tag,
    // same as the original PrimaryTargetTag mechanism, just scoped). See
    // LayerCategorizer.Classify and LayerEditorViewModel.IsTargetNodeVisible.
    public string SortGroup { get; set; } = "Specific";

    // Exempts this category from the fold threshold entirely -- it shows as
    // its own toggle whenever it has >=1 real match, regardless of count.
    // For categories where identity matters more than volume (e.g. Life
    // Safety), where folding into a generic fallback would defeat the point.
    public bool AlwaysShow { get; set; }
}

public class LayerDictionaryDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string? Description { get; set; }
    public string Delimiter { get; set; } = "-";
    public int FieldsScanned { get; set; } = 3;
    public int FoldThreshold { get; set; } = 5;
    public List<string> ExcludedPrefixes { get; set; } = [];
    public List<string> ExcludedLayers { get; set; } = [];
    public List<LayerCategoryDefinition> Categories { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Machine-specific per-user file, installed alongside config.json and
    // deliberately user-editable (see the file's own "description" field) --
    // an installer upgrade must never overwrite a user's retuned categories.
    public static string DictionaryPath =>
        Path.Combine(PluginConfig.ConfigDirectory, "layer_dictionary.json");

    public static LayerDictionaryDefinition Load()
    {
        if (!File.Exists(DictionaryPath))
            return new LayerDictionaryDefinition();

        try
        {
            var json = File.ReadAllText(DictionaryPath);
            return JsonSerializer.Deserialize<LayerDictionaryDefinition>(json, JsonOptions)
                ?? new LayerDictionaryDefinition();
        }
        catch
        {
            return new LayerDictionaryDefinition();
        }
    }
}
