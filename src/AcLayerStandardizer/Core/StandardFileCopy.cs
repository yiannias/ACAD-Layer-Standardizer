using System.IO;

namespace AcLayerStandardizer.Core;

// Copies a standard file to a private temp path so it can be read even while another
// session holds the original open. Deliberately free of AutoCAD types.
public static class StandardFileCopy
{
    public static string CopyToTemp(string sourcePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "LayerHerder");
        Directory.CreateDirectory(dir);
        var copyPath = Path.Combine(dir, $"{System.Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");

        // Read access with ReadWrite|Delete share: the other session's write handle doesn't block us.
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
        return copyPath;
    }
}
