using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AcLayerStandardizer.UI;

public class LayerConnectionViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public LayerConnectionViewModel(
        LayerNodeViewModel source,
        LayerNodeViewModel target,
        ConnectionMatchSource matchSource = ConnectionMatchSource.Manual)
    {
        Source = source;
        Target = target;
        MatchSource = matchSource;
        source.IsMapped = true;
        target.IsMapped = true;
        source.SetConnectorColor(matchSource);
        target.SetConnectorColor(matchSource);
    }

    public LayerNodeViewModel Source { get; }
    public LayerNodeViewModel Target { get; }
    public ConnectionMatchSource MatchSource { get; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public string Stroke => MatchSource switch
    {
        ConnectionMatchSource.ExactName => "#008300",
        ConnectionMatchSource.Memory => "#3987e5",
        ConnectionMatchSource.Heuristic => "#c98500",
        ConnectionMatchSource.Manual => "#9085e9",
        _ => "#9085e9",
    };

    public DoubleCollection? StrokeDashCollection => MatchSource switch
    {
        ConnectionMatchSource.Heuristic => new DoubleCollection([4, 3]),
        _ => null,
    };
}
