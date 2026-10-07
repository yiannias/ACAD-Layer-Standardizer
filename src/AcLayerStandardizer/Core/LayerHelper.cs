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

    // AutoCAD never lets layer 0 be renamed, erased, or merged into another layer, so a
    // plan that does so can only fail half-way with "eInvalidInput". Returns a message
    // for the user when the plan contains such a mapping, otherwise null.
    public static string? FindUnapplicableMapping(IReadOnlyDictionary<string, string> mappings)
    {
        foreach (var (source, target) in mappings)
        {
            if (source == "0" && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                return $"Layer 0 can't be re-assigned: AutoCAD does not allow layer 0 to be renamed or merged into another layer (here, '{target}'). Remove that connection and apply again. No changes were made.";
        }
        return null;
    }

    public static bool ShouldSkip(string name)
    {
        return IsSystemLayer(name) || IsXrefLayer(name);
    }
}