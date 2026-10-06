using System.Text.Json;
using Xunit;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.Matching;

namespace AcLayerStandardizer.Tests;

// Pins categorization, heuristic matching, and translation-memory behaviour to
// golden files under tests/parity. The same goldens are asserted by the Rust
// tests (rust/crates/acad_layer_core/tests/parity_tests.rs), so both languages
// stay identical. Goldens are generated from THIS (shipped) implementation:
//   set PARITY_GENERATE=1 and run the Generate_parity_goldens test.
public class ParityTests
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static string ParityDir()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "parity"));
        Assert.True(Directory.Exists(dir), $"Expected parity directory at {dir}");
        return dir;
    }

    private static string[] RealLayers() =>
        JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(ParityDir(), "real_layers.json")))!;

    private static LayerDictionaryDefinition ShippedDictionary()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "installer", "assets", "layer_dictionary.json"));
        return JsonSerializer.Deserialize<LayerDictionaryDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }


    // Compares parsed JSON, treating numbers within 1e-9 as equal: .NET Framework
    // prints 0.6 as 0.59999999999999998, so a textual comparison would fail there
    // even though the value is identical.
    private static void AssertJsonEqual(string expected, string actual)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        AssertElementsEqual(e.RootElement, a.RootElement, "$");
    }

    private static void AssertElementsEqual(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: {expected.ValueKind} vs {actual.ValueKind}");
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                Assert.Equal(expectedProps.Keys.OrderBy(k => k, StringComparer.Ordinal), actualProps.Keys.OrderBy(k => k, StringComparer.Ordinal));
                foreach (var pair in expectedProps)
                    AssertElementsEqual(pair.Value, actualProps[pair.Key], $"{path}.{pair.Key}");
                break;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                Assert.True(expectedItems.Length == actualItems.Length, $"{path}: length {expectedItems.Length} vs {actualItems.Length}");
                for (var i = 0; i < expectedItems.Length; i++)
                    AssertElementsEqual(expectedItems[i], actualItems[i], $"{path}[{i}]");
                break;
            case JsonValueKind.Number:
                Assert.True(Math.Abs(expected.GetDouble() - actual.GetDouble()) < 1e-9,
                    $"{path}: {expected.GetDouble()} vs {actual.GetDouble()}");
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;
        }
    }

    // ---- builders: deterministic, sorted JSON ---------------------------------

    private static string BuildCategorization()
    {
        var result = LayerCategorizer.Classify(RealLayers(), ShippedDictionary());
        var layerTags = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var pair in result.LayerTags)
            layerTags[pair.Key] = pair.Value.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(new
        {
            alwaysHidden = result.AlwaysHidden.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            visibleCategories = result.VisibleCategories.ToArray(),
            sortGroupByTag = new SortedDictionary<string, string>(result.SortGroupByTag, StringComparer.Ordinal),
            layerTags,
        }, Pretty);
    }

    private static readonly string[] HeuristicSources =
    [
        "A-WALL", "A-WALL-FULL", "WALLS", "a-door", "DOORS", "A_DOOR_SWING", "TEXT", "A-ANNO-TXT",
        "DIM", "A-ANNO-DIMS", "0-COL-APARTMENT", "FLOOR", "CEILING", "HATCH", "X-SCRATCH", "A-FLOR-IDEN",
        "E-LITE", "M-HVAC", "XREF", "Layer1", "A-ELEV-MEDM", "A-DT-3", "A-DT-30", "A-ANNO-DIM-UNITS",
        "A-CLNG-LGHT", "A-CLNG-MEPF", "TITLEBLOCK", "A-TLBLK-TXT", "VIEWPORT", "A-AREA-GROSS-2",
        "0", "Defpoints", "A-ANNO-TAG-ROOM", "A-ANNO-REV-02", "STAIR", "A-STRS", "PLUMBING", "LANDSCAPE",
    ];

    private static readonly (string A, string B)[] SimilarityPairs =
    [
        ("A-WALL", "A-WALL"), ("A-WALL", "a-wall"), ("A-WALL", "A-WALL-FULL"), ("WALLS", "A-WALL"),
        ("A_DOOR_SWING", "A-DOOR-SWNG"), ("TEXT", "A-ANNO-TEXT"), ("DIM", "A-ANNO-DIM"),
        ("A-ANNO-TXT", "A-ANNO-TEXT"), ("A-DT-3", "A-DT-30"), ("X-SCRATCH", "X-SCRATCH"),
        ("A-FLR", "A-FLOR"), ("A-ANNO-DIMS", "A-ANNO-DIM-UNIT"), ("", "A-WALL"), ("A", "A"),
        ("0-COL-APARTMENT", "0-COL-APARTMENT-A"), ("CEILING", "A-CLNG"), ("HATCH", "A-HATCH-251"),
        ("Layer1", "Layer2"), ("A-ELEV-MEDM", "A-DT-3"), ("TITLEBLOCK", "A-TLBLK"),
    ];

    private static string BuildHeuristic()
    {
        var standards = RealLayers();
        var matcher = new HeuristicMatcher(standards, 0.6);
        var matches = HeuristicSources.Select(source =>
        {
            var match = matcher.TryMatch(source);
            return new { source, target = match?.TargetLayer, confidence = match?.Confidence, kind = match?.Source.ToString() };
        }).ToArray();
        var similarities = SimilarityPairs
            .Select(p => new { a = p.A, b = p.B, score = HeuristicMatcher.CalculateSimilarity(p.A, p.B) })
            .ToArray();
        return JsonSerializer.Serialize(new { minConfidence = 0.6, matches, similarities }, Pretty);
    }

    private static readonly string[] MemoryLookups = ["a-wall", "A-WALL", "a-ELEV-medm", "$0$a-anno-dim", "x-scratch", "A-DOOR"];

    private static string BuildMemory()
    {
        var memory = new MemoryStore(Path.Combine(ParityDir(), "memory_1_2_x.json")).Load();
        var lookups = MemoryLookups.Select(source =>
            new { source, target = memory.Mappings.TryGetValue(source, out var t) ? t : null }).ToArray();
        return JsonSerializer.Serialize(new { count = memory.Mappings.Count, lookups }, Pretty);
    }

    // ---- generator (opt-in) ----------------------------------------------------

    [Fact]
    public void Generate_parity_goldens()
    {
        if (Environment.GetEnvironmentVariable("PARITY_GENERATE") != "1") return;
        File.WriteAllText(Path.Combine(ParityDir(), "categorization.golden.json"), BuildCategorization() + "\n");
        File.WriteAllText(Path.Combine(ParityDir(), "heuristic.golden.json"), BuildHeuristic() + "\n");
        File.WriteAllText(Path.Combine(ParityDir(), "memory_lookup.golden.json"), BuildMemory() + "\n");
    }

    // ---- assertions ------------------------------------------------------------

    [Fact]
    public void Categorization_matches_golden() =>
        AssertJsonEqual(File.ReadAllText(Path.Combine(ParityDir(), "categorization.golden.json")), BuildCategorization());

    [Fact]
    public void Heuristic_scores_match_golden() =>
        AssertJsonEqual(File.ReadAllText(Path.Combine(ParityDir(), "heuristic.golden.json")), BuildHeuristic());

    [Fact]
    public void Memory_lookup_matches_golden() =>
        AssertJsonEqual(File.ReadAllText(Path.Combine(ParityDir(), "memory_lookup.golden.json")), BuildMemory());

    [Fact]
    public void Memory_file_saved_by_rust_loads_in_csharp()
    {
        var path = Path.Combine(ParityDir(), "memory_saved_by_rust.json");
        var memory = new MemoryStore(path).Load();
        Assert.Equal("A-WALL", memory.Mappings["a-wall"]);
        Assert.Equal(2, memory.Mappings.Count);
        Assert.True(memory.LastModified.Year >= 2026);
    }
}
