using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class LayerHelperLayerZeroTests
{
    [Fact]
    public void Layer_zero_mapped_to_another_layer_is_refused_with_a_plain_message()
    {
        var mappings = new Dictionary<string, string>
        {
            ["Layer2"] = "0-COL-AMENITY",
            ["0"] = "0-COL-AMENITY",
        };
        var message = LayerHelper.FindUnapplicableMapping(mappings);
        Assert.NotNull(message);
        Assert.Contains("Layer 0", message);
        Assert.Contains("0-COL-AMENITY", message);
        Assert.Contains("No changes were made", message);
        Assert.DoesNotContain("eInvalidInput", message);
    }

    [Fact]
    public void Layer_zero_mapped_to_itself_is_fine()
    {
        Assert.Null(LayerHelper.FindUnapplicableMapping(new Dictionary<string, string> { ["0"] = "0" }));
    }

    [Fact]
    public void Other_layers_can_be_mapped_onto_layer_zero()
    {
        Assert.Null(LayerHelper.FindUnapplicableMapping(new Dictionary<string, string> { ["Layer1"] = "0" }));
    }
}
