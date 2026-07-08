using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Nodify;

namespace AcLayerStandardizer.UI;

public enum MappingEditorAction
{
    Cancel,
    Apply,
    ApplyAndSave,
}

public partial class NodeGraphWindow : Window
{
    private readonly LayerEditorViewModel _viewModel;
    private bool _hasChanges;
    private bool _initializing = true;

    public IReadOnlyDictionary<string, string> ResultMappings => _viewModel.CurrentMappings;
    public MappingEditorAction ResultAction { get; private set; }

    public NodeGraphWindow(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string>? memoryMappings,
        List<Matching.MatchResult>? heuristicResults = null)
    {
        InitializeComponent();

        _viewModel = new LayerEditorViewModel(sourceLayers, standardLayers, memoryMappings, heuristicResults);
        DataContext = _viewModel;

        Closing += OnWindowClosing;

        _viewModel.Connections.CollectionChanged += (_, _) =>
        {
            if (!_initializing) _hasChanges = true;
            UpdateStatus();
        };

        _initializing = false;

        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void UpdateStatus()
    {
        StatusText.Text = _viewModel.Connections.Count == 0
            ? "Ready"
            : $"{_viewModel.Connections.Count} mapping(s) active";
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Connections.Count == 0)
        {
            StatusText.Text = "No mappings to apply.";
            return;
        }
        ResultAction = MappingEditorAction.Apply;
        DialogResult = true;
        Close();
    }

    private void OnApplyAndSave(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Connections.Count == 0)
        {
            StatusText.Text = "No mappings to apply.";
            return;
        }
        ResultAction = MappingEditorAction.ApplyAndSave;
        _hasChanges = false;
        DialogResult = true;
        Close();
    }

    private void OnConnectionPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = (sender as DependencyObject) switch
        {
            Connection c => c.DataContext,
            _ => null
        } as LayerConnectionViewModel;

        if (vm is not null)
        {
            _viewModel.RemoveConnectionCommand.Execute(vm);
            e.Handled = true;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_hasChanges && DialogResult is null)
        {
            var result = MessageBox.Show(
                "Discard unsaved changes?", "Layer Mapping Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        if (DialogResult is null)
            DialogResult = false;
    }
}