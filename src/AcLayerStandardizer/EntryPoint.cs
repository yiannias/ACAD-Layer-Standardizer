using System.IO;
using Autodesk.AutoCAD.Runtime;
using AcLayerStandardizer.Core;

[assembly: ExtensionApplication(typeof(AcLayerStandardizer.EntryPoint))]

namespace AcLayerStandardizer;

public class EntryPoint : IExtensionApplication
{
    public void Initialize()
    {
        var config = PluginConfig.Load();

        if (string.IsNullOrEmpty(config.MemoryFilePath))
        {
            config.MemoryFilePath = Path.Combine(
                PluginConfig.ConfigDirectory, "standards_memory.json");
            config.Save();
        }

        System.Diagnostics.Debug.WriteLine("AcLayerStandardizer loaded.");
    }

    public void Terminate()
    {
    }
}
