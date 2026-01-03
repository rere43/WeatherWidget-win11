using System.Windows;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class ThemeManager
{
    private ThemeMode _currentTheme = ThemeMode.Auto;
    private bool _isDarkVariant;
    private readonly Dictionary<ThemeMode, ResourceDictionary> _themes;

    public ThemeManager()
    {
        _themes = new Dictionary<ThemeMode, ResourceDictionary>
        {
            [ThemeMode.Light] = new() { Source = new Uri("pack://application:,,,/UI/LightTheme.xaml") },
            [ThemeMode.Dark] = new() { Source = new Uri("pack://application:,,,/UI/DarkTheme.xaml") },
            [ThemeMode.Ocean] = new() { Source = new Uri("pack://application:,,,/UI/OceanTheme.xaml") },
            [ThemeMode.Forest] = new() { Source = new Uri("pack://application:,,,/UI/ForestTheme.xaml") },
            [ThemeMode.Sunset] = new() { Source = new Uri("pack://application:,,,/UI/SunsetTheme.xaml") },
            [ThemeMode.Lavender] = new() { Source = new Uri("pack://application:,,,/UI/LavenderTheme.xaml") },
            [ThemeMode.Rose] = new() { Source = new Uri("pack://application:,,,/UI/RoseTheme.xaml") },
            [ThemeMode.Mint] = new() { Source = new Uri("pack://application:,,,/UI/MintTheme.xaml") },
            [ThemeMode.Mocha] = new() { Source = new Uri("pack://application:,,,/UI/MochaTheme.xaml") },
            [ThemeMode.Slate] = new() { Source = new Uri("pack://application:,,,/UI/SlateTheme.xaml") },
            [ThemeMode.Cherry] = new() { Source = new Uri("pack://application:,,,/UI/CherryTheme.xaml") },
            [ThemeMode.Amber] = new() { Source = new Uri("pack://application:,,,/UI/AmberTheme.xaml") },
            [ThemeMode.Obsidian] = new() { Source = new Uri("pack://application:,,,/UI/ObsidianTheme.xaml") },
            [ThemeMode.Charcoal] = new() { Source = new Uri("pack://application:,,,/UI/CharcoalTheme.xaml") },
            [ThemeMode.Midnight] = new() { Source = new Uri("pack://application:,,,/UI/MidnightTheme.xaml") },
            [ThemeMode.Snow] = new() { Source = new Uri("pack://application:,,,/UI/SnowTheme.xaml") },
            [ThemeMode.Ivory] = new() { Source = new Uri("pack://application:,,,/UI/IvoryTheme.xaml") },
            [ThemeMode.Pearl] = new() { Source = new Uri("pack://application:,,,/UI/PearlTheme.xaml") },
        };
    }

    public bool IsDarkTheme => _isDarkVariant;

    public void ApplyTheme(ThemeMode mode, DateTime? sunrise = null, DateTime? sunset = null)
    {
        ThemeMode targetTheme;
        bool isDarkVariant;

        if (mode == ThemeMode.Auto)
        {
            isDarkVariant = ShouldUseDarkTheme(sunrise, sunset);
            targetTheme = isDarkVariant ? ThemeMode.Dark : ThemeMode.Light;
        }
        else
        {
            targetTheme = mode;
            isDarkVariant = mode == ThemeMode.Dark || mode == ThemeMode.Ocean ||
                           mode == ThemeMode.Mocha || mode == ThemeMode.Slate ||
                           mode == ThemeMode.Cherry || mode == ThemeMode.Obsidian ||
                           mode == ThemeMode.Charcoal || mode == ThemeMode.Midnight;
        }

        if (targetTheme == _currentTheme && Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            return;
        }

        _currentTheme = targetTheme;
        _isDarkVariant = isDarkVariant;

        if (!_themes.TryGetValue(targetTheme, out var themeDict))
        {
            themeDict = _themes[ThemeMode.Light];
        }

        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        for (var i = mergedDicts.Count - 1; i >= 0; i--)
        {
            var source = mergedDicts[i].Source?.ToString() ?? string.Empty;
            if (source.Contains("Theme.xaml") && !source.EndsWith("/Theme.xaml"))
            {
                mergedDicts.RemoveAt(i);
            }
        }

        var themeXamlIndex = -1;
        for (var i = 0; i < mergedDicts.Count; i++)
        {
            var source = mergedDicts[i].Source?.ToString() ?? string.Empty;
            if (source.EndsWith("/Theme.xaml"))
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

        if (sunrise.HasValue && sunset.HasValue)
        {
            var sunriseTime = sunrise.Value;
            var sunsetTime = sunset.Value;

            sunriseTime = new DateTime(now.Year, now.Month, now.Day, sunriseTime.Hour, sunriseTime.Minute, 0);
            sunsetTime = new DateTime(now.Year, now.Month, now.Day, sunsetTime.Hour, sunsetTime.Minute, 0);

            return now < sunriseTime || now > sunsetTime;
        }

        var hour = now.Hour;
        return hour < 6 || hour >= 18;
    }
}
