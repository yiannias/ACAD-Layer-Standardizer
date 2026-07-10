using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Autodesk.Windows;

namespace AcLayerStandardizer.Core;

internal static class RibbonSetup
{
    public static bool Setup(PluginConfig config)
    {
        if (!config.InstallRibbon) return true;

        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon is null) return false;

            var addinsTab = FindAddInsTab(ribbon);
            if (addinsTab is null) return false;

            foreach (var panel in addinsTab.Panels)
            {
                if (panel.Source?.Title == "Layer Standardizer")
                    return true;
            }

            var panelSource = new RibbonPanelSource
            {
                Title = "Layer Standardizer"
            };
            var newPanel = new RibbonPanel
            {
                Source = panelSource
            };
            addinsTab.Panels.Add(newPanel);

            var largeIcon = LoadIcon("ribbon32.png");
            var smallIcon = LoadIcon("ribbon16.png");
            var button = new RibbonButton
            {
                Name = "Layer Standardizer",
                Text = "Layer\nStandardizer",
                ToolTip = "Open Layer Standardizer",
                CommandHandler = new LsrCommandHandler(),
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                ShowText = true,
                ShowImage = largeIcon is not null,
                LargeImage = largeIcon,
                Image = smallIcon,
            };
            panelSource.Items.Add(button);

            return true;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer ribbon setup failed: {ex}");
            return false;
        }
    }

    // Loaded from embedded resources rather than WPF pack URIs -- pack URI
    // resolution depends on WPF application-level state we don't control
    // inside AutoCAD's process.
    private static BitmapImage? LoadIcon(string fileName)
    {
        try
        {
            using var stream = typeof(RibbonSetup).Assembly
                .GetManifestResourceStream($"AcLayerStandardizer.Resources.{fileName}");
            if (stream is null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer ribbon icon load failed: {ex}");
            return null;
        }
    }

    private static RibbonTab? FindAddInsTab(RibbonControl ribbon)
    {
        foreach (var tab in ribbon.Tabs)
        {
            if (tab.Id == "ID_TabAddIns" || tab.Title == "Add-ins")
                return tab;
        }
        return null;
    }
}
