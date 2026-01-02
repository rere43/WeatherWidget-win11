using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class ClothingImageProvider
{
    private readonly ConcurrentDictionary<HumidityLevel, ImageSource?> _cache = new();

    public ImageSource? Get(HumidityLevel level)
    {
        return _cache.GetOrAdd(level, TryLoad);
    }

    private static ImageSource? TryLoad(HumidityLevel level)
    {
        var name = level switch
        {
            HumidityLevel.Dry => "clothing_dry",
            HumidityLevel.Normal => "clothing_normal",
            HumidityLevel.Humid => "clothing_humid",
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "assets", "generated", $"{name}.png");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

