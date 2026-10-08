using Autodesk.AutoCAD.ApplicationServices;

namespace AcLayerStandardizer.Core;

internal static class MenuSetup
{
    private const string MenuTitle = "Layer Herder";
    // Menu the ACAD Layer Standardizer releases added; taken off the menu
    // bar so upgraded installs don't show both.
    private const string LegacyMenuTitle = "Layer Standardizer";
    private const string MenuItemLabel = "Layer Herder...";
    // COM menu macros take raw characters, not CUI caret notation: "^C^C"
    // set through AcadPopupMenuItem.Macro is sent literally (caret, C, ...)
    // and garbles the command. The classic COM/VBA convention is ASCII 3
    // (the cancel character) twice, then the command with a trailing space
    // acting as Enter.
    private const string MenuItemMacro = "\x03\x03HERD ";

    // COM access is late-bound (dynamic) rather than via the versioned
    // Autodesk.AutoCAD.Interop assemblies, so one body serves every
    // AutoCAD release we target without per-version interop references.
    public static bool Setup(PluginConfig config)
    {
        RemoveLegacyMenu();
        if (!config.InstallMenu) return true;

        try
        {
            dynamic acadApp = Application.AcadApplication;
            dynamic baseGroup = acadApp.MenuGroups.Item(0);

            dynamic? popupMenu = null;
            foreach (dynamic existing in baseGroup.Menus)
            {
                if (existing.Name == MenuTitle)
                {
                    popupMenu = existing;
                    break;
                }
            }

            popupMenu ??= baseGroup.Menus.Add(MenuTitle);

            if (popupMenu.Count == 0)
            {
                popupMenu.AddMenuItem(0, MenuItemLabel, MenuItemMacro);
            }
            else
            {
                // Self-heal: keep the item's macro current even if the menu
                // group already existed from an earlier run (e.g. an older
                // build that shipped a different macro string).
                dynamic item = popupMenu.Item(0);
                if (item.Macro != MenuItemMacro)
                    item.Macro = MenuItemMacro;
            }

            if (!popupMenu.OnMenuBar)
                popupMenu.InsertInMenuBar(acadApp.MenuBar.Count);

            return true;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer menu setup failed: {ex}");
            return false;
        }
    }

    // Best effort: a failure here must not stop the Layer Herder menu.
    private static void RemoveLegacyMenu()
    {
        try
        {
            dynamic acadApp = Application.AcadApplication;
            dynamic baseGroup = acadApp.MenuGroups.Item(0);
            foreach (dynamic existing in baseGroup.Menus)
            {
                if (existing.Name == LegacyMenuTitle && existing.OnMenuBar)
                {
                    existing.RemoveFromMenuBar();
                    break;
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer legacy menu removal failed: {ex}");
        }
    }
}
