using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;

namespace AcLayerStandardizer.Core;

internal class LsrCommandHandler : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    // Must go through AutoCAD's command engine (SendStringToExecute), not
    // call Commands.WelcomeCommand.ShowWelcome() directly. AutoCAD acquires
    // the document lock automatically when it dispatches a registered
    // [CommandMethod] (e.g. typing "LSR" at the command line); calling the
    // C# method straight from this ribbon-button click handler runs on the
    // UI thread with no lock at all, so the first database Transaction
    // deep inside (StandardizeCommand.EnsureNotCurrentLayer) throws
    // eLockViolation and takes the whole AutoCAD process down with it.
    public void Execute(object? parameter)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        doc?.SendStringToExecute("LSR ", true, false, false);
    }
}
