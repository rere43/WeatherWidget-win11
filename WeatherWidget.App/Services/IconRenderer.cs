using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class IconRenderer
{
    private readonly WeatherArtProvider _artProvider;

    public IconRenderer(WeatherArtProvider artProvider)
    {
        _artProvider = artProvider;
    }

    public ImageSource RenderTaskbarIcon(WeatherNow now, Settings settings, int size = 64)
    {
        size = Math.Clamp(size, 16, 512);

        var baseArt = _artProvider.RenderBaseArt(now.WeatherCode, size);

        // 解析字体
        var fontFamily = GetFontFamily(settings.BadgeFontFamily);

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            if (settings.IconBackgroundEnabled)
            {
                // 加一个浅色底，避免透明 PNG 在深色任务栏上"看起来像黑块"
                var bgFill = new SolidColorBrush(Color.FromRgb(0xF3, 0xF8, 0xFF));
                var bgStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xE6, 0xF7)), Math.Max(1, size * 0.03));
                ctx.DrawRoundedRectangle(bgFill, bgStroke, new Rect(0, 0, size, size), size * 0.22, size * 0.22);
            }

            // 应用图标偏移（以64px为基准缩放）
            var offsetScale = size / 64.0;
            var iconDx = settings.IconOffsetX * offsetScale;
            var iconDy = settings.IconOffsetY * offsetScale;
            ctx.DrawImage(baseArt, new Rect(iconDx, iconDy, size, size));

            var tempValue = $"{Math.Round(now.TemperatureC):0}";
            var tempText = ApplyTemplate(settings.TempBadgeFormat, tempValue, "{value}°");
            DrawCornerLabel(
                ctx,
                tempText,
                position: settings.TempBadgePosition,
                size: size,
                offsetX: settings.TempBadgeOffsetX,
                offsetY: settings.TempBadgeOffsetY,
                fontScale: settings.TempBadgeFontScale,
                showBackground: settings.BadgeBackgroundEnabled,
                strokeWidth: settings.BadgeStrokeWidth,
                fontFamily: fontFamily,
                fontColor: ParseColor(settings.TempBadgeColor));

            var cornerText = settings.IconCornerMetric switch
            {
                // 角标即使当前无数据，也显示占位符，避免"没角标了"的观感
                IconCornerMetric.UvIndex => ApplyTemplate(
                    settings.CornerUvFormat,
                    now.UvIndex is null ? "—" : $"{Math.Round(now.UvIndex.Value):0}",
                    "UV{value}"),
                IconCornerMetric.Humidity => ApplyTemplate(
                    settings.CornerHumidityFormat,
                    now.RelativeHumidityPercent is null ? "—" : $"{now.RelativeHumidityPercent.Value}",
                    "{value}%"),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(cornerText))
            {
                DrawCornerLabel(
                    ctx,
                    cornerText,
                    position: settings.CornerBadgePosition,
                    size: size,
                    small: true,
                    offsetX: settings.CornerBadgeOffsetX,
                    offsetY: settings.CornerBadgeOffsetY,
                    fontScale: settings.CornerBadgeFontScale,
                    showBackground: settings.BadgeBackgroundEnabled,
                    strokeWidth: settings.BadgeStrokeWidth,
                    fontFamily: fontFamily,
                    fontColor: ParseColor(settings.CornerBadgeColor));
            }

            if (settings.ExtraBadgeEnabled && !string.IsNullOrWhiteSpace(settings.ExtraBadgeFormat))
            {
                var extraText = ApplyMultiTemplate(settings.ExtraBadgeFormat, now);
                if (!string.IsNullOrWhiteSpace(extraText))
                {
                    DrawCornerLabel(
                        ctx,
                        extraText,
                        position: settings.ExtraBadgePosition,
                        size: size,
                        small: true,
                        offsetX: settings.ExtraBadgeOffsetX,
                        offsetY: settings.ExtraBadgeOffsetY,
                        fontScale: settings.ExtraBadgeFontScale,
                        showBackground: settings.BadgeBackgroundEnabled,
                        strokeWidth: settings.BadgeStrokeWidth,
                        fontFamily: fontFamily,
                        fontColor: ParseColor(settings.ExtraBadgeColor));
                }
            }
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public ImageSource RenderPreviewBitmap(WeatherSnapshot snapshot, Settings settings, bool isDarkTheme)
    {
        const int width = 320;
        const int height = 240;
        var visual = new DrawingVisual();
        var now = snapshot.Now;

        using (var ctx = visual.RenderOpen())
        {
            // Colors
            var bgColor = isDarkTheme ? Color.FromRgb(30, 40, 54) : Color.FromRgb(249, 251, 255);
            var borderColor = isDarkTheme ? Color.FromRgb(45, 58, 77) : Color.FromRgb(227, 234, 246);
            var textColor = isDarkTheme ? Color.FromRgb(232, 237, 245) : Color.FromRgb(34, 48, 65);
            var subTextColor = isDarkTheme ? Color.FromRgb(158, 170, 184) : Color.FromRgb(90, 107, 125);

            ctx.DrawRectangle(new SolidColorBrush(bgColor), new Pen(new SolidColorBrush(borderColor), 1), new Rect(0, 0, width, height));

            var fontFamily = GetFontFamily(settings.BadgeFontFamily);
            var boldTypeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var normalTypeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // 1. Header: City
            var cityText = new FormattedText(
                snapshot.LocationName,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                boldTypeface,
                15,
                new SolidColorBrush(textColor),
                1.25);
            ctx.DrawText(cityText, new Point(12, 10));

            // 2. Current Weather
            var iconSize = 48;
            var baseArt = _artProvider.RenderBaseArt(now.WeatherCode, iconSize);
            ctx.DrawImage(baseArt, new Rect(12, 38, iconSize, iconSize));

            var tempText = new FormattedText(
                $"{Math.Round(now.TemperatureC):0}°",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                boldTypeface,
                32,
                new SolidColorBrush(textColor),
                1.25);
            ctx.DrawText(tempText, new Point(68, 36));

            var detailString = $"湿度 {now.RelativeHumidityPercent}%  UV {now.UvIndex:0.0}";
            var detailText = new FormattedText(
                detailString,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                normalTypeface,
                11,
                new SolidColorBrush(subTextColor),
                1.25);
            ctx.DrawText(detailText, new Point(140, 42));

            // Divider
            ctx.DrawLine(new Pen(new SolidColorBrush(borderColor), 1), new Point(12, 94), new Point(width - 12, 94));

            // 3. Hourly Forecast (Next 5 hours)
            var currentHour = DateTimeOffset.Now.ToUnixTimeSeconds() / 3600;
            var hourCount = 0;
            var startX = 12.0;
            var colWidth = (width - 24) / 5.0;

            if (snapshot.Hours != null)
            {
                foreach (var h in snapshot.Hours)
                {
                    if (h.Time.ToUnixTimeSeconds() / 3600 <= currentHour) continue;

                    var hX = startX + hourCount * colWidth;
                    var timeStr = h.Time.ToLocalTime().ToString("HH:mm");

                    var timeText = new FormattedText(
                        timeStr,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        normalTypeface,
                        10,
                        new SolidColorBrush(subTextColor),
                        1.25);
                    ctx.DrawText(timeText, new Point(hX + (colWidth - timeText.Width) / 2, 100));

                    var hIcon = _artProvider.RenderBaseArt(h.WeatherCode ?? 0, 24);
                    ctx.DrawImage(hIcon, new Rect(hX + (colWidth - 24) / 2, 116, 24, 24));

                    var hTemp = $"{Math.Round(h.TemperatureC ?? 0):0}°";
                    var hTempText = new FormattedText(
                        hTemp,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        boldTypeface,
                        11,
                        new SolidColorBrush(textColor),
                        1.25);
                    ctx.DrawText(hTempText, new Point(hX + (colWidth - hTempText.Width) / 2, 142));

                    hourCount++;
                    if (hourCount >= 5) break;
                }
            }

            // Divider
            ctx.DrawLine(new Pen(new SolidColorBrush(borderColor), 1), new Point(12, 164), new Point(width - 12, 164));

            // 4. Daily Forecast (5 days)
            var dayCount = 0;
            if (snapshot.Days != null)
            {
                foreach (var d in snapshot.Days)
                {
                    var dX = startX + dayCount * colWidth;
                    var dateStr = d.Date.ToString("M/d");
                    if (d.Date == DateOnly.FromDateTime(DateTime.Now)) dateStr = "今天";

                    var dateText = new FormattedText(
                        dateStr,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        normalTypeface,
                        10,
                        new SolidColorBrush(subTextColor),
                        1.25);
                    ctx.DrawText(dateText, new Point(dX + (colWidth - dateText.Width) / 2, 170));

                    var dIcon = _artProvider.RenderBaseArt(d.WeatherCode, 24);
                    ctx.DrawImage(dIcon, new Rect(dX + (colWidth - 24) / 2, 186, 24, 24));

                    var range = $"{Math.Round(d.TemperatureMaxC):0}°/{Math.Round(d.TemperatureMinC):0}°";
                    var rangeText = new FormattedText(
                        range,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        normalTypeface,
                        10,
                        new SolidColorBrush(textColor),
                        1.25);
                    ctx.DrawText(rangeText, new Point(dX + (colWidth - rangeText.Width) / 2, 212));

                    dayCount++;
                    if (dayCount >= 5) break;
                }
            }
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public static bool IsAllTransparent(BitmapSource source)
    {
        // RenderTargetBitmap 可能在启动初期渲染出“全透明”（任务栏表现为黑/空）。
        // 这里做一个快速检测，避免把坏图标设置到任务栏。
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return true;
        }

        var stride = source.PixelWidth * 4;
        var pixels = new byte[source.PixelHeight * stride];
        source.CopyPixels(pixels, stride, 0);

        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void DrawCornerLabel(
        DrawingContext ctx,
        string text,
        BadgePosition position,
        int size,
        bool small = false,
        double offsetX = 0,
        double offsetY = 0,
        double fontScale = 1.0,
        bool showBackground = true,
        double strokeWidth = 2.0,
        FontFamily? fontFamily = null,
        Color? fontColor = null)
    {
        // Win11 任务栏实际显示更小（尤其 32px），需要更大的字号才能可读
        var scale = size <= 32 ? 1.28 : 1.0;
        fontScale = double.IsFinite(fontScale) && fontScale > 0 ? fontScale : 1.0;
        // 统一基础字号：所有角标使用相同的底层字号
        var baseFontSize = size * 0.26;
        var fontSize = baseFontSize * scale * fontScale;
        fontFamily ??= new FontFamily("Segoe UI");
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var textBrush = new SolidColorBrush(fontColor ?? Colors.White);

        var padding = size * 0.06;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            textBrush,
            1.0);

        var bubbleWidth = formatted.Width + padding * 1.7;
        var bubbleHeight = formatted.Height + padding * 1.0;

        // 文本过长时自动缩小，避免被裁切成"看不到角标"
        var maxBubbleWidth = size - padding * 2;
        if (maxBubbleWidth > 0 && bubbleWidth > maxBubbleWidth)
        {
            var ratio = maxBubbleWidth / bubbleWidth;
            var adjustedFontSize = Math.Max(1.0, fontSize * ratio);
            formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                adjustedFontSize,
                textBrush,
                1.0);
            bubbleWidth = formatted.Width + padding * 1.7;
            bubbleHeight = formatted.Height + padding * 1.0;
        }

        // 偏移单位以 64px 图标为基准
        var offsetScale = size / 64.0;
        var dx = offsetX * offsetScale;
        var dy = offsetY * offsetScale;

        // 计算8个位置的基准坐标
        var centerX = (size - bubbleWidth) / 2;
        var centerY = (size - bubbleHeight) / 2;
        var leftX = padding;
        var rightX = size - bubbleWidth - padding;
        var topY = padding;
        var bottomY = size - bubbleHeight - padding;

        var rect = position switch
        {
            BadgePosition.TopLeft => new Rect(leftX + dx, topY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.Top => new Rect(centerX + dx, topY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.TopRight => new Rect(rightX + dx, topY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.Left => new Rect(leftX + dx, centerY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.Right => new Rect(rightX + dx, centerY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.BottomLeft => new Rect(leftX + dx, bottomY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.Bottom => new Rect(centerX + dx, bottomY + dy, bubbleWidth, bubbleHeight),
            BadgePosition.BottomRight => new Rect(rightX + dx, bottomY + dy, bubbleWidth, bubbleHeight),
            _ => new Rect(padding + dx, padding + dy, bubbleWidth, bubbleHeight),
        };

        if (showBackground)
        {
            var bubbleFill = new SolidColorBrush(Color.FromArgb(0xDD, 0x25, 0x2F, 0x3A));
            var bubbleStroke = new Pen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)), Math.Max(1, size * 0.045));
            ctx.DrawRoundedRectangle(bubbleFill, bubbleStroke, rect, rect.Height / 2, rect.Height / 2);
            formatted.SetForegroundBrush(textBrush);
        }
        else
        {
            // 无背景时使用描边文字
            strokeWidth = Math.Max(0.5, strokeWidth);
            var textGeo = formatted.BuildGeometry(new Point(0, 0));

            var textPos = new Point(rect.Left + (rect.Width - formatted.Width) / 2, rect.Top + (rect.Height - formatted.Height) / 2);
            ctx.PushTransform(new TranslateTransform(textPos.X, textPos.Y));

            // 绘制描边
            var strokePen = new Pen(new SolidColorBrush(Color.FromArgb(0xDD, 0x25, 0x2F, 0x3A)), strokeWidth)
            {
                LineJoin = PenLineJoin.Round
            };
            ctx.DrawGeometry(null, strokePen, textGeo);

            // 绘制填充
            ctx.DrawGeometry(textBrush, null, textGeo);
            ctx.Pop();
            return;
        }

        var finalTextPos = new Point(rect.Left + (rect.Width - formatted.Width) / 2, rect.Top + (rect.Height - formatted.Height) / 2);
        ctx.DrawText(formatted, finalTextPos);
    }

    private static string ApplyTemplate(string template, string value, string fallbackTemplate)
    {
        template = string.IsNullOrWhiteSpace(template) ? fallbackTemplate : template.Trim();
        value = value ?? string.Empty;

        if (template.Contains("{value}", StringComparison.OrdinalIgnoreCase))
        {
            return template.Replace("{value}", value, StringComparison.OrdinalIgnoreCase);
        }

        if (template.Contains("value", StringComparison.OrdinalIgnoreCase))
        {
            return template.Replace("value", value, StringComparison.OrdinalIgnoreCase);
        }

        return template + value;
    }

    private static string ApplyMultiTemplate(string template, WeatherNow now)
    {
        template = string.IsNullOrWhiteSpace(template) ? string.Empty : template.Trim();
        if (template.Length == 0)
        {
            return string.Empty;
        }

        var temp = $"{Math.Round(now.TemperatureC):0}";
        var uv = now.UvIndex is null ? "—" : $"{Math.Round(now.UvIndex.Value):0}";
        var rh = now.RelativeHumidityPercent is null ? "—" : $"{now.RelativeHumidityPercent.Value}";

        return template
            .Replace("{temp}", temp, StringComparison.OrdinalIgnoreCase)
            .Replace("{uv}", uv, StringComparison.OrdinalIgnoreCase)
            .Replace("{rh}", rh, StringComparison.OrdinalIgnoreCase)
            .Replace("{humidity}", rh, StringComparison.OrdinalIgnoreCase);
    }

    private static FontFamily GetFontFamily(string fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName))
        {
            return new FontFamily("Segoe UI");
        }

        return new FontFamily(fontFamilyName);
    }

    private static Color ParseColor(string colorString)
    {
        if (string.IsNullOrWhiteSpace(colorString))
        {
            return Colors.White;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorString);
            return color;
        }
        catch
        {
            return Colors.White;
        }
    }
}
