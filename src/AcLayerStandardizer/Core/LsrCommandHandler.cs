using System.Windows.Input;
using Autodesk.Windows;

namespace AcLayerStandardizer.Core;

internal class LsrCommandHandler : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        Commands.WelcomeCommand.ShowWelcome();
    }
}
