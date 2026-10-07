using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AcLayerStandardizer.Tests;

// The autoloader (dist/PackageContents.xml) must list exactly the commands the
// plug-in registers. Reads the source text only: loading the plug-in's command
// classes would pull in AutoCAD types and crash the test host.
public class PackageManifestTests
{
    // [CommandMethod("NAME"...)] or [CommandMethod("GROUP", "NAME"...)].
    private static readonly Regex CommandAttribute =
        new(@"\[\s*CommandMethod\(\s*""([^""]+)""\s*(?:,\s*""([^""]+)"")?");

    [Fact]
    public void The_autoloader_manifest_lists_exactly_the_registered_commands()
    {
        var root = FindRepoRoot();

        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commandsDir = Path.Combine(root, "src", "AcLayerStandardizer", "Commands");
        foreach (var file in Directory.GetFiles(commandsDir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in CommandAttribute.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value;
                registered.Add(name.ToUpperInvariant());
            }
        }
        Assert.NotEmpty(registered);

        var manifest = XDocument.Load(Path.Combine(root, "dist", "PackageContents.xml"));
        var blocks = manifest.Root!.Elements("Components").ToList();
        Assert.Equal(3, blocks.Count);

        foreach (var block in blocks)
        {
            var label = (string?)block.Attribute("Description") ?? "(no description)";
            var listed = new HashSet<string>(
                block.Descendants("Command")
                    .Select(c => ((string?)c.Attribute("Global") ?? string.Empty).ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);

            var missing = registered.Except(listed, StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            var extra = listed.Except(registered, StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"Components block \"{label}\": missing [{string.Join(", ", missing)}], " +
                $"extra [{string.Join(", ", extra)}].");
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dist", "PackageContents.xml")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root (dist\\PackageContents.xml).");
    }
}
