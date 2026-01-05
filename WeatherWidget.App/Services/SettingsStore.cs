using System.IO;
using System.Text.Json;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private const int CurrentSchemaVersion = 2;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public Settings LoadOrCreateDefault()
    {
        if (!File.Exists(_path))
        {
            Save(Settings.Default);
            return Settings.Default;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = LoadFromJson(json);
            var normalized = Normalize(settings);
            if (normalized != settings)
            {
                Save(normalized);
            }

            return normalized;
        }
        catch
        {
            Save(Settings.Default);
            return Settings.Default;
        }
    }

    public void Save(Settings settings)
    {
        settings = Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static Settings LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Settings.Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(nameof(Settings.SchemaVersion), out var schemaProp) &&
                schemaProp.ValueKind == JsonValueKind.Number &&
                schemaProp.TryGetInt32(out var schemaVersion) &&
                schemaVersion >= CurrentSchemaVersion)
            {
                return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? Settings.Default;
            }
        }
        catch
        {
            // ignore: fallthrough to legacy parse
        }

        var legacy = JsonSerializer.Deserialize<LegacySettingsV1>(json, JsonOptions);
        return legacy is null ? Settings.Default : ConvertLegacy(legacy);
    }

    private static Settings Normalize(Settings settings)
    {
        var def = Settings.Default;

        var city = string.IsNullOrWhiteSpace(settings.City) ? def.City : settings.City.Trim();
        var refresh = settings.RefreshInterval <= TimeSpan.Zero ? def.RefreshInterval : settings.RefreshInterval;

        var lat = double.IsFinite(settings.Latitude) ? Math.Clamp(settings.Latitude, -90, 90) : def.Latitude;
        var lon = double.IsFinite(settings.Longitude) ? Math.Clamp(settings.Longitude, -180, 180) : def.Longitude;

        var themeMode = Enum.IsDefined(typeof(ThemeMode), settings.ThemeMode) ? settings.ThemeMode : def.ThemeMode;

        var embedded = settings.Embedded ?? def.Embedded;
        var embeddedDef = def.Embedded;

        var lineSpacing = double.IsFinite(embedded.LineSpacing) ? Math.Clamp(embedded.LineSpacing, 0, 40) : embeddedDef.LineSpacing;
        var uvToIconGap = double.IsFinite(embedded.UvToIconGap) ? Math.Clamp(embedded.UvToIconGap, 2, 40) : embeddedDef.UvToIconGap;
        var iconToTextGap = double.IsFinite(embedded.IconToTextGap) ? Math.Clamp(embedded.IconToTextGap, 2, 40) : embeddedDef.IconToTextGap;
        var offsetX = double.IsFinite(embedded.OffsetX) ? Math.Clamp(embedded.OffsetX, -300, 300) : embeddedDef.OffsetX;

        var iconScale = double.IsFinite(embedded.IconScale) && embedded.IconScale > 0
            ? Math.Clamp(embedded.IconScale, 0.5, 1.6)
            : embeddedDef.IconScale;

        var hoverDelayMs = embedded.HoverDelayMs >= 0 ? Math.Clamp(embedded.HoverDelayMs, 0, 5000) : embeddedDef.HoverDelayMs;
        var hoverPinMs = embedded.HoverPinMs >= 0 ? Math.Clamp(embedded.HoverPinMs, 0, 5000) : embeddedDef.HoverPinMs;

        var fontFamily = string.IsNullOrWhiteSpace(embedded.FontFamily) ? embeddedDef.FontFamily : embedded.FontFamily.Trim();

        var tempScale = double.IsFinite(embedded.TemperatureFontScale) && embedded.TemperatureFontScale > 0
            ? Math.Clamp(embedded.TemperatureFontScale, 0.5, 3.0)
            : embeddedDef.TemperatureFontScale;
        var humidityScale = double.IsFinite(embedded.HumidityFontScale) && embedded.HumidityFontScale > 0
            ? Math.Clamp(embedded.HumidityFontScale, 0.5, 3.0)
            : embeddedDef.HumidityFontScale;
        var uvNumberScale = double.IsFinite(embedded.UvNumberFontScale) && embedded.UvNumberFontScale > 0
            ? Math.Clamp(embedded.UvNumberFontScale, 0.5, 6.0)
            : embeddedDef.UvNumberFontScale;

        var strokeWidth = double.IsFinite(embedded.TextStrokeWidth) && embedded.TextStrokeWidth >= 0
            ? Math.Clamp(embedded.TextStrokeWidth, 0, 8.0)
            : embeddedDef.TextStrokeWidth;

        var tempFormat = string.IsNullOrWhiteSpace(embedded.TemperatureFormat) ? embeddedDef.TemperatureFormat : embedded.TemperatureFormat.Trim();
        var rhFormat = string.IsNullOrWhiteSpace(embedded.HumidityFormat) ? embeddedDef.HumidityFormat : embedded.HumidityFormat.Trim();
        var uvFormat = string.IsNullOrWhiteSpace(embedded.UvNumberFormat) ? embeddedDef.UvNumberFormat : embedded.UvNumberFormat.Trim();

        var tempColor = string.IsNullOrWhiteSpace(embedded.TemperatureColor) ? embeddedDef.TemperatureColor : embedded.TemperatureColor.Trim();
        var rhColor = string.IsNullOrWhiteSpace(embedded.HumidityColor) ? embeddedDef.HumidityColor : embedded.HumidityColor.Trim();
        var uvTextColor = string.IsNullOrWhiteSpace(embedded.UvNumberColor) ? embeddedDef.UvNumberColor : embedded.UvNumberColor.Trim();
        var uvFillColor = string.IsNullOrWhiteSpace(embedded.UvBarFillColor) ? embeddedDef.UvBarFillColor : embedded.UvBarFillColor.Trim();
        var uvBgColor = string.IsNullOrWhiteSpace(embedded.UvBarBackgroundColor) ? embeddedDef.UvBarBackgroundColor : embedded.UvBarBackgroundColor.Trim();

        var iconBgEnabled = embedded.WeatherIconBackgroundEnabled;
        var iconOffsetX = double.IsFinite(embedded.WeatherIconOffsetX) ? embedded.WeatherIconOffsetX : embeddedDef.WeatherIconOffsetX;
        var iconOffsetY = double.IsFinite(embedded.WeatherIconOffsetY) ? embedded.WeatherIconOffsetY : embeddedDef.WeatherIconOffsetY;

        var normalizedEmbedded = embedded with
        {
            LineSpacing = lineSpacing,
            UvToIconGap = uvToIconGap,
            IconToTextGap = iconToTextGap,
            OffsetX = offsetX,
            IconScale = iconScale,
            HoverDelayMs = hoverDelayMs,
            HoverPinMs = hoverPinMs,
            FontFamily = fontFamily,
            TemperatureFontScale = tempScale,
            HumidityFontScale = humidityScale,
            UvNumberFontScale = uvNumberScale,
            TextStrokeWidth = strokeWidth,
            TemperatureFormat = tempFormat,
            HumidityFormat = rhFormat,
            UvNumberFormat = uvFormat,
            TemperatureColor = tempColor,
            HumidityColor = rhColor,
            UvNumberColor = uvTextColor,
            UvBarFillColor = uvFillColor,
            UvBarBackgroundColor = uvBgColor,
            WeatherIconBackgroundEnabled = iconBgEnabled,
            WeatherIconOffsetX = iconOffsetX,
            WeatherIconOffsetY = iconOffsetY,
        };

        return settings with
        {
            SchemaVersion = CurrentSchemaVersion,
            City = city,
            Latitude = lat,
            Longitude = lon,
            RefreshInterval = refresh,
            ThemeMode = themeMode,
            AutoStart = settings.AutoStart,
            StartHidden = settings.StartHidden,
            Embedded = normalizedEmbedded,
        };
    }

    private sealed record LegacySettingsV1
    {
        public string City { get; init; } = Settings.Default.City;
        public double Latitude { get; init; } = Settings.Default.Latitude;
        public double Longitude { get; init; } = Settings.Default.Longitude;
        public TimeSpan RefreshInterval { get; init; } = Settings.Default.RefreshInterval;
        public ThemeMode ThemeMode { get; init; } = Settings.Default.ThemeMode;

        public bool AutoStart { get; init; } = false;
        public bool StartHidden { get; init; } = false;

        // 旧版：嵌入布局曾复用角标配置项
        public double TempBadgeOffsetX { get; init; } = 0;      // 行间距
        public double TempBadgeOffsetY { get; init; } = 4;      // UV条与图标间距（历史复用）
        public double TempBadgeFontScale { get; init; } = 1.0;  // 温度字号
        public string TempBadgeFormat { get; init; } = "{value}°";
        public string TempBadgeColor { get; init; } = "#FFFFFFFF";

        public double CornerBadgeOffsetX { get; init; } = 6;    // 图标与文字间距
        public double CornerBadgeOffsetY { get; init; } = 2.0;  // UV数字字号缩放
        public double CornerBadgeFontScale { get; init; } = 1.0;// 湿度字号
        public string CornerHumidityFormat { get; init; } = "{value}%";
        public string CornerBadgeColor { get; init; } = "#FFFFFFFF";

        public string BadgeFontFamily { get; init; } = "Segoe UI";
        public double BadgeStrokeWidth { get; init; } = 2.0;

        public bool IconBackgroundEnabled { get; init; } = true;
        public double IconOffsetX { get; init; } = 0;
        public double IconOffsetY { get; init; } = 0;

        public double EmbeddedIconScale { get; init; } = 1.0;
        public double EmbeddedOffsetX { get; init; } = 0;
        public double EmbeddedUvToWeatherGap { get; init; } = double.NaN;
        public int EmbeddedHoverDelayMs { get; init; } = 500;
        public int EmbeddedHoverPinMs { get; init; } = 500;
    }

    private static Settings ConvertLegacy(LegacySettingsV1 legacy)
    {
        var def = Settings.Default;
        var embeddedDef = def.Embedded;

        var uvGap = double.IsFinite(legacy.EmbeddedUvToWeatherGap)
            ? legacy.EmbeddedUvToWeatherGap
            : legacy.TempBadgeOffsetY;

        return def with
        {
            SchemaVersion = CurrentSchemaVersion,
            City = legacy.City,
            Latitude = legacy.Latitude,
            Longitude = legacy.Longitude,
            RefreshInterval = legacy.RefreshInterval,
            ThemeMode = legacy.ThemeMode,
            AutoStart = legacy.AutoStart,
            StartHidden = legacy.StartHidden,
            Embedded = embeddedDef with
            {
                LineSpacing = legacy.TempBadgeOffsetX,
                UvToIconGap = uvGap,
                IconToTextGap = legacy.CornerBadgeOffsetX,
                OffsetX = legacy.EmbeddedOffsetX,
                IconScale = legacy.EmbeddedIconScale,
                HoverDelayMs = legacy.EmbeddedHoverDelayMs,
                HoverPinMs = legacy.EmbeddedHoverPinMs,
                FontFamily = legacy.BadgeFontFamily,
                TemperatureFontScale = legacy.TempBadgeFontScale,
                HumidityFontScale = legacy.CornerBadgeFontScale,
                UvNumberFontScale = legacy.CornerBadgeOffsetY,
                TextStrokeWidth = legacy.BadgeStrokeWidth,
                TemperatureFormat = legacy.TempBadgeFormat,
                HumidityFormat = legacy.CornerHumidityFormat,
                TemperatureColor = legacy.TempBadgeColor,
                HumidityColor = legacy.CornerBadgeColor,
                WeatherIconBackgroundEnabled = legacy.IconBackgroundEnabled,
                WeatherIconOffsetX = legacy.IconOffsetX,
                WeatherIconOffsetY = legacy.IconOffsetY,
            }
        };
    }
}
