using System.Windows;

namespace AcLayerStandardizer.UI;

public class LayerNodeViewModel : ObservableObject
{
    // Validated via the dataviz palette validator against this editor's dark
    // surface (#141414): worst adjacent CVD separation ΔE 21.5+, well clear
    // of the >=12 target -- no secondary encoding (labels/texture) required.
    // "Selected" (see NodeGraphWindow.xaml) uses a 5th, unrelated hue so it
    // can never be confused with a match-type color.
    private static readonly Dictionary<ConnectionMatchSource, string> SourceColors = new()
    {
        [ConnectionMatchSource.ExactName] = "#008300",
        [ConnectionMatchSource.Memory] = "#3987e5",
        [ConnectionMatchSource.Heuristic] = "#c98500",
        [ConnectionMatchSource.Manual] = "#9085e9",
    };

    // Node fill once mapped: the base node gray (#424242) blended ~25% with
    // the match-type hue above, so the type reads on the node itself (not
    // just the 8px connector dot) without dropping the dark node-label
    // text's contrast the way a fully-saturated fill would.
    private static readonly Dictionary<ConnectionMatchSource, string> MappedBackgroundColors = new()
    {
        [ConnectionMatchSource.ExactName] = "#325232",
        [ConnectionMatchSource.Memory] = "#40536b",
        [ConnectionMatchSource.Heuristic] = "#645332",
        [ConnectionMatchSource.Manual] = "#56536c",
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

    // Target-side only: the smart-group tags this layer resolved to via
    // Core.LayerCategorizer, and whether it's an excluded junk/system layer
    // that should never be shown regardless of any Target Filter toggle.
    public HashSet<string> TargetTags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsAlwaysHiddenTarget { get; set; }

    // The single most-specific tag (fewest members in this template) --
    // Target Filter visibility keys off this alone. Pure AND/OR over the
    // full multi-tag set both failed in practice: OR meant toggling
    // "Equipment" off couldn't hide A-FL-EQUIPMENT (it stayed via "Floor
    // Plan"), AND meant toggling the broad "Architectural" off hid every
    // single A-* layer even with its specific group (Ceiling Plan, Wall)
    // explicitly on.
    public string? PrimaryTargetTag { get; set; }

    // Source/Target canvas labels are just LayerNodeViewModel instances with
    // IsHeader set -- reusing Location/pan/zoom/z-order plumbing rather than
    // introducing a second item type into the shared Nodes collection (which
    // would need every LINQ query in LayerEditorViewModel to filter/cast by
    // type). Name doubles as the header's title ("Source"/"Target").
    public bool IsHeader { get; init; }

    private string _subtitle = "";
    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public void SetConnectorColor(ConnectionMatchSource source)
    {
        ConnectorColor = SourceColors.TryGetValue(source, out var c) ? c : "#9085e9";
        BackgroundColor = MappedBackgroundColors.TryGetValue(source, out var bg) ? bg : "#424242";
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
