using System.Collections.Generic;
using Xunit;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Matching;

namespace AcLayerStandardizer.Tests;

public class RustBridgeTests
{
    [Fact]
    public void Rust_bridge_is_loaded_and_reports_version()
    {
        Assert.True(RustNativeBridge.IsAvailable, 
            $"RustNativeBridge should find and load acad_layer_ffi.dll. Error: {RustNativeBridge.InitializationError}");
        Assert.NotNull(RustNativeBridge.Version);
        Assert.Equal("0.1.0", RustNativeBridge.Version);
    }

    [Theory]
    [InlineData("L-WALL", "L-WALL", 1.0)]
    [InlineData("l-wall", "L-WALL", 1.0)]
    [InlineData("WALL", "L-WALL", 0.9)]
    [InlineData("A-DOOR", "A-DOOR-FULL", 0.85)]
    [InlineData("", "L-WALL", 0.0)]
    public void Similarity_matches_managed_engine(string a, string b, double minExpected)
    {
        if (!RustNativeBridge.IsAvailable) return;

        var nativeSim = RustNativeBridge.CalculateSimilarity(a, b);
        var managedSim = HeuristicMatcher.CalculateSimilarity(a, b);

        Assert.Equal(managedSim, nativeSim, precision: 4);
        if (minExpected > 0)
        {
            Assert.True(nativeSim >= minExpected);
        }
    }

    [Fact]
    public void Rust_classify_matches_expected()
    {
        if (!RustNativeBridge.IsAvailable) return;

        var sources = new List<string> { "WALL", "DOOR" };
        var standards = new List<string> { "A-WALL-FULL", "A-DOOR-SWNG" };

        var results = RustNativeBridge.TryClassify(sources, standards, 0.5);
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);

        Assert.Equal("WALL", results[0].SourceLayer);
        Assert.Equal("A-WALL-FULL", results[0].TargetLayer);
        Assert.Equal(MatchSource.Heuristic, results[0].Source);

        Assert.Equal("DOOR", results[1].SourceLayer);
        Assert.Equal("A-DOOR-SWNG", results[1].TargetLayer);
        Assert.Equal(MatchSource.Heuristic, results[1].Source);
    }
}
