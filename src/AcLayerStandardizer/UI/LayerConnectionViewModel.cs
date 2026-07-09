using System.Windows.Media;

namespace AcLayerStandardizer.UI;

public enum ConnectionMatchSource
{
    ExactName,
    Memory,
    Heuristic,
    Manual,
}

public class LayerConnectionViewModel
{
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

    public string Stroke => MatchSource switch
    {
        ConnectionMatchSource.ExactName => "#00e676",
        ConnectionMatchSource.Memory => "#448aff",
        ConnectionMatchSource.Heuristic => "#ffd740",
        ConnectionMatchSource.Manual => "#7c4dff",
        _ => "#7c4dff",
    };

    public DoubleCollection? StrokeDashCollection => MatchSource switch
    {
        ConnectionMatchSource.Memory or ConnectionMatchSource.Heuristic => new DoubleCollection([4, 3]),
        _ => null,
    };
}
