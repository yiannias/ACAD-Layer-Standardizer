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

            var button = new RibbonButton
            {
                Name = "Layer Standardizer",
                Text = "Layer\nStandardizer",
                ToolTip = "Open Layer Standardizer",
                CommandHandler = new LsrCommandHandler()
            };
            panelSource.Items.Add(button);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RibbonTab? FindAddInsTab(RibbonControl ribbon)
    {
        foreach (var tab in ribbon.Tabs)
        {
            if (tab.Id == "Add-ins" || tab.Title == "Add-ins")
                return tab;
        }
        return null;
    }
}
