using System.IO;
using AcLayerStandardizer.Core;
using Xunit;

namespace AcLayerStandardizer.Tests;

public class StandardFileCopyTests
{
    [Fact]
    public void Copy_reads_while_another_handle_holds_the_standard_open_for_write()
    {
        var source = Path.Combine(Path.GetTempPath(), $"lh-std-{System.Guid.NewGuid():N}.dws");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(source, bytes);
        string? copy = null;
        try
        {
            // Stands in for AutoCAD in another session: writable, and denying nobody read access.
            using var holder = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            copy = StandardFileCopy.CopyToTemp(source);

            Assert.NotEqual(source, copy);
            Assert.Equal(bytes, File.ReadAllBytes(copy));
        }
        finally
        {
            if (copy != null && File.Exists(copy)) File.Delete(copy);
            File.Delete(source);
        }
    }
}
