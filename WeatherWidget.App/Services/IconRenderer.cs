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

    public ImageSource RenderWeatherIcon(WeatherNow now, EmbeddedWidgetSettings settings, int size = 64)
    {
        size = Math.Clamp(size, 16, 512);

        var baseArt = _artProvider.RenderBaseArt(now.WeatherCode, size);

        var visual = new DrawingVisual();
        ConfigureVisualQuality(visual);
        using (var ctx = visual.RenderOpen())
        {
            if (settings.WeatherIconBackgroundEnabled)
            {
                // 加一个浅色底，避免透明 PNG 在深色任务栏上观感异常
                var bgFill = new SolidColorBrush(Color.FromRgb(0xF3, 0xF8, 0xFF));
                var bgStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0xE6, 0xF7)), Math.Max(1, size * 0.03));
                ctx.DrawRoundedRectangle(bgFill, bgStroke, new Rect(0, 0, size, size), size * 0.22, size * 0.22);
            }

            // 偏移单位以 64px 图标为基准
            var offsetScale = size / 64.0;
            var iconDx = settings.WeatherIconOffsetX * offsetScale;
            var iconDy = settings.WeatherIconOffsetY * offsetScale;
            ctx.DrawImage(baseArt, new Rect(iconDx, iconDy, size, size));
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void ConfigureVisualQuality(Visual visual)
    {
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
    }
}

