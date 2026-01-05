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
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? Settings.Default;
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

    private static Settings Normalize(Settings settings)
    {
        var def = Settings.Default;

        var city = string.IsNullOrWhiteSpace(settings.City) ? def.City : settings.City.Trim();
        var refresh = settings.RefreshInterval <= TimeSpan.Zero ? def.RefreshInterval : settings.RefreshInterval;

        var tempOffsetX = double.IsFinite(settings.TempBadgeOffsetX) ? settings.TempBadgeOffsetX : def.TempBadgeOffsetX;
        var tempOffsetY = double.IsFinite(settings.TempBadgeOffsetY) ? settings.TempBadgeOffsetY : def.TempBadgeOffsetY;
        var cornerOffsetX = double.IsFinite(settings.CornerBadgeOffsetX) ? settings.CornerBadgeOffsetX : def.CornerBadgeOffsetX;
        var cornerOffsetY = double.IsFinite(settings.CornerBadgeOffsetY) ? settings.CornerBadgeOffsetY : def.CornerBadgeOffsetY;
        var extraOffsetX = double.IsFinite(settings.ExtraBadgeOffsetX) ? settings.ExtraBadgeOffsetX : def.ExtraBadgeOffsetX;
        var extraOffsetY = double.IsFinite(settings.ExtraBadgeOffsetY) ? settings.ExtraBadgeOffsetY : def.ExtraBadgeOffsetY;
        var embeddedIconScale = double.IsFinite(settings.EmbeddedIconScale) && settings.EmbeddedIconScale > 0
            ? Math.Clamp(settings.EmbeddedIconScale, 0.5, 1.6)
            : def.EmbeddedIconScale;
        var embeddedOffsetX = double.IsFinite(settings.EmbeddedOffsetX)
            ? Math.Clamp(settings.EmbeddedOffsetX, -300, 300)
            : def.EmbeddedOffsetX;

        // 兼容历史版本：嵌入模式下 UV 间距曾复用 TempBadgeOffsetY
        var embeddedUvGapRaw = double.IsFinite(settings.EmbeddedUvToWeatherGap)
            ? settings.EmbeddedUvToWeatherGap
            : settings.IconDisplayMode == IconDisplayMode.Embedded
                ? tempOffsetY
                : def.EmbeddedUvToWeatherGap;
        var embeddedUvGap = double.IsFinite(embeddedUvGapRaw)
            ? Math.Clamp(embeddedUvGapRaw, 2, 40)
            : def.EmbeddedUvToWeatherGap;

        var embeddedHoverDelayMs = settings.EmbeddedHoverDelayMs >= 0
            ? Math.Clamp(settings.EmbeddedHoverDelayMs, 0, 5000)
            : def.EmbeddedHoverDelayMs;

        var embeddedHoverPinMs = settings.EmbeddedHoverPinMs >= 0
            ? Math.Clamp(settings.EmbeddedHoverPinMs, 0, 5000)
            : def.EmbeddedHoverPinMs;

        var embeddedTextLayout = Enum.IsDefined(typeof(EmbeddedTextLayout), settings.EmbeddedTextLayout)
            ? settings.EmbeddedTextLayout
            : def.EmbeddedTextLayout;

        var embeddedTextAlignment = Enum.IsDefined(typeof(EmbeddedTextAlignment), settings.EmbeddedTextAlignment)
            ? settings.EmbeddedTextAlignment
            : def.EmbeddedTextAlignment;

        var tempScale = double.IsFinite(settings.TempBadgeFontScale) && settings.TempBadgeFontScale > 0
            ? Math.Clamp(settings.TempBadgeFontScale, 0.5, 3.0)
            : def.TempBadgeFontScale;

        var cornerScale = double.IsFinite(settings.CornerBadgeFontScale) && settings.CornerBadgeFontScale > 0
            ? Math.Clamp(settings.CornerBadgeFontScale, 0.5, 3.0)
            : def.CornerBadgeFontScale;

        var extraScale = double.IsFinite(settings.ExtraBadgeFontScale) && settings.ExtraBadgeFontScale > 0
            ? Math.Clamp(settings.ExtraBadgeFontScale, 0.5, 3.0)
            : def.ExtraBadgeFontScale;

        var tempFormat = string.IsNullOrWhiteSpace(settings.TempBadgeFormat) ? def.TempBadgeFormat : settings.TempBadgeFormat.Trim();
        var uvFormat = string.IsNullOrWhiteSpace(settings.CornerUvFormat) ? def.CornerUvFormat : settings.CornerUvFormat.Trim();
        var rhFormat = string.IsNullOrWhiteSpace(settings.CornerHumidityFormat) ? def.CornerHumidityFormat : settings.CornerHumidityFormat.Trim();
        var extraFormat = string.IsNullOrWhiteSpace(settings.ExtraBadgeFormat) ? def.ExtraBadgeFormat : settings.ExtraBadgeFormat.Trim();

        var lat = double.IsFinite(settings.Latitude) ? settings.Latitude : def.Latitude;
        var lon = double.IsFinite(settings.Longitude) ? settings.Longitude : def.Longitude;

        return settings with
        {
            City = city,
            Latitude = lat,
            Longitude = lon,
            RefreshInterval = refresh,
            TempBadgeOffsetX = tempOffsetX,
            TempBadgeOffsetY = tempOffsetY,
            TempBadgeFontScale = tempScale,
            TempBadgeFormat = tempFormat,
            CornerBadgeOffsetX = cornerOffsetX,
            CornerBadgeOffsetY = cornerOffsetY,
            CornerBadgeFontScale = cornerScale,
            CornerUvFormat = uvFormat,
            CornerHumidityFormat = rhFormat,
            ExtraBadgeEnabled = settings.ExtraBadgeEnabled,
            ExtraBadgeOffsetX = extraOffsetX,
            ExtraBadgeOffsetY = extraOffsetY,
            ExtraBadgeFontScale = extraScale,
            ExtraBadgeFormat = extraFormat,
            ThemeMode = settings.ThemeMode,
            AutoStart = settings.AutoStart,
            StartHidden = settings.StartHidden,
            BadgeBackgroundEnabled = settings.BadgeBackgroundEnabled,
            BadgeStrokeWidth = settings.BadgeStrokeWidth,
            IconBackgroundEnabled = settings.IconBackgroundEnabled,
            IconOffsetX = settings.IconOffsetX,
            IconOffsetY = settings.IconOffsetY,
            TempBadgePosition = settings.TempBadgePosition,
            CornerBadgePosition = settings.CornerBadgePosition,
            ExtraBadgePosition = settings.ExtraBadgePosition,
            BadgeFontFamily = settings.BadgeFontFamily,
            TempBadgeColor = settings.TempBadgeColor,
            CornerBadgeColor = settings.CornerBadgeColor,
            ExtraBadgeColor = settings.ExtraBadgeColor,
            EmbeddedIconScale = embeddedIconScale,
            EmbeddedOffsetX = embeddedOffsetX,
            EmbeddedUvToWeatherGap = embeddedUvGap,
            EmbeddedHoverDelayMs = embeddedHoverDelayMs,
            EmbeddedHoverPinMs = embeddedHoverPinMs,
            EmbeddedTextLayout = embeddedTextLayout,
            EmbeddedTextAlignment = embeddedTextAlignment,
        };
    }
}
