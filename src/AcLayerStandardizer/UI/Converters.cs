using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AcLayerStandardizer.UI;

public class BooleanToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public static readonly BooleanToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public static readonly InverseBooleanToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

// Used to cap the Target Filter panel's MaxHeight to "available window
// height minus margin" so the panel shrinks to fit a short list (matching
// the Legend panel's tight look) instead of always stretching to the full
// window height with empty space below a short button list, while still
// capping+scrolling when the list is long.
public class SubtractDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double d = value is double dv ? dv : 0;
        double offset = parameter is string s && double.TryParse(s, out var p) ? p : 0;
        return Math.Max(0, d - offset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// (ViewportPanOffsetConverter used to live here: it hand-computed the grid
// brush's pan offset from ViewportZoom/ViewportLocation. Removed 2026-07-11
// when the grid brush's Transform was bound directly to the editor's own
// ViewportTransform, which is the same math maintained by Nodify itself.)

public class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}