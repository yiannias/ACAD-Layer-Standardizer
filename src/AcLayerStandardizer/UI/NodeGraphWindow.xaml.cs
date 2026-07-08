using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AcLayerStandardizer.UI;

public partial class NodeGraphWindow : Window
{
    private const double NodeWidth = 170;
    private const double NodeHeight = 30;
    private const double NodeSpacing = 44;
    private const double TopMargin = 30;
    private const double LeftMargin = 40;
    private const double RightMargin = 40;
    private const double ConnectionHitWidth = 14;
    private const double DragThreshold = 6;
    private const double MinZoom = 0.2;
    private const double MaxZoom = 3.0;
    private const double ZoomFactor = 1.12;

    private readonly List<string> _sourceLayers;
    private readonly List<string> _allStandardLayers;
    private readonly Dictionary<string, string> _mappings;
    private string? _selectedSource;
    private bool _hasChanges;

    private string? _dragCandidate;
    private Point _dragStartPos;
    private bool _isDragging;
    private Point _lastDragCursor;
    private Path? _tempBezier;

    private bool _isPanning;
    private Point _panStart;
    private double _panOriginX;
    private double _panOriginY;

    private double _zoomLevel = 1.0;

    public IReadOnlyDictionary<string, string> ResultMappings => _mappings;

    public NodeGraphWindow(
        List<string> sourceLayers,
        List<string> standardLayers,
        Dictionary<string, string> existingMappings)
    {
        InitializeComponent();

        _sourceLayers = [.. sourceLayers.OrderBy(n => n)];
        _allStandardLayers = [.. standardLayers.OrderBy(n => n == "0" ? 0 : 1).ThenBy(n => n)];
        _mappings = new Dictionary<string, string>(existingMappings, StringComparer.OrdinalIgnoreCase);

        SourceInitialized += (_, _) => EnableDarkTitleBar();
        RenderGraph();
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private double TargetX => Math.Max(GraphCanvas.ActualWidth, 850) - NodeWidth - RightMargin;

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderGraph();

    private void RenderGraph()
    {
        var canvasW = Math.Max(GraphCanvas.ActualWidth, 850);
        var tx = TargetX;

        GraphCanvas.Children.Clear();
        DrawConnections(tx);
        DrawSourceNodes(tx);
        DrawTargetNodes(tx);
        DrawColumnLabels(tx, canvasW);
    }

    private void DrawColumnLabels(double targetX, double canvasW)
    {
        AddLabel(LeftMargin, 6, "Drawing Layers", "#ffa726", 13, true);
        AddLabel(targetX, 6, "Standard Layers", "#ffa726", 13, true);

        var midX = (LeftMargin + NodeWidth + targetX) / 2;
        AddLabel(midX - 60, 6, $"Mappings ({_mappings.Count})", "#66bb6a", 11, false);
    }

    private void DrawSourceNodes(double targetX)
    {
        for (int i = 0; i < _sourceLayers.Count; i++)
        {
            var name = _sourceLayers[i];
            var y = TopMargin + i * NodeSpacing;
            var border = CreateNode(name, y, isSource: true, targetX);
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
            var border = CreateNode(name, y, isSource: false, targetX);
            Canvas.SetLeft(border, targetX);
            Canvas.SetTop(border, y);
            GraphCanvas.Children.Add(border);
        }
    }

    private Border CreateNode(string name, double y, bool isSource, double targetX)
    {
        var isMapped = isSource ? _mappings.ContainsKey(name) : _mappings.ContainsValue(name);
        var isSelected = isSource && name == _selectedSource;

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
            DrawOneConnection(kvp.Key, LeftMargin + NodeWidth, srcY, targetX, tgtY);
        }
    }

    private void DrawOneConnection(string? tagKey, double x1, double y1, double x2, double y2)
    {
        var mid1X = x1 + (x2 - x1) * 0.35;
        var mid2X = x1 + (x2 - x1) * 0.65;

        var geo = MakeBezier(x1, y1, mid1X, y1, mid2X, y2, x2, y2);

        var path = new Path
        {
            Stroke = new SolidColorBrush(Color.FromArgb(120, 102, 187, 255)),
            StrokeThickness = 2.5,
            Fill = Brushes.Transparent,
            Data = geo,
            Tag = tagKey,
            Cursor = Cursors.Hand,
        };
        path.MouseEnter += (_, _) => path.Stroke = new SolidColorBrush(Color.FromRgb(239, 83, 80));
        path.MouseLeave += (_, _) => path.Stroke = new SolidColorBrush(Color.FromArgb(120, 102, 187, 255));

        var hit = new Path
        {
            Stroke = Brushes.Transparent,
            StrokeThickness = ConnectionHitWidth,
            Fill = Brushes.Transparent,
            Data = geo.Clone(),
            Cursor = Cursors.Hand,
        };

        GraphCanvas.Children.Add(path);
        GraphCanvas.Children.Add(hit);

        var labelMidX = (x1 + x2) / 2 - 10;
        var labelMidY = (y1 + y2) / 2 - 8;
        var arrow = new TextBlock
        {
            Text = "\u2192",
            Foreground = new SolidColorBrush(Color.FromArgb(160, 102, 187, 255)),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
        };
        Canvas.SetLeft(arrow, labelMidX);
        Canvas.SetTop(arrow, labelMidY);
        GraphCanvas.Children.Add(arrow);
    }

    private static PathGeometry MakeBezier(double x1, double y1, double cx1, double cy1,
                                            double cx2, double cy2, double x2, double y2)
    {
        var geo = new PathGeometry();
        geo.Figures.Add(new PathFigure(
            new Point(x1, y1),
            [new BezierSegment(new Point(cx1, cy1), new Point(cx2, cy2), new Point(x2, y2), true)],
            false));
        return geo;
    }

    private void AddLabel(double x, double y, string text, string color, double size, bool bold)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        GraphCanvas.Children.Add(tb);
    }

    private string? HitTestNodeAt(Point pos, bool targetsOnly)
    {
        if (!targetsOnly)
        {
            for (int i = 0; i < _sourceLayers.Count; i++)
            {
                var y = TopMargin + i * NodeSpacing;
                var r = new Rect(LeftMargin, y, NodeWidth, NodeHeight);
                if (r.Contains(pos)) return _sourceLayers[i];
            }
        }

        for (int i = 0; i < _allStandardLayers.Count; i++)
        {
            var y = TopMargin + i * NodeSpacing;
            var r = new Rect(TargetX, y, NodeWidth, NodeHeight);
            if (r.Contains(pos)) return _allStandardLayers[i];
        }

        return null;
    }

    private string? HitTestConnectionAt(Point pos)
    {
        foreach (var kvp in _mappings)
        {
            var srcIdx = _sourceLayers.IndexOf(kvp.Key);
            var tgtIdx = _allStandardLayers.IndexOf(kvp.Value);
            if (srcIdx < 0 || tgtIdx < 0) continue;

            var srcY = TopMargin + srcIdx * NodeSpacing + NodeHeight / 2;
            var tgtY = TopMargin + tgtIdx * NodeSpacing + NodeHeight / 2;
            var tx = TargetX;

            var mid1X = LeftMargin + NodeWidth + (tx - LeftMargin - NodeWidth) * 0.35;
            var mid2X = LeftMargin + NodeWidth + (tx - LeftMargin - NodeWidth) * 0.65;

            var geo = MakeBezier(LeftMargin + NodeWidth, srcY, mid1X, srcY, mid2X, tgtY, tx, tgtY);
            if (geo.FillContains(pos))
                return kvp.Key;
        }
        return null;
    }

    // ── Preview mouse handlers (tunnel down, intercept before child elements) ──

    private void OnCanvasPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(GraphCanvas);
        _dragCandidate = HitTestNodeAt(pos, targetsOnly: false);

        if (_dragCandidate != null && _sourceLayers.Contains(_dragCandidate))
        {
            _dragStartPos = pos;
            _isDragging = false;
            Mouse.Capture(GraphCanvas);
            e.Handled = true;
        }
        else if (_dragCandidate != null && _allStandardLayers.Contains(_dragCandidate))
        {
            HandleTargetClick(_dragCandidate);
            e.Handled = true;
        }
        else
        {
            var conn = HitTestConnectionAt(pos);
            if (conn != null)
            {
                HandleConnectionClick(conn);
                e.Handled = true;
            }
            else
            {
                _selectedSource = null;
                StatusBar.Text = "Ready";
                RenderGraph();
                e.Handled = true;
            }
        }
    }

    private void OnCanvasPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || !_sourceLayers.Contains(_dragCandidate)) return;

        var pos = e.GetPosition(GraphCanvas);

        if (!_isDragging)
        {
            var dx = pos.X - _dragStartPos.X;
            var dy = pos.Y - _dragStartPos.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > DragThreshold)
            {
                _isDragging = true;
                _selectedSource = null;
                RenderGraph();
            }
        }

        if (_isDragging)
        {
            _lastDragCursor = pos;

            var srcIdx = _sourceLayers.IndexOf(_dragCandidate);
            if (srcIdx < 0) return;

            var srcX = LeftMargin + NodeWidth;
            var srcY = TopMargin + srcIdx * NodeSpacing + NodeHeight / 2;

            var geo = MakeBezier(srcX, srcY, srcX + 40, srcY, pos.X - 40, pos.Y, pos.X, pos.Y);

            if (_tempBezier is null)
            {
                _tempBezier = new Path
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 167, 38)),
                    StrokeThickness = 2.5,
                    StrokeDashArray = new DoubleCollection([4, 3]),
                    Fill = Brushes.Transparent,
                };
                GraphCanvas.Children.Add(_tempBezier);
            }

            _tempBezier.Data = geo;
            e.Handled = true;
        }
    }

    private void OnCanvasPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCandidate == null || !_sourceLayers.Contains(_dragCandidate))
        {
            Mouse.Capture(null);
            return;
        }

        _tempBezier = null;

        if (_isDragging)
        {
            var target = HitTestNodeAt(_lastDragCursor, targetsOnly: true);
            if (target != null && _allStandardLayers.Contains(target))
            {
                CreateMapping(_dragCandidate, target);
            }

            _isDragging = false;
            _dragCandidate = null;
            Mouse.Capture(null);
            RenderGraph();
            e.Handled = true;
        }
        else
        {
            _isDragging = false;
            Mouse.Capture(null);

            if (_sourceLayers.Contains(_dragCandidate))
            {
                HandleSourceClick(_dragCandidate);
            }
            _dragCandidate = null;
            e.Handled = true;
        }
    }

    private void OnCanvasPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(GraphCanvas);
        var oldZoom = _zoomLevel;

        _zoomLevel *= e.Delta > 0 ? ZoomFactor : (1.0 / ZoomFactor);
        _zoomLevel = Math.Clamp(_zoomLevel, MinZoom, MaxZoom);

        ZoomTransform.ScaleX = _zoomLevel;
        ZoomTransform.ScaleY = _zoomLevel;

        PanTransform.X = pos.X - (pos.X - PanTransform.X) * (_zoomLevel / oldZoom);
        PanTransform.Y = pos.Y - (pos.Y - PanTransform.Y) * (_zoomLevel / oldZoom);

        ZoomLabel.Text = $"{_zoomLevel * 100:F0}%";
        e.Handled = true;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;

        var pos = e.GetPosition(GraphCanvas);
        _isPanning = true;
        _panStart = pos;
        _panOriginX = PanTransform.X;
        _panOriginY = PanTransform.Y;
        Mouse.Capture(GraphCanvas);
        e.Handled = true;

        var prevMove = (MouseEventHandler?)null;
        prevMove = null!;
        prevMove = (_, me) =>
        {
            if (!_isPanning) return;
            var cur = me.GetPosition(GraphCanvas);
            PanTransform.X = _panOriginX + (cur.X - _panStart.X);
            PanTransform.Y = _panOriginY + (cur.Y - _panStart.Y);
            me.Handled = true;
        };
        GraphCanvas.PreviewMouseMove += prevMove;

        void OnMiddleUp(object? _, MouseButtonEventArgs ue)
        {
            if (ue.ChangedButton != MouseButton.Middle) return;
            _isPanning = false;
            Mouse.Capture(null);
            GraphCanvas.PreviewMouseMove -= prevMove;
            GraphCanvas.PreviewMouseUp -= OnMiddleUp;
            ue.Handled = true;
        }
        GraphCanvas.PreviewMouseUp += OnMiddleUp;
    }

    // ── Click handlers ──

    private void HandleSourceClick(string name)
    {
        _selectedSource = _selectedSource == name ? null : name;
        StatusBar.Text = _selectedSource is null
            ? "Click a source layer to select it."
            : $"Selected: {_selectedSource} — now click a standard layer to map it.";
        RenderGraph();
    }

    private void HandleTargetClick(string name)
    {
        if (_selectedSource is null)
        {
            var toRemove = _mappings.Where(kv => kv.Value == name).Select(kv => kv.Key).ToList();
            if (toRemove.Count > 0)
            {
                foreach (var s in toRemove)
                    _mappings.Remove(s);
                _hasChanges = true;
                StatusBar.Text = toRemove.Count == 1
                    ? $"Removed mapping → {name}"
                    : $"Removed {toRemove.Count} mappings → {name}";
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

        CreateMapping(_selectedSource, name);
    }

    private void CreateMapping(string source, string target)
    {
        var prevTarget = _mappings.TryGetValue(source, out var oldTarget) ? oldTarget : null;

        _mappings[source] = target;

        _hasChanges = true;
        _selectedSource = null;
        StatusBar.Text = prevTarget is null
            ? $"Mapped: {source} → {target}"
            : $"Re-mapped: {source} now points to {target} (was {prevTarget})";
        RenderGraph();
    }

    private void HandleConnectionClick(string sourceName)
    {
        _mappings.Remove(sourceName);
        _hasChanges = true;
        _selectedSource = null;
        StatusBar.Text = $"Removed mapping from {sourceName}";
        RenderGraph();
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
}
