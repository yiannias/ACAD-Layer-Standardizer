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
        var memory = JsonSerializer.Deserialize<TranslationMemory>(json)
            ?? throw new InvalidDataException("The translation memory file contains no data.");
        if (memory.Mappings is null)
            throw new InvalidDataException("The translation memory file has no valid mappings collection.");

        // JSON deserialization does not retain the dictionary's configured comparer.
        memory.Mappings = new Dictionary<string, string>(memory.Mappings, StringComparer.OrdinalIgnoreCase);
        return memory;
    }

    public void Save(TranslationMemory memory)
    {
        if (memory.Mappings is null)
            throw new InvalidDataException("Cannot save translation memory without a mappings collection.");

        memory.LastModified = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(memory, JsonOptions);
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);

            // Validate the complete staged file before touching the last known-good copy.
            var staged = JsonSerializer.Deserialize<TranslationMemory>(File.ReadAllText(tempPath));
            if (staged?.Mappings is null || staged.Mappings.Count != memory.Mappings.Count)
                throw new InvalidDataException("The staged translation memory did not pass validation.");

            if (File.Exists(FilePath))
            {
                var backupPath = FilePath + ".bak-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                File.Replace(tempPath, FilePath, backupPath);
            }
            else
            {
                File.Move(tempPath, FilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
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
