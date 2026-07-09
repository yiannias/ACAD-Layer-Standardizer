using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcLayerStandardizer.Core;
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
        List<Matching.MatchResult>? heuristicResults = null,
        HashSet<string>? emptyLayers = null,
        Action<IReadOnlySet<string>>? purgeCallback = null)
    {
        InitializeComponent();

        _viewModel = new LayerEditorViewModel(
            sourceLayers, standardLayers, memoryMappings, heuristicResults, emptyLayers);

        if (purgeCallback is not null && emptyLayers is { Count: > 0 })
        {
            _viewModel.PurgeUnusedCommand = new DelegateCommand<object>(_ =>
            {
                var msg = $"Purge {emptyLayers.Count} unused layer(s)?\n\n{string.Join("\n", emptyLayers.OrderBy(n => n))}";
                if (MessageBox.Show(msg, "Purge Unused Layers",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                purgeCallback(emptyLayers);
                _viewModel.RemoveEmptyLayers();
            });
        }

        DataContext = _viewModel;

        Closing += OnWindowClosing;

        _viewModel.Connections.CollectionChanged += (_, _) =>
        {
            if (!_initializing) _hasChanges = true;
            UpdateStatus();
        };

        _initializing = false;

        SourceInitialized += (_, _) => EnableDarkTitleBar();
        Editor.AddHandler(ItemContainer.LocationChangedEvent, new RoutedEventHandler(OnItemLocationChanged));
    }

    public PropertyMatchSettings PropertySettings => new(
        _viewModel.IsMatchColorEnabled,
        _viewModel.IsMatchLinetypeEnabled,
        _viewModel.IsMatchLineweightEnabled
    );

    private static readonly DependencyProperty PrevLocationProperty =
        DependencyProperty.RegisterAttached("PrevLocation", typeof(Point), typeof(NodeGraphWindow));

    private void OnItemLocationChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ItemContainer container || !container.IsLoaded)
            return;

        var newLoc = container.Location;
        var prevLoc = (Point)container.GetValue(PrevLocationProperty);
        container.SetValue(PrevLocationProperty, newLoc);

        if (prevLoc == default || prevLoc == newLoc)
            return;

        var offset = new Vector(prevLoc.X - newLoc.X, prevLoc.Y - newLoc.Y);
        var dist = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
        if (dist < 5) return;

        var transform = new TranslateTransform(offset.X, offset.Y);
        container.RenderTransform = transform;
        container.RenderTransformOrigin = new Point(0, 0);

        double duration = Math.Clamp(dist / 60.0, 0.15, 0.4);
        transform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, TimeSpan.FromSeconds(duration)) { DecelerationRatio = 0.3 });
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromSeconds(duration)) { DecelerationRatio = 0.3 });
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
        _hasChanges = false;
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !e.IsRepeat)
        {
            var eg = Editor.InputGestures as Nodify.Interactivity.EditorGestures;
            if (eg is null)
            {
                eg = new Nodify.Interactivity.EditorGestures();
                Editor.InputGestures = eg;
            }
            eg.Editor.Pan.Value = new Nodify.Interactivity.AnyGesture(
                new Nodify.Interactivity.MouseGesture(MouseAction.MiddleClick),
                new Nodify.Interactivity.MouseGesture(MouseAction.RightClick),
                new Nodify.Interactivity.MouseGesture(MouseAction.LeftClick));
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            var eg = Editor.InputGestures as Nodify.Interactivity.EditorGestures;
            if (eg is null)
            {
                eg = new Nodify.Interactivity.EditorGestures();
                Editor.InputGestures = eg;
            }
            eg.Editor.Pan.Value = new Nodify.Interactivity.AnyGesture(
                new Nodify.Interactivity.MouseGesture(MouseAction.MiddleClick),
                new Nodify.Interactivity.MouseGesture(MouseAction.RightClick));
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