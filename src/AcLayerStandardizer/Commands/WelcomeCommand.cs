using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcLayerStandardizer.UI;

namespace AcLayerStandardizer.Commands;

public static class WelcomeCommand
{
    [CommandMethod("LAYERSTANDARDIZER", CommandFlags.Modal)]
    [CommandMethod("ACLAYERSTD", "LAYERSTANDARDIZER", CommandFlags.Modal)]
    public static void ShowWelcome()
    {
        var dialog = new WelcomeDialog();
        new WindowInteropHelper(dialog) { Owner = Application.MainWindow.Handle };

        if (dialog.ShowDialog() == true && dialog.OpenMappings)
        {
            MappingsCommand.ShowMappingsEditor();
        }
    }
}
