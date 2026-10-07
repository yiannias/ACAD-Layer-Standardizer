using System.Text.Json;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class PluginConfigTests
{
    private static string Default => Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_memory_path_means_the_default_file(string configured)
    {
        var config = new PluginConfig { MemoryFilePath = configured };
        Assert.Equal(Default, config.GetEffectiveMemoryPath());
    }

    [Fact]
    public void A_relative_memory_path_is_relative_to_the_config_folder()
    {
        var config = new PluginConfig { MemoryFilePath = Path.Combine("shared", "mem.json") };
        Assert.Equal(Path.Combine(PluginConfig.ConfigDirectory, "shared", "mem.json"), config.GetEffectiveMemoryPath());
    }

    [Fact]
    public void An_absolute_memory_path_is_used_as_given()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "mem.json");
        var config = new PluginConfig { MemoryFilePath = absolute };
        Assert.Equal(absolute, config.GetEffectiveMemoryPath());
    }

    [Fact]
    public void The_effective_path_is_not_written_into_config_json()
    {
        var json = JsonSerializer.Serialize(new PluginConfig());
        Assert.DoesNotContain("Effective", json);
    }
}
