using System.Windows;
using System.Windows.Input;

namespace AcLayerStandardizer.UI;

public partial class PurgeConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public PurgeConfirmDialog(int count, List<string> layerNames)
    {
        InitializeComponent();
        CountRun.Text = count.ToString();
        LayerList.ItemsSource = layerNames;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }
}
