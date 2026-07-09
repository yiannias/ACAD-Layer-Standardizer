using System.IO;
using System.Text.Json;

namespace AcLayerStandardizer.Data;

public class MemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string FilePath { get; }

    public MemoryStore(string filePath)
    {
        FilePath = filePath;
    }

    public TranslationMemory Load()
    {
        if (!File.Exists(FilePath))
        {
            return new TranslationMemory();
        }

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<TranslationMemory>(json) ?? new TranslationMemory();
    }

    public void Save(TranslationMemory memory)
    {
        memory.LastModified = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(memory, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public TranslationMemory Merge(TranslationMemory current, TranslationMemory imported)
    {
        foreach (var kvp in imported.Mappings)
        {
            current.Mappings.TryAdd(kvp.Key, kvp.Value);
        }

        return current;
    }
}
