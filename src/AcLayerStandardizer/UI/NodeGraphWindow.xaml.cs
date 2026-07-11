using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcLayerStandardizer.Core;
using AcLayerStandardizer.Data;
using AcLayerStandardizer.Matching;
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

    // Updated by SwitchTemplate; the caller (MappingsCommand) reads this
    // after ShowDialog returns instead of its own originally-captured copy,
    // since the user may have switched templates mid-session.
    public IReadOnlyDictionary<string, LayerProperties> StandardLayerProperties { get; private set; }

    private readonly UserPreferences _preferences;
    private string _currentTemplatePath;
    private readonly Action<string>? _onTemplateChanged;

    public NodeGraphWindow(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string>? memoryMappings,
        List<Matching.MatchResult>? heuristicResults = null,
        HashSet<string>? emptyLayers = null,
        Action<IReadOnlySet<string>>? purgeCallback = null,
        string sourceFileName = "",
        string templatePath = "",
        IReadOnlyDictionary<string, LayerProperties>? standardLayerProperties = null,
        Action<string>? onTemplateChanged = null)
    {
        InitializeComponent();
        VersionText.Text = GetAppVersionDisplay();

        _preferences = UserPreferences.Load();
        Width = _preferences.MappingEditorWidth;
        Height = _preferences.MappingEditorHeight;
        WindowState = _preferences.MappingEditorMaximized ? WindowState.Maximized : WindowState.Normal;

        StandardLayerProperties = standardLayerProperties ?? new Dictionary<string, LayerProperties>();
        _currentTemplatePath = templatePath;
        _onTemplateChanged = onTemplateChanged;

        _viewModel = new LayerEditorViewModel(
            sourceLayers, standardLayers, memoryMappings, heuristicResults, emptyLayers,
            sourceFileName, Path.GetFileName(templatePath));

        if (purgeCallback is not null && emptyLayers is { Count: > 0 })
        {
            _viewModel.PurgeUnusedCommand = new DelegateCommand<object>(_ =>
            {
                var ordered = emptyLayers.OrderBy(n => n).ToList();
                var dialog = new PurgeConfirmDialog(emptyLayers.Count, ordered);
                if (dialog.ShowDialog() != true)
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

        // Viewport zoom/pan can only be applied once the editor has a real
        // size to compute against, so restore it after layout rather than
        // in the constructor. Guarded: a bad/corrupt saved value (e.g. 0 or
        // NaN zoom) throwing here would otherwise crash the whole AutoCAD
        // process, since exceptions escaping a WPF event handler aren't
        // caught by AutoCAD's host.
        Loaded += (_, _) =>
        {
            try
            {
                Editor.ViewportZoom = _preferences.MappingEditorZoom;
                Editor.ViewportLocation = new Point(
                    _preferences.MappingEditorViewportX,
                    _preferences.MappingEditorViewportY);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer: failed to restore viewport state: {ex}");
            }
        };
    }

    public PropertyMatchSettings PropertySettings => new(
        _viewModel.IsMatchColorEnabled,
        _viewModel.IsMatchLinetypeEnabled,
        _viewModel.IsMatchLineweightEnabled
    );

    // Reads the same $AppVersion string build.ps1 stamps into the assembly
    // via -p:InformationalVersion, so this label can't drift out of sync
    // with the installer filename/PluginConfig version the way a hardcoded
    // XAML string did (it was still reading "2026-07-09.v2 (ALPHA)" two
    // version bumps after the fact).
    private static string GetAppVersionDisplay()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return string.IsNullOrEmpty(info) ? "" : info;
    }

    // Shared by both the Source and Target canvas header labels (same
    // DataTemplate) -- only Target actually does anything.
    private void OnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LayerNodeViewModel { IsHeader: true, Name: "Target" })
            return;

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Standard Template",
            Filter = "AutoCAD Drawing (*.dwg;*.dxf)|*.dwg;*.dxf|All Files (*.*)|*.*",
        };
        var initialDir = string.IsNullOrEmpty(_currentTemplatePath)
            ? null
            : Path.GetDirectoryName(_currentTemplatePath);
        if (!string.IsNullOrEmpty(initialDir))
            ofd.InitialDirectory = initialDir;

        if (ofd.ShowDialog(this) != true) return;

        bool preserveByName = false;
        if (_viewModel.Connections.Count > 0)
        {
            var choice = new TemplateSwitchDialog();
            new WindowInteropHelper(choice) { Owner = new WindowInteropHelper(this).Handle };
            if (choice.ShowDialog() != true) return; // Cancel
            preserveByName = choice.PreserveByName;
        }

        LoadTemplate(ofd.FileName, preserveByName);
    }

    private void LoadTemplate(string path, bool preserveByName)
    {
        IReadOnlyDictionary<string, LayerProperties> standardLayerProps;
        try
        {
            standardLayerProps = SideDatabase.LoadStandardLayers(path);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, $"Error reading template DWG: {ex.Message}",
                "Layer Mapping Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var sortedStandard = standardLayerProps.Keys
            .OrderBy(n => n == "0" ? 0 : 1)
            .ThenBy(n => n)
            .ToList();

        Dictionary<string, string>? preserveMappings = preserveByName
            ? new Dictionary<string, string>(_viewModel.CurrentMappings, StringComparer.OrdinalIgnoreCase)
            : null;

        var config = PluginConfig.Load();
        var memPath = string.IsNullOrEmpty(config.MemoryFilePath)
            ? Path.Combine(PluginConfig.ConfigDirectory, "standards_memory.json")
            : config.MemoryFilePath;

        Dictionary<string, string> memoryMappings;
        try
        {
            memoryMappings = new MemoryStore(memPath).Load().Mappings;
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, $"Error reading translation memory: {ex.Message}",
                "Layer Mapping Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            memoryMappings = new Dictionary<string, string>();
        }

        var heuristicMatcher = new HeuristicMatcher(sortedStandard, config.HeuristicThreshold);
        var heuristicResults = new List<Matching.MatchResult>();
        foreach (var srcNode in _viewModel.Nodes.Where(n => n.IsSource))
        {
            if (memoryMappings.ContainsKey(srcNode.Name)) continue;
            var result = heuristicMatcher.TryMatch(srcNode.Name);
            if (result is not null)
                heuristicResults.Add(result);
        }

        _viewModel.SwitchTemplate(sortedStandard, memoryMappings, heuristicResults, preserveMappings);

        StandardLayerProperties = standardLayerProps;
        _currentTemplatePath = path;
        _viewModel.TargetFileName = Path.GetFileName(path);
        _hasChanges = true;

        _onTemplateChanged?.Invoke(path);
    }

    private static readonly DependencyProperty PrevLocationProperty =
        DependencyProperty.RegisterAttached("PrevLocation", typeof(Point), typeof(NodeGraphWindow));

    // Node-reposition animation DISABLED (chris, 2026-07-10): while
    // debugging the "toggles do nothing / lines point at empty canvas"
    // report, the tween loop below is a prime interference suspect -- it
    // rewrites node.Location every frame from captured starting positions,
    // racing any concurrent ApplyFilters() layout pass and leaving
    // nodes/anchors at positions the view model never computed. Nodes now
    // jump instantly to their computed spot. The machinery is kept (unused)
    // so the animation can be re-enabled deliberately once the visibility
    // bug is found, rather than rewritten from memory.
    private void OnItemLocationChanged(object sender, RoutedEventArgs e)
    {
        // Intentionally no-op beyond bookkeeping; see comment above.
        if (e.OriginalSource is ItemContainer container && container.IsLoaded)
            container.SetValue(PrevLocationProperty, container.Location);
    }

    // Keyed by node so a second reposition request mid-animation retargets
    // the existing tween instead of stacking a duplicate one.
    private readonly Dictionary<LayerNodeViewModel, (DateTime Start, Point From, Point To, double Duration)> _activeLocationAnimations = new();
    private bool _suppressLocationAnimationEvents;

    // Animates the ViewModel's actual Location (not a visual RenderTransform
    // on the container) so the connected line's anchor point -- bound to
    // Location via LayerNodeViewModel.UpdateAnchor -- moves in lockstep with
    // the node every frame, instead of snapping to the destination instantly
    // while the node visually slides in behind it (the old RenderTransform
    // trick never touched Location, so the line and the node visibly
    // detached from each other mid-animation -- the likely source of the
    // "clunky" feeling flagged in chris's review).
    private void StartLocationAnimation(LayerNodeViewModel node, Point from, Point to, double durationSeconds)
    {
        bool alreadyAnimating = _activeLocationAnimations.ContainsKey(node);
        _activeLocationAnimations[node] = (DateTime.UtcNow, from, to, durationSeconds);
        if (alreadyAnimating) return; // the running loop below picks up the retargeted destination next tick

        void Tick(object? s, EventArgs e)
        {
            if (!_activeLocationAnimations.TryGetValue(node, out var anim))
            {
                CompositionTarget.Rendering -= Tick;
                return;
            }

            double t = (DateTime.UtcNow - anim.Start).TotalSeconds / anim.Duration;
            Point next;
            bool done = t >= 1.0;
            if (done)
            {
                next = anim.To;
                _activeLocationAnimations.Remove(node);
                CompositionTarget.Rendering -= Tick;
            }
            else
            {
                // Ease-out cubic -- smoother deceleration than the old
                // DecelerationRatio-only transform.
                double eased = 1 - Math.Pow(1 - t, 3);
                next = new Point(
                    anim.From.X + (anim.To.X - anim.From.X) * eased,
                    anim.From.Y + (anim.To.Y - anim.From.Y) * eased);
            }

            _suppressLocationAnimationEvents = true;
            node.Location = next;
            _suppressLocationAnimationEvents = false;
        }

        CompositionTarget.Rendering += Tick;
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
        if (e.Key == Key.Escape)
        {
            foreach (var n in _viewModel.Nodes.Where(n => n.IsSelected))
                n.IsSelected = false;
        }

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

        SavePreferences();
    }

    private void SavePreferences()
    {
        // Whole method guarded, not just the file write: RestoreBounds and
        // the Editor viewport properties are external/WPF-owned reads that
        // could throw in edge cases (e.g. a window closing before it was
        // ever fully shown), and an exception escaping the Closing handler
        // crashes the whole AutoCAD process rather than just this dialog.
        try
        {
            _preferences.MappingEditorMaximized = WindowState == WindowState.Maximized;
            // RestoreBounds holds the pre-maximize size/position when
            // maximized; Width/Height would otherwise report the maximized
            // (full-screen) dimensions, which isn't what we want to restore
            // to next time.
            var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            _preferences.MappingEditorWidth = bounds.Width;
            _preferences.MappingEditorHeight = bounds.Height;
            _preferences.MappingEditorZoom = Editor.ViewportZoom;
            _preferences.MappingEditorViewportX = Editor.ViewportLocation.X;
            _preferences.MappingEditorViewportY = Editor.ViewportLocation.Y;

            _preferences.Save();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AcLayerStandardizer: failed to save UI preferences: {ex}");
        }
    }
}