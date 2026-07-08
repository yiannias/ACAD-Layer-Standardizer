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
}
