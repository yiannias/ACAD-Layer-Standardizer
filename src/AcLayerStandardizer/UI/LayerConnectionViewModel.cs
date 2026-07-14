using System;
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
        ConnectionMatchSource matchSource = ConnectionMatchSource.Manual,
        double confidence = 1.0)
    {
        Source = source;
        Target = target;
        MatchSource = matchSource;
        Confidence = confidence;
        source.IsMapped = true;
        target.IsMapped = true;
        source.SetConnectorColor(matchSource);
        target.SetConnectorColor(matchSource);
    }

    public LayerNodeViewModel Source { get; }
    public LayerNodeViewModel Target { get; }
    public ConnectionMatchSource MatchSource { get; }

    // Only meaningful for Heuristic connections (the matcher's score);
    // defaults to 1.0 for ExactName/Memory/Manual, which don't carry one.
    public double Confidence { get; }

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

    // Confidence range the matcher can ever produce (PluginConfig.HeuristicThreshold's
    // default floor through a perfect match) -- also the sub-filter slider's range.
    private const double ConfidenceFloor = 0.6;
    private const double ConfidenceCeiling = 1.0;
    private const string MutedHeuristicColor = "#6b5738";
    private const string VividHeuristicColor = "#c98500";

    public string Stroke => MatchSource switch
    {
        ConnectionMatchSource.ExactName => "#008300",
        ConnectionMatchSource.Memory => "#3987e5",
        ConnectionMatchSource.Heuristic => InterpolateColor(MutedHeuristicColor, VividHeuristicColor,
            (Confidence - ConfidenceFloor) / (ConfidenceCeiling - ConfidenceFloor)),
        ConnectionMatchSource.Manual => "#9085e9",
        _ => "#9085e9",
    };

    public DoubleCollection? StrokeDashCollection => MatchSource switch
    {
        ConnectionMatchSource.Heuristic => new DoubleCollection([4, 3]),
        _ => null,
    };

    private static string InterpolateColor(string from, string to, double t)
    {
        // Math.Min/Max instead of Math.Clamp: the latter doesn't exist on net48
        t = Math.Min(1.0, Math.Max(0.0, t));

        var fromColor = (Color)ColorConverter.ConvertFromString(from);
        var toColor = (Color)ColorConverter.ConvertFromString(to);

        byte Lerp(byte a, byte b) => (byte)(a + (b - a) * t);

        var r = Lerp(fromColor.R, toColor.R);
        var g = Lerp(fromColor.G, toColor.G);
        var b = Lerp(fromColor.B, toColor.B);

        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
