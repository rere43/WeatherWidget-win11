namespace WeatherWidget.App.Models;

public enum IconCornerMetric
{
    Off = 0,
    UvIndex = 1,
    Humidity = 2,
}

public enum BadgePosition
{
    TopLeft = 0,
    Top = 1,
    TopRight = 2,
    Left = 3,
    Right = 4,
    BottomLeft = 5,
    Bottom = 6,
    BottomRight = 7,
}

public enum ThemeMode
{
    Auto = 0,       // 根据日出日落自动切换
    Light = 1,      // 始终日间主题
    Dark = 2,       // 始终夜间主题
    Ocean = 3,      // 海洋蓝调
    Forest = 4,     // 森林绿意
    Sunset = 5,     // 日落橙红
    Lavender = 6,   // 薰衣草紫
    Rose = 7,       // 玫瑰粉调
    Mint = 8,       // 薄荷清凉
    Mocha = 9,      // 摩卡咖啡
    Slate = 10,     // 石板灰调
    Cherry = 11,    // 樱桃红韵
    Amber = 12,     // 琥珀金黄
    Obsidian = 13,  // 黑曜石（纯黑）
    Charcoal = 14,  // 炭黑
    Midnight = 15,  // 午夜蓝黑
    Snow = 16,      // 雪白（纯白）
    Ivory = 17,     // 象牙白
    Pearl = 18,     // 珍珠灰白
}

public enum IconDisplayMode
{
    Combined = 0,   // 单图标：天气图标+温度角标（默认）
    Separate = 1,   // 双图标：一个显示天气图标，一个显示温度数字
}

public sealed record Settings(
    string City,
    double Latitude,
    double Longitude,
    IconCornerMetric IconCornerMetric,
    TimeSpan RefreshInterval,
    double TempBadgeOffsetX = 0,
    double TempBadgeOffsetY = 0,
    double TempBadgeFontScale = 1.0,
    string TempBadgeFormat = "{value}°",
    BadgePosition TempBadgePosition = BadgePosition.TopRight,
    string TempBadgeColor = "#FFFFFFFF",
    double CornerBadgeOffsetX = 0,
    double CornerBadgeOffsetY = 0,
    double CornerBadgeFontScale = 1.0,
    string CornerUvFormat = "UV{value}",
    string CornerHumidityFormat = "{value}%",
    BadgePosition CornerBadgePosition = BadgePosition.BottomRight,
    string CornerBadgeColor = "#FFFFFFFF",
    bool ExtraBadgeEnabled = false,
    double ExtraBadgeOffsetX = 0,
    double ExtraBadgeOffsetY = 0,
    double ExtraBadgeFontScale = 1.0,
    string ExtraBadgeFormat = "",
    BadgePosition ExtraBadgePosition = BadgePosition.BottomLeft,
    string ExtraBadgeColor = "#FFFFFFFF",
    bool BadgeBackgroundEnabled = true,
    double BadgeStrokeWidth = 2.0,
    bool IconBackgroundEnabled = true,
    double IconOffsetX = 0,
    double IconOffsetY = 0,
    string BadgeFontFamily = "Segoe UI",
    ThemeMode ThemeMode = ThemeMode.Auto,
    IconDisplayMode IconDisplayMode = IconDisplayMode.Combined,
    bool AutoStart = false,
    bool StartHidden = false)
{
    public static Settings Default =>
        new(
            City: "Shanghai",
            Latitude: 31.2304,
            Longitude: 121.4737,
            IconCornerMetric: IconCornerMetric.UvIndex,
            RefreshInterval: TimeSpan.FromMinutes(10));
}
