using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeatherWidget.App.Services;

/// <summary>
/// 天气图标映射服务，提供 WMO 天气代码与本地图标文件之间的双向索引。
/// WMO Weather interpretation codes (WW):
/// 0 - 晴朗
/// 1,2,3 - 主要晴朗、局部多云、多云
/// 45,48 - 雾、淞雾
/// 51,53,55 - 毛毛雨（轻、中、大）
/// 56,57 - 冻毛毛雨（轻、大）
/// 61,63,65 - 雨（小、中、大）
/// 66,67 - 冻雨（轻、大）
/// 71,73,75 - 雪（小、中、大）
/// 77 - 米雪
/// 80,81,82 - 阵雨（轻、中、大）
/// 85,86 - 阵雪（轻、大）
/// 95 - 雷暴
/// 96,99 - 雷暴伴冰雹（轻、大）
/// </summary>
public sealed class WeatherIconMapper
{
    private static readonly string IconDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "天气图标");
    private readonly ConcurrentDictionary<int, ImageSource> _iconCache = new();
    private readonly ConcurrentDictionary<int, string> _weatherTextCache = new();

    /// <summary>
    /// 图标文件名与天气代码的映射（一个图标可对应多个代码）
    /// </summary>
    private static readonly Dictionary<string, int[]> IconToWeatherCodes = new()
    {
        ["Sunny"] = [0],                          // 晴朗
        ["PartlySunny"] = [1, 2],                 // 主要晴朗、局部多云
        ["Cloudy"] = [3],                         // 多云
        ["Fog"] = [45, 48],                       // 雾、淞雾
        ["Haze"] = [],                            // 雾霾（API 无直接代码，可作为备用）
        ["Drizzle"] = [51, 53],                   // 毛毛雨（轻、中）
        ["Rain"] = [55, 61, 63, 80, 81],          // 毛毛雨大、小雨、中雨、阵雨
        ["HeavyRain"] = [65, 82],                 // 大雨、强阵雨
        ["Sleet"] = [56, 57, 66, 67],             // 冻毛毛雨、冻雨
        ["Snow"] = [71, 73],                      // 小雪、中雪
        ["ScatteredSnow"] = [75, 77, 85, 86],     // 大雪、米雪、阵雪
        ["ThunderBolt"] = [95, 96, 99],           // 雷暴
        ["Wind"] = [],                            // 有风（API 无直接代码）
        ["ClearNight"] = [],                      // 晴朗夜间（需根据时间判断）
        ["PartlyCloudyNight"] = [],               // 局部多云夜间
        ["NightDrizzle"] = [],                    // 夜间毛毛雨
        ["Sunrise"] = [],                         // 日出（特殊用途）
        ["Sunset"] = [],                          // 日落（特殊用途）
    };

    /// <summary>
    /// 天气代码到图标文件名的映射
    /// </summary>
    private static readonly Dictionary<int, string> WeatherCodeToIcon = new()
    {
        [0] = "Sunny",
        [1] = "PartlySunny",
        [2] = "PartlySunny",
        [3] = "Cloudy",
        [45] = "Fog",
        [48] = "Fog",
        [51] = "Drizzle",
        [53] = "Drizzle",
        [55] = "Rain",
        [56] = "Sleet",
        [57] = "Sleet",
        [61] = "Rain",
        [63] = "Rain",
        [65] = "HeavyRain",
        [66] = "Sleet",
        [67] = "Sleet",
        [71] = "Snow",
        [73] = "Snow",
        [75] = "ScatteredSnow",
        [77] = "ScatteredSnow",
        [80] = "Rain",
        [81] = "Rain",
        [82] = "HeavyRain",
        [85] = "ScatteredSnow",
        [86] = "ScatteredSnow",
        [95] = "ThunderBolt",
        [96] = "ThunderBolt",
        [99] = "ThunderBolt",
    };

    /// <summary>
    /// 夜间版本的图标映射（日间 -> 夜间）
    /// </summary>
    private static readonly Dictionary<string, string> DayToNightIcon = new()
    {
        ["Sunny"] = "ClearNight",
        ["PartlySunny"] = "PartlyCloudyNight",
        ["Drizzle"] = "NightDrizzle",
    };

    /// <summary>
    /// 天气代码到天气描述文字的映射
    /// </summary>
    private static readonly Dictionary<int, string> WeatherCodeToText = new()
    {
        [0] = "晴",
        [1] = "晴间多云",
        [2] = "多云",
        [3] = "阴",
        [45] = "雾",
        [48] = "淞雾",
        [51] = "小毛毛雨",
        [53] = "毛毛雨",
        [55] = "大毛毛雨",
        [56] = "冻毛毛雨",
        [57] = "大冻毛毛雨",
        [61] = "小雨",
        [63] = "中雨",
        [65] = "大雨",
        [66] = "冻雨",
        [67] = "大冻雨",
        [71] = "小雪",
        [73] = "中雪",
        [75] = "大雪",
        [77] = "米雪",
        [80] = "小阵雨",
        [81] = "阵雨",
        [82] = "强阵雨",
        [85] = "小阵雪",
        [86] = "阵雪",
        [95] = "雷暴",
        [96] = "雷暴冰雹",
        [99] = "强雷暴冰雹",
    };

    /// <summary>
    /// 获取天气代码对应的图标
    /// </summary>
    /// <param name="weatherCode">WMO 天气代码</param>
    /// <param name="isNight">是否为夜间（用于切换夜间版本图标）</param>
    /// <param name="size">图标尺寸</param>
    /// <returns>图标图像</returns>
    public ImageSource GetIcon(int weatherCode, bool isNight = false, int size = 64)
    {
        var cacheKey = weatherCode * 10000 + (isNight ? 1 : 0) * 1000 + size;
        return _iconCache.GetOrAdd(cacheKey, _ => LoadIcon(weatherCode, isNight, size));
    }

    /// <summary>
    /// 获取天气代码对应的天气描述文字
    /// </summary>
    /// <param name="weatherCode">WMO 天气代码</param>
    /// <returns>天气描述文字</returns>
    public string GetWeatherText(int weatherCode)
    {
        return WeatherCodeToText.TryGetValue(weatherCode, out var text) ? text : "未知";
    }

    /// <summary>
    /// 获取图标文件名对应的所有天气代码
    /// </summary>
    /// <param name="iconName">图标文件名（不含扩展名）</param>
    /// <returns>天气代码数组</returns>
    public static int[] GetWeatherCodesForIcon(string iconName)
    {
        return IconToWeatherCodes.TryGetValue(iconName, out var codes) ? codes : [];
    }

    /// <summary>
    /// 获取天气代码对应的图标文件名
    /// </summary>
    /// <param name="weatherCode">WMO 天气代码</param>
    /// <param name="isNight">是否为夜间</param>
    /// <returns>图标文件名（不含扩展名）</returns>
    public static string GetIconNameForWeatherCode(int weatherCode, bool isNight = false)
    {
        if (!WeatherCodeToIcon.TryGetValue(weatherCode, out var iconName))
        {
            iconName = "Cloudy"; // 默认图标
        }

        if (isNight && DayToNightIcon.TryGetValue(iconName, out var nightIcon))
        {
            return nightIcon;
        }

        return iconName;
    }

    /// <summary>
    /// 获取所有可用的图标名称
    /// </summary>
    public static IReadOnlyList<string> GetAllIconNames()
    {
        return IconToWeatherCodes.Keys.ToList();
    }

    /// <summary>
    /// 获取所有天气代码及其描述
    /// </summary>
    public static IReadOnlyDictionary<int, string> GetAllWeatherTexts()
    {
        return WeatherCodeToText;
    }

    private ImageSource LoadIcon(int weatherCode, bool isNight, int size)
    {
        var iconName = GetIconNameForWeatherCode(weatherCode, isNight);
        var iconPath = Path.Combine(IconDirectory, $"{iconName}.png");

        if (!File.Exists(iconPath))
        {
            // 如果夜间版本不存在，尝试使用日间版本
            if (isNight)
            {
                var dayIconName = GetIconNameForWeatherCode(weatherCode, false);
                iconPath = Path.Combine(IconDirectory, $"{dayIconName}.png");
            }
        }

        if (!File.Exists(iconPath))
        {
            // 使用默认图标
            iconPath = Path.Combine(IconDirectory, "Cloudy.png");
        }

        if (!File.Exists(iconPath))
        {
            // 返回空白图像作为最终后备
            return CreateFallbackIcon(size);
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(iconPath);
            bmp.DecodePixelWidth = size;
            bmp.DecodePixelHeight = size;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return CreateFallbackIcon(size);
        }
    }

    private static ImageSource CreateFallbackIcon(int size)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF8, 0xFF));
            ctx.DrawRoundedRectangle(background, null, new System.Windows.Rect(0, 0, size, size), size * 0.22, size * 0.22);
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
