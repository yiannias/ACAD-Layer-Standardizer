using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;

namespace AcLayerStandardizer.Core;

internal static class MenuSetup
{
    private const string MenuTitle = "Layer Standardizer";
    private const string MenuItemLabel = "Layer Standardizer...";
    // COM menu macros take raw characters, not CUI caret notation: "^C^C"
    // set through AcadPopupMenuItem.Macro is sent literally (caret, C, ...)
    // and garbles the command. The classic COM/VBA convention is ASCII 3
    // (the cancel character) twice, then the command with a trailing space
    // acting as Enter.
    private const string MenuItemMacro = "\x03\x03LSR ";

    public static bool Setup(PluginConfig config)
    {
        if (!config.InstallMenu) return true;

        try
        {
            var acadApp = (AcadApplication)Application.AcadApplication;
            var baseGroup = acadApp.MenuGroups.Item(0);

            AcadPopupMenu? popupMenu = null;
            foreach (AcadPopupMenu existing in baseGroup.Menus)
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
                var item = popupMenu.Item(0);
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
}
