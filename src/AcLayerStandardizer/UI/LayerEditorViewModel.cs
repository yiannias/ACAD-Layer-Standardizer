using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Nodify;

namespace AcLayerStandardizer.UI;

public class LayerEditorViewModel
{
    private const double NodeWidth = 170;
    private const double NodeHeight = 30;
    private const double NodeSpacing = 44;
    private const double TopMargin = 40;
    private const double LeftColX = 50;
    private const double RightColX = 550;

    public ObservableCollection<LayerNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<LayerConnectionViewModel> Connections { get; } = [];

    public PendingConnectionViewModel PendingConnection { get; }

    public ICommand RemoveConnectionCommand { get; }
    public ICommand DisconnectConnectorCommand { get; }

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
        Dictionary<string, string>? existingMappings = null)
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
            var loc = new Point(RightColX, TopMargin + i * NodeSpacing);
            var vm = new LayerNodeViewModel(standardLayers[i], false, loc);
            standardModels.Add(vm);
            Nodes.Add(vm);
        }

        var sourceMap = sourceModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var standardMap = standardModels.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        if (existingMappings != null)
        {
            foreach (var kvp in existingMappings)
            {
                if (sourceMap.TryGetValue(kvp.Key, out var src)
                    && standardMap.TryGetValue(kvp.Value, out var tgt))
                {
                    Connections.Add(new LayerConnectionViewModel(src, tgt));
                }
            }
        }

        PendingConnection = new PendingConnectionViewModel(this, sourceMap, standardMap);

        RemoveConnectionCommand = new DelegateCommand<LayerConnectionViewModel>(conn =>
        {
            if (conn is null) return;
            conn.Source.IsMapped = false;
            Connections.Remove(conn);
        });

        DisconnectConnectorCommand = new DelegateCommand<LayerNodeViewModel>(node =>
        {
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];
                if (c.Source == node || c.Target == node)
                {
                    c.Source.IsMapped = false;
                    Connections.RemoveAt(i);
                }
            }
        });
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
                _editor.Connections.Remove(old);
            }

            _editor.Connections.Add(new LayerConnectionViewModel(_pendingSource, target));
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
