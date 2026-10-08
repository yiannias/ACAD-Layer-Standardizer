using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;

namespace AcLayerStandardizer.Core;

internal class LsrCommandHandler : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    // Must go through AutoCAD's command engine (SendStringToExecute), not
    // call Commands.MappingsCommand.ShowMappingsEditor() directly. AutoCAD acquires
    // the document lock automatically when it dispatches a registered
    // [CommandMethod] (e.g. typing "HERD" at the command line); calling the
    // C# method straight from this ribbon-button click handler runs on the
    // UI thread with no lock at all, so the first database Transaction
    // deep inside (e.g. reading layers) throws
    // eLockViolation and takes the whole AutoCAD process down with it.
    public void Execute(object? parameter)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        doc?.SendStringToExecute("HERD ", true, false, false);
    }
}
