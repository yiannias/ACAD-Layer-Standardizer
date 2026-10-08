using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AcLayerStandardizer.Tests;

public class InstallerScriptTests
{
    [Fact]
    public void Installer_installs_the_rust_window_for_every_autocad_generation()
    {
        var script = Path.Combine(FindRepoRoot(), "installer", "LayerHerder.iss");
        var uiSourceLines = File.ReadAllLines(script)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
            .Where(line => SourceOf(line).EndsWith(@"\acad_layer_ui.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var era in new[] { "R24", "R25", "R26" })
        {
            var count = uiSourceLines.Count(line =>
                line.IndexOf(@"DestDir: ""{app}\Contents\" + era + @"""", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.True(count == 1, $"Expected one acad_layer_ui.exe line for {era}, found {count}.");
        }
    }

    // The quoted path after "Source:".
    private static string SourceOf(string line)
    {
        var start = line.IndexOf('"');
        var end = start < 0 ? -1 : line.IndexOf('"', start + 1);
        return end < 0 ? string.Empty : line.Substring(start + 1, end - start - 1);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "installer", "LayerHerder.iss")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find installer\\LayerHerder.iss above " + AppContext.BaseDirectory);
    }
}
