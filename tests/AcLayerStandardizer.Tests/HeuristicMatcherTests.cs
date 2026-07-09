using Xunit;
using AcLayerStandardizer.Matching;

namespace AcLayerStandardizer.Tests;

public class HeuristicMatcherTests
{
    [Fact]
    public void Exact_match_returns_1_0()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("L-WALL", "L-WALL");
        Assert.Equal(1.0, sim);
    }

    [Fact]
    public void Identical_after_case_normalization_returns_1_0()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("l-wall", "L-WALL");
        Assert.Equal(1.0, sim);
    }

    [Fact]
    public void Contains_match_returns_0_85()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("WALL", "L-WALL");
        Assert.Equal(0.85, sim);
    }

    [Fact]
    public void Levenshtein_similar_layer_names()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("L-WLL", "L-WALL");
        Assert.InRange(sim, 0.6, 0.9);
    }

    [Fact]
    public void Completely_different_returns_low()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("ABC", "XYZ");
        Assert.True(sim < 0.3);
    }

    [Fact]
    public void Empty_string_returns_zero()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("", "L-WALL");
        Assert.Equal(0.0, sim);
    }

    [Fact]
    public void HeuristicMatcher_finds_best_match()
    {
        var standards = new[] { "L-WALL", "A-ANNO-TEXT", "E-LITE", "V-PROP-LINE" };
        var matcher = new HeuristicMatcher(standards, 0.6);

        var result = matcher.TryMatch("L-WAL");

        Assert.NotNull(result);
        Assert.Equal("L-WALL", result.TargetLayer);
        Assert.Equal(MatchSource.Heuristic, result.Source);
    }

    [Fact]
    public void HeuristicMatcher_returns_null_for_no_match()
    {
        var standards = new[] { "L-WALL", "A-ANNO-TEXT" };
        var matcher = new HeuristicMatcher(standards, 0.9);

        var result = matcher.TryMatch("ZZZZZZ");

        Assert.Null(result);
    }

    [Fact]
    public void MemoryMatcher_finds_exact_match()
    {
        var memory = new Data.TranslationMemory();
        memory.Mappings["L-WAL-OLD"] = "L-WALL";

        var matcher = new MemoryMatcher(memory);
        var result = matcher.TryMatch("L-WAL-OLD");

        Assert.NotNull(result);
        Assert.Equal("L-WALL", result.TargetLayer);
        Assert.Equal(MatchSource.Memory, result.Source);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void MemoryMatcher_returns_null_for_unknown()
    {
        var memory = new Data.TranslationMemory();
        var matcher = new MemoryMatcher(memory);

        var result = matcher.TryMatch("UNKNOWN");
        Assert.Null(result);
    }

    [Fact]
    public void MatchingEngine_pipeline_orders_correctly()
    {
        var memory = new Data.TranslationMemory();
        memory.Mappings["EXACT-MATCH"] = "L-WALL";

        var standards = new[] { "L-WALL", "A-ANNO-TEXT" };
        var memoryMatcher = new MemoryMatcher(memory);
        var heuristicMatcher = new HeuristicMatcher(standards, 0.6);
        var engine = new MatchingEngine(memoryMatcher, heuristicMatcher);

        var memResult = engine.Classify("EXACT-MATCH");
        Assert.Equal(MatchSource.Memory, memResult.Source);

        var heuristicResult = engine.Classify("L-WAL");
        Assert.Equal(MatchSource.Heuristic, heuristicResult.Source);

        var unmatchedResult = engine.Classify("ZZZZZZZZ");
        Assert.Equal(MatchSource.Unmatched, unmatchedResult.Source);
    }

    [Fact]
    public void MemoryStore_roundtrip_preserves_data()
    {
        var path = Path.GetTempFileName();
        try
        {
            var store = new Data.MemoryStore(path);
            var mem = new Data.TranslationMemory
            {
                UserIdentity = "test-user",
                SchemaVersion = "1.0",
            };
            mem.Mappings["A"] = "B";
            mem.Mappings["C"] = "D";

            store.Save(mem);

            var loaded = store.Load();
            Assert.Equal("test-user", loaded.UserIdentity);
            Assert.Equal("B", loaded.Mappings["A"]);
            Assert.Equal("D", loaded.Mappings["C"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MemoryStore_merge_imports_new_mappings()
    {
        var current = new Data.TranslationMemory();
        current.Mappings["X"] = "Y";

        var imported = new Data.TranslationMemory();
        imported.Mappings["A"] = "B";
        imported.Mappings["X"] = "OVERRIDE";

        var store = new Data.MemoryStore("unused.json");
        var merged = store.Merge(current, imported);

        Assert.Equal("Y", merged.Mappings["X"]);
        Assert.Equal("B", merged.Mappings["A"]);
    }
}
