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
    public void Missing_discipline_code_still_matches_high()
    {
        // "WALL" is missing L-WALL's discipline segment but is otherwise a
        // full match on the remaining token -- should score very high.
        var sim = HeuristicMatcher.CalculateSimilarity("WALL", "L-WALL");
        Assert.True(sim >= 0.9);
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
    public void Extra_qualifier_segment_still_matches_high()
    {
        // Same discipline, same base word, just a more specific subtype --
        // should score high even though the raw strings differ a lot.
        var sim = HeuristicMatcher.CalculateSimilarity("A-DOOR", "A-DOOR-FULL");
        Assert.True(sim >= 0.85);
    }

    [Fact]
    public void Different_discipline_sharing_a_word_scores_low()
    {
        // Same trailing word, but S- vs A- are different namespaces --
        // this used to get conflated by the old prefix-stripping logic.
        var sim = HeuristicMatcher.CalculateSimilarity("S-WALL", "A-WALL");
        Assert.True(sim < 0.5);
    }

    [Fact]
    public void Word_composed_entirely_of_discipline_letters_matches_as_a_subset()
    {
        // Old StripCommonPrefixes did a char-class TrimStart('A','S','E','L','V','I','-','_'),
        // which could eat an entire legitimate word like "AISLE" (A,I,S,L,E
        // are all in that set) down to nothing before comparison. It should
        // now be compared as an intact token, matching a name that legitimately
        // shares it...
        var sim = HeuristicMatcher.CalculateSimilarity("AISLE", "AISLE-WIDTH");
        Assert.True(sim >= 0.85, $"expected AISLE vs AISLE-WIDTH to score high, got {sim}");
    }

    [Fact]
    public void Word_composed_entirely_of_discipline_letters_does_not_falsely_match_unrelated_word()
    {
        // ...and NOT get collapsed to an empty/garbage string that happens
        // to equal some other unrelated strip-set word.
        var sim = HeuristicMatcher.CalculateSimilarity("AISLE", "SEAL");
        Assert.True(sim < 0.5, $"expected AISLE vs SEAL to score low, got {sim}");
    }

    [Fact]
    public void Bound_xref_prefix_is_stripped_before_matching()
    {
        // AutoCAD renames layers "XREFFILE$LAYERNAME" when an xref is bound;
        // matching should ignore everything up to the last '$'.
        var sim = HeuristicMatcher.CalculateSimilarity("A-OLD-BOUND-FILE$A-WALL-FULL", "A-WALL-FULL");
        Assert.Equal(1.0, sim);
    }

    [Fact]
    public void Bound_xref_prefix_stripped_on_both_sides()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("FILE1$A-WALL", "FILE2$A-WALL");
        Assert.Equal(1.0, sim);
    }

    [Fact]
    public void No_dollar_sign_is_unaffected()
    {
        var sim = HeuristicMatcher.CalculateSimilarity("A-WALL", "A-WALL");
        Assert.Equal(1.0, sim);
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
