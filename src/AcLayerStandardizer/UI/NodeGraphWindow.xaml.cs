using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AcLayerStandardizer.UI;

public partial class NodeGraphWindow : Window
{
    private const double NodeWidth = 170;
    private const double NodeHeight = 30;
    private const double ColumnGap = 80;
    private const double NodeSpacing = 44;
    private const double TopMargin = 30;
    private const double LeftMargin = 40;
    private const double RightMargin = 40;
    private const double ConnectionHitWidth = 14;

    private readonly List<string> _sourceLayers;
    private readonly List<string> _allStandardLayers;
    private readonly Dictionary<string, string> _mappings; // source → target
    private string? _selectedSource;
    private bool _hasChanges;

    public IReadOnlyDictionary<string, string> ResultMappings => _mappings;

    public NodeGraphWindow(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string> existingMappings)
    {
        InitializeComponent();

        _sourceLayers = sourceLayers;
        _allStandardLayers = standardLayers;
        _mappings = new Dictionary<string, string>(existingMappings, StringComparer.OrdinalIgnoreCase);

        RenderGraph();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderGraph();
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();

        var canvasW = Math.Max(GraphCanvas.ActualWidth, 850);
        var targetX = canvasW - NodeWidth - RightMargin;

        DrawConnections(targetX);
        DrawSourceNodes(targetX);
        DrawTargetNodes(targetX);
        DrawColumnLabels(targetX, canvasW);
    }

    private void DrawColumnLabels(double targetX, double canvasW)
    {
        var titleStyle = new TextStyle("#ffa726", 13, true);
        AddLabel(LeftMargin, 6, "Drawing Layers", titleStyle);
        AddLabel(targetX, 6, "Standard Layers", titleStyle);

        var midX = (LeftMargin + NodeWidth + targetX) / 2;
        var unmatched = _sourceLayers.Count(l => !_mappings.ContainsKey(l));
        AddLabel(midX - 60, 6, $"Mappings ({_mappings.Count})", new TextStyle("#66bb6a", 11, false));
    }

    private void DrawSourceNodes(double targetX)
    {
        for (int i = 0; i < _sourceLayers.Count; i++)
        {
            var name = _sourceLayers[i];
            var y = TopMargin + i * NodeSpacing;
            var isMapped = _mappings.ContainsKey(name);
            var isSelected = name == _selectedSource;

            var border = CreateNode(name, y, isMapped, isSelected, isSource: true);
            Canvas.SetLeft(border, LeftMargin);
            Canvas.SetTop(border, y);
            GraphCanvas.Children.Add(border);
        }
    }

    private void DrawTargetNodes(double targetX)
    {
        for (int i = 0; i < _allStandardLayers.Count; i++)
        {
            var name = _allStandardLayers[i];
            var y = TopMargin + i * NodeSpacing;

            var border = CreateNode(name, y, isMapped: false, isSelected: false, isSource: false);
            Canvas.SetLeft(border, targetX);
            Canvas.SetTop(border, y);
            GraphCanvas.Children.Add(border);
        }
    }

    private Border CreateNode(string name, double y, bool isMapped, bool isSelected, bool isSource)
    {
        var bg = isSelected ? "#ffa726" : isMapped ? "#2e7d32" : "#424242";
        var textColor = isSelected ? "#1e1e1e" : "#eee";

        var text = new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textColor)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var border = new Border
        {
            Child = text,
            Width = NodeWidth,
            Height = NodeHeight,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isSelected ? "#fff" : "#666")),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            Tag = name,
            Cursor = Cursors.Hand,
        };

        if (isSource)
            border.MouseLeftButtonDown += (_, _) => OnSourceClick(name);
        else
            border.MouseLeftButtonDown += (_, _) => OnTargetClick(name);

        return border;
    }

    private void DrawConnections(double targetX)
    {
        foreach (var kvp in _mappings)
        {
            var srcIdx = _sourceLayers.IndexOf(kvp.Key);
            var tgtIdx = _allStandardLayers.IndexOf(kvp.Value);
            if (srcIdx < 0 || tgtIdx < 0) continue;

            var srcY = TopMargin + srcIdx * NodeSpacing + NodeHeight / 2;
            var tgtY = TopMargin + tgtIdx * NodeSpacing + NodeHeight / 2;

            var srcRight = LeftMargin + NodeWidth;
            var tgtLeft = targetX;

            var mid1X = srcRight + (tgtLeft - srcRight) * 0.35;
            var mid2X = srcRight + (tgtLeft - srcRight) * 0.65;

            var path = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(120, 102, 187, 255)),
                StrokeThickness = 2.5,
                Fill = Brushes.Transparent,
                Tag = kvp.Key,
                Cursor = Cursors.Hand,
            };

            var geo = new PathGeometry();
            var fig = new PathFigure(
                new Point(srcRight, srcY),
                [
                    new BezierSegment(
                        new Point(mid1X, srcY),
                        new Point(mid2X, tgtY),
                        new Point(tgtLeft, tgtY),
                        true),
                ],
                false);
            geo.Figures.Add(fig);
            path.Data = geo;

            path.MouseLeftButtonDown += (_, _) => OnConnectionClick(kvp.Key);
            path.MouseEnter += (_, _) => path.Stroke = new SolidColorBrush(Color.FromRgb(239, 83, 80));
            path.MouseLeave += (_, _) => path.Stroke = new SolidColorBrush(Color.FromArgb(120, 102, 187, 255));

            GraphCanvas.Children.Add(path);

            var hitPath = new Path
            {
                Stroke = Brushes.Transparent,
                StrokeThickness = ConnectionHitWidth,
                Fill = Brushes.Transparent,
                Data = geo.Clone(),
                Cursor = Cursors.Hand,
            };
            hitPath.MouseLeftButtonDown += (_, _) => OnConnectionClick(kvp.Key);
            GraphCanvas.Children.Add(hitPath);

            var labelMidX = (srcRight + tgtLeft) / 2 - 20;
            var labelMidY = (srcY + tgtY) / 2;
            var label = new TextBlock
            {
                Text = "\u2192",
                Foreground = new SolidColorBrush(Color.FromArgb(160, 102, 187, 255)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
            };
            Canvas.SetLeft(label, labelMidX);
            Canvas.SetTop(label, labelMidY - 10);
            GraphCanvas.Children.Add(label);
        }
    }

    private void AddLabel(double x, double y, string text, TextStyle style)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Color)),
            FontSize = style.Size,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        GraphCanvas.Children.Add(tb);
    }

    private void OnSourceClick(string name)
    {
        _selectedSource = _selectedSource == name ? null : name;
        StatusBar.Text = _selectedSource is null
            ? "Click a source layer to select it."
            : $"Selected: {_selectedSource} — now click a standard layer to map it.";
        RenderGraph();
    }

    private void OnTargetClick(string name)
    {
        if (_selectedSource is null)
        {
            if (_mappings.ContainsValue(name))
            {
                var src = _mappings.FirstOrDefault(kv => kv.Value == name).Key;
                _mappings.Remove(src);
                _hasChanges = true;
                StatusBar.Text = $"Removed mapping → {name}";
                RenderGraph();
            }
            return;
        }

        if (_selectedSource == name)
        {
            _selectedSource = null;
            RenderGraph();
            return;
        }

        var sourceName = _selectedSource;
        var prevTarget = _mappings.TryGetValue(sourceName, out var oldTarget) ? oldTarget : null;
        _mappings[sourceName] = name;

        var otherSource = _mappings.FirstOrDefault(kv => kv.Value == name && kv.Key != sourceName).Key;
        if (otherSource is not null)
            _mappings.Remove(otherSource);

        _hasChanges = true;
        _selectedSource = null;
        StatusBar.Text = prevTarget is null
            ? $"Mapped: {sourceName} → {name}"
            : $"Re-mapped: {sourceName} now points to {name} (was {prevTarget})";
        RenderGraph();
    }

    private void OnConnectionClick(string sourceName)
    {
        _mappings.Remove(sourceName);
        _hasChanges = true;
        _selectedSource = null;
        StatusBar.Text = $"Removed mapping from {sourceName}";
        RenderGraph();
    }

    private void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Canvas)
        {
            _selectedSource = null;
            StatusBar.Text = "Ready";
            RenderGraph();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_hasChanges && _mappings.Count == 0)
        {
            StatusBar.Text = "No mappings to save.";
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_hasChanges)
        {
            var result = MessageBox.Show(
                "Discard unsaved changes?", "Layer Mapping Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }
        DialogResult = false;
        Close();
    }

    private readonly record struct TextStyle(string Color, double Size, bool Bold);
}
