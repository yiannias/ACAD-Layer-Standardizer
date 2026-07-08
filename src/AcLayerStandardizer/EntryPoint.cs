using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(AcLayerStandardizer.EntryPoint))]

namespace AcLayerStandardizer;

public class EntryPoint : IExtensionApplication
{
    public void Initialize()
    {
        System.Diagnostics.Debug.WriteLine("AcLayerStandardizer loaded.");
    }

    public void Terminate()
    {
    }
}
