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

// Background grid brush was static "wallpaper" -- didn't pan/zoom with the
// node canvas (chris, 2026-07-10). Computes the pixel offset needed to shift
// the tiled DrawingBrush's pattern in the opposite direction of a pan, at
// the current zoom scale, so the grid appears to stay anchored to world
// space instead of the screen.
public class ViewportPanOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double zoom || values[1] is not double locComponent)
            return 0.0;
        return -locComponent * zoom;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

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