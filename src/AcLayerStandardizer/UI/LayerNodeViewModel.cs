using System.Windows;

namespace AcLayerStandardizer.UI;

public class LayerNodeViewModel : ObservableObject
{
    public string Name { get; }
    public bool IsSource { get; }

    private bool _isMapped;
    public bool IsMapped
    {
        get => _isMapped;
        set => SetProperty(ref _isMapped, value);
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
            UpdateAnchor();
        }
    }

    public void UpdateAnchor()
    {
        Anchor = IsSource
            ? new Point(_location.X + _size.Width, _location.Y + _size.Height / 2)
            : new Point(_location.X, _location.Y + _size.Height / 2);
    }

    public LayerNodeViewModel(string name, bool isSource, Point location)
    {
        Name = name;
        IsSource = isSource;
        _location = location;
        _size = new Size(170, 30);
        UpdateAnchor();
    }
}
