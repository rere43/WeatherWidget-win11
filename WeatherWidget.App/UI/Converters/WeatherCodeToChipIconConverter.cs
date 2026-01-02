using System.Globalization;
using System.Windows.Data;
using WeatherWidget.App.Services;

namespace WeatherWidget.App.UI.Converters;

public sealed class WeatherCodeToChipIconConverter : IValueConverter
{
    private readonly WeatherIconMapper _iconMapper = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int code)
        {
            return null;
        }

        // 根据当前时间判断是否为夜间（6:00-18:00 为日间）
        var hour = DateTime.Now.Hour;
        var isNight = hour < 6 || hour >= 18;

        return _iconMapper.GetIcon(code, isNight, 64);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

