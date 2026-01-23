using System.Globalization;
using System.Windows.Data;

namespace WeatherWidget.App.UI.Converters;

public sealed class ScaleCoordinateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double v || values[1] is not double actualSize)
        {
            return 0d;
        }

        if (!double.IsFinite(v) || !double.IsFinite(actualSize) || actualSize <= 0)
        {
            return 0d;
        }

        var baseSize = 1d;
        var offset = 0d;
        if (parameter is string s && !string.IsNullOrWhiteSpace(s))
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBase))
            {
                baseSize = parsedBase;
            }

            if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOffset))
            {
                offset = parsedOffset;
            }
        }

        if (!double.IsFinite(baseSize) || baseSize == 0)
        {
            return 0d;
        }

        var scaled = (v + offset) * actualSize / baseSize;
        return double.IsFinite(scaled) ? scaled : 0d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

