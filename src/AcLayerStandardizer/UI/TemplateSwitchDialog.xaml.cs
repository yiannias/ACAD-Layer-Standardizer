using System.Windows;
using System.Windows.Input;

namespace AcLayerStandardizer.UI;

public partial class TemplateSwitchDialog : Window
{
    public bool PreserveByName { get; private set; }

    public TemplateSwitchDialog()
    {
        InitializeComponent();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnPreserve(object sender, RoutedEventArgs e)
    {
        PreserveByName = true;
        DialogResult = true;
        Close();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        PreserveByName = false;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
