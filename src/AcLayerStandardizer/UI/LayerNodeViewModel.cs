using System.Windows;

namespace AcLayerStandardizer.UI;

public class LayerNodeViewModel : ObservableObject
{
    private static readonly Dictionary<ConnectionMatchSource, string> SourceColors = new()
    {
        [ConnectionMatchSource.ExactName] = "#8bc34a",
        [ConnectionMatchSource.Memory] = "#448aff",
        [ConnectionMatchSource.Heuristic] = "#ffd740",
        [ConnectionMatchSource.Manual] = "#7c4dff",
    };
    private const double NodeWidth = 280;

    public string Name { get; }
    public bool IsSource { get; }

    private bool _isMapped;
    public bool IsMapped
    {
        get => _isMapped;
        set => SetProperty(ref _isMapped, value);
    }

    private bool _isEmpty;
    public bool IsEmpty
    {
        get => _isEmpty;
        set => SetProperty(ref _isEmpty, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private string _backgroundColor = "#424242";
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => SetProperty(ref _backgroundColor, value);
    }

    private string _connectorColor = "#aaa";
    public string ConnectorColor
    {
        get => _connectorColor;
        set => SetProperty(ref _connectorColor, value);
    }

    public void SetConnectorColor(ConnectionMatchSource source)
    {
        ConnectorColor = SourceColors.TryGetValue(source, out var c) ? c : "#7c4dff";
    }

    private Point _location;
    public Point Location
    {
        get => _location;
        set
        {
            SetProperty(ref _location, value);
            UpdateAnchor();
        }
    }

    private Point _anchor;
    public Point Anchor
    {
        get => _anchor;
        private set => SetProperty(ref _anchor, value);
    }

    private Size _size;
    public Size Size
    {
        get => _size;
        set
        {
            SetProperty(ref _size, value);
        }
    }

    public void UpdateAnchor()
    {
        Anchor = IsSource
            ? new Point(_location.X + NodeWidth, _location.Y + 15)
            : new Point(_location.X, _location.Y + 15);
    }

    public LayerNodeViewModel(string name, bool isSource, Point location)
    {
        Name = name;
        IsSource = isSource;
        _location = location;
        _size = new Size(NodeWidth, 30);
        UpdateAnchor();
    }
}
