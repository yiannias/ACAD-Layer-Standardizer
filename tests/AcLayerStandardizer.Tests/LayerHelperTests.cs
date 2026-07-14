using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class LayerHelperTests
{
    [Theory]
    [InlineData("XREFFILE|A-WALL", true)]
    [InlineData("A-WALL", false)]
    public void IsXrefLayer_detects_pipe_delimiter(string name, bool expected)
    {
        Assert.Equal(expected, LayerHelper.IsXrefLayer(name));
    }

    [Fact]
    public void ShouldSkip_true_for_xref_layers()
    {
        Assert.True(LayerHelper.ShouldSkip("XREFFILE|A-WALL"));
    }

    [Fact]
    public void ShouldSkip_true_for_system_layers()
    {
        Assert.True(LayerHelper.ShouldSkip("Defpoints"));
    }

    [Fact]
    public void ShouldSkip_false_for_ordinary_layer()
    {
        Assert.False(LayerHelper.ShouldSkip("A-WALL"));
    }

    [Fact]
    public void ShouldSkip_false_for_bound_layer()
    {
        // Bound (post-bind, '$'-delimited) layers are editable and should NOT be hidden.
        Assert.False(LayerHelper.ShouldSkip("A-OLD-BOUND-FILE$A-WALL-FULL"));
    }
}
