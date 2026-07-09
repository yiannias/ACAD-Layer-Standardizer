using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Nodify;

namespace AcLayerStandardizer.UI;

public class LayerEditorViewModel : ObservableObject
{
    private const double NodeWidth = 280;
    private const double NodeHeight = 30;
    private const double NodeSpacing = 44;
    private const double TopMargin = 40;
    private const double LeftColX = 50;
    private const double RightColX = 550;
    private const double TargetColumnGap = 40;

    public ObservableCollection<LayerNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<LayerConnectionViewModel> Connections { get; } = [];

    public PendingConnectionViewModel PendingConnection { get; }

    public ICommand RemoveConnectionCommand { get; }
    public ICommand DisconnectConnectorCommand { get; }
    public ICommand ToggleFilterCommand { get; }

    private ICommand _purgeUnusedCommand = new DelegateCommand<object>(_ => { });
    public ICommand PurgeUnusedCommand
    {
        get => _purgeUnusedCommand;
        set => SetProperty(ref _purgeUnusedCommand, value);
    }

    private bool _isExactNameVisible = true;
    public bool IsExactNameVisible
    {
        get => _isExactNameVisible;
        set
        {
            if (_isExactNameVisible == value) return;
            _isExactNameVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isMemoryMatchVisible = true;
    public bool IsMemoryMatchVisible
    {
        get => _isMemoryMatchVisible;
        set
        {
            if (_isMemoryMatchVisible == value) return;
            _isMemoryMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isHeuristicMatchVisible = true;
    public bool IsHeuristicMatchVisible
    {
        get => _isHeuristicMatchVisible;
        set
        {
            if (_isHeuristicMatchVisible == value) return;
            _isHeuristicMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isManualMatchVisible = true;
    public bool IsManualMatchVisible
    {
        get => _isManualMatchVisible;
        set
        {
            if (_isManualMatchVisible == value) return;
            _isManualMatchVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isUnmatchedVisible = true;
    public bool IsUnmatchedVisible
    {
        get => _isUnmatchedVisible;
        set
        {
            if (_isUnmatchedVisible == value) return;
            _isUnmatchedVisible = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private bool _isMatchColorEnabled = true;
    public bool IsMatchColorEnabled
    {
        get => _isMatchColorEnabled;
        set
        {
            if (_isMatchColorEnabled == value) return;
            _isMatchColorEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isMatchLinetypeEnabled = true;
    public bool IsMatchLinetypeEnabled
    {
        get => _isMatchLinetypeEnabled;
        set
        {
            if (_isMatchLinetypeEnabled == value) return;
            _isMatchLinetypeEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isMatchLineweightEnabled = true;
    public bool IsMatchLineweightEnabled
    {
        get => _isMatchLineweightEnabled;
        set
        {
            if (_isMatchLineweightEnabled == value) return;
            _isMatchLineweightEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _isEmptyHighlighted;
    public bool IsEmptyHighlighted
    {
        get => _isEmptyHighlighted;
        set
        {
            if (_isEmptyHighlighted == value) return;
            _isEmptyHighlighted = value;
            OnPropertyChanged();
            UpdateNodeColors();
        }
    }

    public IReadOnlyDictionary<string, string> CurrentMappings
    {
        get
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Connections)
                dict[c.Source.Name] = c.Target.Name;
            return dict;
        }
    }

    public LayerEditorViewModel(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string>? memoryMappings = null,
        List<Matching.MatchResult>? heuristicResults = null,
        HashSet<string>? emptyLayers = null)
    {
        var sourceModels = new List<LayerNodeViewModel>();
        var standardModels = new List<LayerNodeViewModel>();

        for (int i = 0; i < sourceLayers.Count; i++)
        {
            var loc = new Point(LeftColX, TopMargin + i * NodeSpacing);
            var vm = new LayerNodeViewModel(sourceLayers[i], true, loc);
            sourceModels.Add(vm);
            Nodes.Add(vm);
        }

        for (int i = 0; i < standardLayers.Count; i++)
        {
            var vm = new LayerNodeViewModel(standardLayers[i], false, new Point(0, 0));
            standardModels.Add(vm);
            Nodes.Add(vm);
        }

        var sourceMap = sourceModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var standardMap = standardModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        // Tier 1: exact name matches — solid green
        foreach (var src in sourceModels)
        {
            if (standardMap.TryGetValue(src.Name, out var tgt))
            {
                Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.ExactName));
            }
        }

        // Tier 2: memory-sourced mappings — blue (skip if already exact-matched)
        if (memoryMappings != null)
        {
            foreach (var kvp in memoryMappings)
            {
                if (sourceMap.TryGetValue(kvp.Key, out var src)
                    && standardMap.TryGetValue(kvp.Value, out var tgt))
                {
                    if (Connections.Any(c => c.Source == src)) continue;
                    Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.Memory));
                }
            }
        }

        // Tier 3: heuristic matches — dashed yellow (skip if already connected)
        if (heuristicResults != null)
        {
            foreach (var result in heuristicResults)
            {
                if (sourceMap.TryGetValue(result.SourceLayer, out var src)
                    && result.TargetLayer is not null
                    && standardMap.TryGetValue(result.TargetLayer, out var tgt))
                {
                    if (Connections.Any(c => c.Source == src)) continue;
                    Connections.Add(new LayerConnectionViewModel(src, tgt, ConnectionMatchSource.Heuristic));
                }
            }
        }

        if (emptyLayers is not null)
        {
            foreach (var node in Nodes)
            {
                if (node.IsSource && emptyLayers.Contains(node.Name))
                    node.IsEmpty = true;
            }
        }

        PendingConnection = new PendingConnectionViewModel(this, sourceMap, standardMap);

        RemoveConnectionCommand = new DelegateCommand<LayerConnectionViewModel>(conn =>
        {
            if (conn is null) return;
            conn.Source.IsMapped = false;
            if (!Connections.Any(c => c != conn && c.Target == conn.Target))
                conn.Target.IsMapped = false;
            Connections.Remove(conn);
            ApplyFilters();
        });

        DisconnectConnectorCommand = new DelegateCommand<LayerNodeViewModel>(node =>
        {
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];
                if (c.Source == node || c.Target == node)
                {
                    c.Source.IsMapped = false;
                    if (!Connections.Any(other => other != c && other.Target == c.Target))
                        c.Target.IsMapped = false;
                    Connections.RemoveAt(i);
                }
            }
            ApplyFilters();
        });

        ToggleFilterCommand = new DelegateCommand<ConnectionMatchSource>(source =>
        {
            switch (source)
            {
                case ConnectionMatchSource.ExactName: IsExactNameVisible = !IsExactNameVisible; break;
                case ConnectionMatchSource.Memory: IsMemoryMatchVisible = !IsMemoryMatchVisible; break;
                case ConnectionMatchSource.Heuristic: IsHeuristicMatchVisible = !IsHeuristicMatchVisible; break;
                case ConnectionMatchSource.Manual: IsManualMatchVisible = !IsManualMatchVisible; break;
                case ConnectionMatchSource.Unmatched: IsUnmatchedVisible = !IsUnmatchedVisible; break;
            }
        });

        Connections.CollectionChanged += (_, _) =>
        {
            UpdateNodeColors();
            ArrangeTargetsInColumns();
        };

        ArrangeTargetsInColumns();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        foreach (var node in Nodes)
        {
            if (!node.IsSource)
            {
                node.IsVisible = true;
                continue;
            }

            var conn = Connections.FirstOrDefault(c => c.Source == node);
            ConnectionMatchSource matchType;

            if (conn is null)
                matchType = ConnectionMatchSource.Unmatched;
            else
                matchType = conn.MatchSource;

            node.IsVisible = matchType switch
            {
                ConnectionMatchSource.ExactName => IsExactNameVisible,
                ConnectionMatchSource.Memory => IsMemoryMatchVisible,
                ConnectionMatchSource.Heuristic => IsHeuristicMatchVisible,
                ConnectionMatchSource.Manual => IsManualMatchVisible,
                ConnectionMatchSource.Unmatched => IsUnmatchedVisible,
                _ => true,
            };
        }

        foreach (var conn in Connections)
        {
            conn.IsVisible = conn.Source.IsVisible;
        }

        RepositionVisibleNodes();
        UpdateNodeColors();
        ArrangeTargetsInColumns();
    }

    private void RepositionVisibleNodes()
    {
        var visibleSources = Nodes.Where(n => n.IsSource && n.IsVisible).ToList();

        for (int i = 0; i < visibleSources.Count; i++)
        {
            visibleSources[i].Location = new Point(LeftColX, TopMargin + i * NodeSpacing);
        }
    }

    private void UpdateNodeColors()
    {
        foreach (var node in Nodes)
        {
            if (node.IsSource)
            {
                var conn = Connections.FirstOrDefault(c => c.Source == node);
                node.BackgroundColor = conn switch
                {
                    { MatchSource: ConnectionMatchSource.ExactName } => "#8bc34a",
                    { MatchSource: ConnectionMatchSource.Memory } => "#448aff",
                    { MatchSource: ConnectionMatchSource.Heuristic } => "#ffd740",
                    { MatchSource: ConnectionMatchSource.Manual } => "#7c4dff",
                    _ => "#424242",
                };
                if (node.IsEmpty && IsEmptyHighlighted)
                    node.BackgroundColor = "#b71c1c";
            }
            else
            {
                bool inUse = Connections.Any(c => c.Target == node);
                node.BackgroundColor = inUse ? "#4682b4" : "#424242";
            }
        }
    }

    private static string ExtractPrefix(string layerName)
    {
        int hyphenCount = 0;
        int cutIndex = -1;
        for (int i = 0; i < layerName.Length; i++)
        {
            if (layerName[i] == '-')
            {
                hyphenCount++;
                if (hyphenCount == 2)
                {
                    cutIndex = i;
                    break;
                }
            }
        }
        return cutIndex >= 0 ? layerName[..cutIndex] : layerName;
    }

    private void ArrangeTargetsInColumns()
    {
        var targetNodes = Nodes.Where(n => !n.IsSource).ToList();
        if (targetNodes.Count == 0) return;

        // Build source Y for each connected target (visible sources only)
        var sourceY = new Dictionary<LayerNodeViewModel, double>();
        foreach (var conn in Connections)
        {
            if (!sourceY.ContainsKey(conn.Target) && conn.Source.IsVisible)
                sourceY[conn.Target] = conn.Source.Location.Y;
        }

        var connected = new List<LayerNodeViewModel>();
        var unconnected = new List<LayerNodeViewModel>();

        foreach (var target in targetNodes)
        {
            if (sourceY.ContainsKey(target))
                connected.Add(target);
            else
                unconnected.Add(target);
        }

        // Connected targets sit at RightColX, row-aligned with their source
        double xConnected = RightColX;
        foreach (var node in connected)
            node.Location = new Point(xConnected, sourceY[node]);

        if (unconnected.Count == 0) return;

        // Unconnected targets: prefix-grouped, dynamically sized columns
        var ordered = unconnected
            .GroupBy(n => ExtractPrefix(n.Name))
            .OrderByDescending(g => g.Count())
            .SelectMany(g => g.OrderBy(n => n.Name))
            .ToList();

        int numUncColumns = Math.Clamp((int)Math.Ceiling((double)ordered.Count / 10.0), 2, 5);
        int ideal = (int)Math.Ceiling((double)ordered.Count / numUncColumns);

        var columns = new List<string>[numUncColumns];
        for (int i = 0; i < numUncColumns; i++)
            columns[i] = [];

        int col = 0;
        int count = 0;
        foreach (var node in ordered)
        {
            if (count >= ideal && col < numUncColumns - 1)
            {
                col++;
                count = 0;
            }
            columns[col].Add(node.Name);
            count++;
        }

        double x = xConnected + (connected.Count > 0 ? NodeWidth + TargetColumnGap : 0);
        for (int c = 0; c < numUncColumns; c++)
        {
            double y = TopMargin;
            foreach (var name in columns[c])
            {
                var node = targetNodes.First(n => n.Name == name);
                node.Location = new Point(x, y);
                y += NodeSpacing;
            }
            x += NodeWidth + TargetColumnGap;
        }
    }

    public void RemoveEmptyLayers()
    {
        var empty = Nodes.Where(n => n.IsSource && n.IsEmpty).ToList();
        foreach (var node in empty)
        {
            var conns = Connections.Where(c => c.Source == node || c.Target == node).ToList();
            foreach (var c in conns)
            {
                c.Source.IsMapped = false;
                if (!Connections.Any(other => other != c && other.Target == c.Target))
                    c.Target.IsMapped = false;
                Connections.Remove(c);
            }
            Nodes.Remove(node);
        }
        ArrangeTargetsInColumns();
        ApplyFilters();
    }

}

public class PendingConnectionViewModel
{
    private readonly LayerEditorViewModel _editor;
    private readonly Dictionary<string, LayerNodeViewModel> _sourceMap;
    private readonly Dictionary<string, LayerNodeViewModel> _standardMap;
    private LayerNodeViewModel? _pendingSource;

    public ICommand StartCommand { get; }
    public ICommand FinishCommand { get; }

    public PendingConnectionViewModel(
        LayerEditorViewModel editor,
        Dictionary<string, LayerNodeViewModel> sourceMap,
        Dictionary<string, LayerNodeViewModel> standardMap)
    {
        _editor = editor;
        _sourceMap = sourceMap;
        _standardMap = standardMap;

        StartCommand = new DelegateCommand<object>(param =>
        {
            if (param is LayerNodeViewModel source)
                _pendingSource = source;
        });

        FinishCommand = new DelegateCommand<object>(param =>
        {
            if (param is not LayerNodeViewModel target || _pendingSource is null) return;
            if (_pendingSource == target) return;
            if (!_pendingSource.IsSource) return;
            if (target.IsSource) return;

            if (_editor.Connections.Any(c => c.Source == _pendingSource))
            {
                var old = _editor.Connections.First(c => c.Source == _pendingSource);
                old.Source.IsMapped = false;
                if (!_editor.Connections.Any(other => other != old && other.Target == old.Target))
                    old.Target.IsMapped = false;
                _editor.Connections.Remove(old);
            }

            _editor.Connections.Add(new LayerConnectionViewModel(_pendingSource, target, ConnectionMatchSource.Manual));
            _pendingSource = null;
        });
    }
}

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T?> _action;
    private readonly Func<T?, bool>? _condition;

    public event EventHandler? CanExecuteChanged;

    public DelegateCommand(Action<T?> action, Func<T?, bool>? condition = null)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _condition = condition;
    }

    public bool CanExecute(object? parameter)
        => _condition?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter)
        => _action((T?)parameter);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
