using System.Windows;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class ThemeManager
{
    private bool _isDarkTheme;
    private readonly ResourceDictionary _lightTheme;
    private readonly ResourceDictionary _darkTheme;

    public ThemeManager()
    {
        _lightTheme = new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/LightTheme.xaml") };
        _darkTheme = new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/DarkTheme.xaml") };
    }

    public bool IsDarkTheme => _isDarkTheme;

    public void ApplyTheme(ThemeMode mode, DateTime? sunrise = null, DateTime? sunset = null)
    {
        var useDark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            ThemeMode.Auto => ShouldUseDarkTheme(sunrise, sunset),
            _ => false
        };

        if (useDark == _isDarkTheme && Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            return; // 主题没有变化
        }

        _isDarkTheme = useDark;
        var themeDict = useDark ? _darkTheme : _lightTheme;

        // 替换主题资源
        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        // 移除旧的主题资源（LightTheme或DarkTheme）
        for (var i = mergedDicts.Count - 1; i >= 0; i--)
        {
            var source = mergedDicts[i].Source?.ToString() ?? string.Empty;
            if (source.Contains("LightTheme.xaml") || source.Contains("DarkTheme.xaml"))
            {
                mergedDicts.RemoveAt(i);
            }
        }

        // 添加新主题（在Theme.xaml之前，这样Theme.xaml中的样式可以使用主题颜色）
        var themeXamlIndex = -1;
        for (var i = 0; i < mergedDicts.Count; i++)
        {
            var source = mergedDicts[i].Source?.ToString() ?? string.Empty;
            if (source.Contains("Theme.xaml") && !source.Contains("LightTheme") && !source.Contains("DarkTheme"))
            {
                themeXamlIndex = i;
                break;
            }
        }

        if (themeXamlIndex >= 0)
        {
            mergedDicts.Insert(themeXamlIndex, themeDict);
        }
        else
        {
            mergedDicts.Insert(0, themeDict);
        }
    }

    private static bool ShouldUseDarkTheme(DateTime? sunrise, DateTime? sunset)
    {
        var now = DateTime.Now;

        // 如果有日出日落数据，使用它们
        if (sunrise.HasValue && sunset.HasValue)
        {
            var sunriseTime = sunrise.Value;
            var sunsetTime = sunset.Value;

            // 确保是今天的时间
            sunriseTime = new DateTime(now.Year, now.Month, now.Day, sunriseTime.Hour, sunriseTime.Minute, 0);
            sunsetTime = new DateTime(now.Year, now.Month, now.Day, sunsetTime.Hour, sunsetTime.Minute, 0);

            // 日出前或日落后使用深色主题
            return now < sunriseTime || now > sunsetTime;
        }

        // 没有日出日落数据时，使用固定时间（6:00-18:00为日间）
        var hour = now.Hour;
        return hour < 6 || hour >= 18;
    }
}
