using System.Text.Json.Serialization;

namespace AcLayerStandardizer.Data;

public class TranslationMemory
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("userIdentity")]
    public string? UserIdentity { get; set; }

    [JsonPropertyName("mappings")]
    public Dictionary<string, string> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
