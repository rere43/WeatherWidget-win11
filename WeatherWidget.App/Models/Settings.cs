namespace WeatherWidget.App.Models;

/// <summary>
/// 自定义地点（用户保存的经纬度位置）
/// </summary>
public sealed record CustomLocation
{
    public string Name { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
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

/// <summary>
/// 任务栏嵌入显示相关配置（UV条数字 + 天气图标 + 气温 + 湿度）。
/// </summary>
public sealed record EmbeddedWidgetSettings
{
    // 布局：温湿度两行之间的间距（DIP）
    public double LineSpacing { get; init; } = 0;

    // 布局：UV条与天气图标间距（DIP）
    public double UvToIconGap { get; init; } = 4;

    // 布局：天气图标与文字区域间距（DIP）
    public double IconToTextGap { get; init; } = 6;

    // 布局：整体水平偏移（像素，正右负左；由不同实现自行解释）
    public double OffsetX { get; init; } = 0;

    // 天气图标缩放
    public double IconScale { get; init; } = 1.0;

    // 天气图标圆角背景（避免透明图在深色任务栏上观感异常）
    public bool WeatherIconBackgroundEnabled { get; init; } = true;

    // 天气图标偏移（以 64px 图标为基准缩放）
    public double WeatherIconOffsetX { get; init; } = 0;
    public double WeatherIconOffsetY { get; init; } = 0;

    // 悬停触发面板延迟（ms）
    public int HoverDelayMs { get; init; } = 500;

    // 悬停打开面板后，达到该时长则视为“固定”（移出触发区不再自动隐藏）（ms）
    public int HoverPinMs { get; init; } = 500;

    // 字体与字号
    public string FontFamily { get; init; } = "Segoe UI";
    public double TemperatureFontScale { get; init; } = 1.0;
    public double HumidityFontScale { get; init; } = 1.0;
    public double UvNumberFontScale { get; init; } = 2.0;
    public double TextStrokeWidth { get; init; } = 2.0;

    // 文本格式
    public string TemperatureFormat { get; init; } = "{value}°";
    public string HumidityFormat { get; init; } = "{value}%";
    public string UvNumberFormat { get; init; } = "{value}";

    // 颜色（ARGB/#RRGGBB 均可）
    public string TemperatureColor { get; init; } = "#FFFFFFFF";
    public string HumidityColor { get; init; } = "#FFFFFFFF";
    public string UvNumberColor { get; init; } = "#FFFFFFFF";
    public string UvBarFillColor { get; init; } = "#FFDA70D6"; // 粉紫色
    public string UvBarBackgroundColor { get; init; } = "#80808080";
}

/// <summary>
/// 应用配置（仅保留“嵌入任务栏”目标所需项）。
/// </summary>
public sealed record Settings
{
    // 版本号：用于 settings.json 结构迁移
    public int SchemaVersion { get; init; } = 2;

    // 城市/定位
    public string City { get; init; } = "Shanghai";
    public double Latitude { get; init; } = 31.2304;
    public double Longitude { get; init; } = 121.4737;
    public bool UseCustomCoordinates { get; init; } = false;

    // 自定义地点列表
    public IReadOnlyList<CustomLocation> CustomLocations { get; init; } = Array.Empty<CustomLocation>();

    // 刷新间隔
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(10);

    // 面板主题
    public ThemeMode ThemeMode { get; init; } = ThemeMode.Auto;

    // 启动行为
    public bool AutoStart { get; init; } = false;
    public bool StartHidden { get; init; } = false;

    // 嵌入任务栏显示
    public EmbeddedWidgetSettings Embedded { get; init; } = new();

    public static Settings Default => new();
}
