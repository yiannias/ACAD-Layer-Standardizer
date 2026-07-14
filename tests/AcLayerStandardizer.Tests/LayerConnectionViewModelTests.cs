using System.Windows;
using Xunit;
using AcLayerStandardizer.UI;

namespace AcLayerStandardizer.Tests;

public class LayerConnectionViewModelTests
{
    private static LayerNodeViewModel MakeNode(string name, bool isSource) =>
        new(name, isSource, new Point(0, 0));

    [Theory]
    [InlineData(ConnectionMatchSource.ExactName)]
    [InlineData(ConnectionMatchSource.Memory)]
    [InlineData(ConnectionMatchSource.Manual)]
    public void Confidence_defaults_to_1_0_for_non_heuristic_sources(ConnectionMatchSource matchSource)
    {
        var conn = new LayerConnectionViewModel(MakeNode("A", true), MakeNode("B", false), matchSource);
        Assert.Equal(1.0, conn.Confidence);
    }

    [Fact]
    public void Heuristic_stroke_is_vivid_amber_at_full_confidence()
    {
        var conn = new LayerConnectionViewModel(
            MakeNode("A", true), MakeNode("B", false), ConnectionMatchSource.Heuristic, confidence: 1.0);

        Assert.Equal("#C98500", conn.Stroke, ignoreCase: true);
    }

    [Fact]
    public void Heuristic_stroke_is_muted_at_the_confidence_floor()
    {
        var conn = new LayerConnectionViewModel(
            MakeNode("A", true), MakeNode("B", false), ConnectionMatchSource.Heuristic, confidence: 0.6);

        Assert.Equal("#6B5738", conn.Stroke, ignoreCase: true);
    }

    [Fact]
    public void Heuristic_stroke_is_distinct_between_floor_and_ceiling()
    {
        var low = new LayerConnectionViewModel(
            MakeNode("A", true), MakeNode("B", false), ConnectionMatchSource.Heuristic, confidence: 0.65);
        var mid = new LayerConnectionViewModel(
            MakeNode("C", true), MakeNode("D", false), ConnectionMatchSource.Heuristic, confidence: 0.8);
        var high = new LayerConnectionViewModel(
            MakeNode("E", true), MakeNode("F", false), ConnectionMatchSource.Heuristic, confidence: 0.95);

        Assert.NotEqual(low.Stroke, mid.Stroke);
        Assert.NotEqual(mid.Stroke, high.Stroke);
        Assert.NotEqual(low.Stroke, high.Stroke);
    }

    [Fact]
    public void Non_heuristic_stroke_is_unaffected_by_confidence()
    {
        var conn = new LayerConnectionViewModel(
            MakeNode("A", true), MakeNode("B", false), ConnectionMatchSource.ExactName);

        Assert.Equal("#008300", conn.Stroke, ignoreCase: true);
    }
}
