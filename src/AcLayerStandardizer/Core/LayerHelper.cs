namespace AcLayerStandardizer.Core;

public static class LayerHelper
{
    public static bool IsSystemLayer(string name)
    {
        return name is "Defpoints" or "AsBuilt"
            || name.StartsWith('*') || name.StartsWith('_');
    }

    // Xref'd layers ("XREFFILE|LAYERNAME") can't be edited by this tool, so they're excluded.
    public static bool IsXrefLayer(string name)
    {
        return name.Contains('|');
    }

    public static bool ShouldSkip(string name)
    {
        return IsSystemLayer(name) || IsXrefLayer(name);
    }
}