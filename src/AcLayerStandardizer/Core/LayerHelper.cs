namespace AcLayerStandardizer.Core;

public static class LayerHelper
{
    public static bool IsSystemLayer(string name)
    {
        return name is "Defpoints" or "AsBuilt"
            || name.StartsWith('*') || name.StartsWith('_');
    }
}