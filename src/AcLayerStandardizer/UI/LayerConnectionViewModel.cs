namespace AcLayerStandardizer.UI;

public class LayerConnectionViewModel
{
    public LayerConnectionViewModel(LayerNodeViewModel source, LayerNodeViewModel target)
    {
        Source = source;
        Target = target;
        source.IsMapped = true;
        target.IsMapped = true;
    }

    public LayerNodeViewModel Source { get; }
    public LayerNodeViewModel Target { get; }
}
