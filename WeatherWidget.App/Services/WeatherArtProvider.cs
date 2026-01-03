using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeatherWidget.App.Services;

public sealed class WeatherArtProvider
{
    private readonly WeatherIconMapper _iconMapper = new();

    public ImageSource RenderBaseArt(int weatherCode, int size)
    {
        // 优先使用天气图标目录下的图标
        var hour = DateTime.Now.Hour;
        var isNight = hour < 6 || hour >= 18;
        var icon = _iconMapper.GetIcon(weatherCode, isNight, size);
        if (icon != null)
        {
            return icon;
        }

        // 后备：使用旧版绘制逻辑
        var condition = WeatherCodeMapper.ToCondition(weatherCode);
        var fromAsset = TryLoadAsset(condition, size);
        if (fromAsset is not null)
        {
            return fromAsset;
        }

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {

            var background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF8, 0xFF));
            ctx.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), size * 0.22, size * 0.22);

            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x3A, 0x4A)), Math.Max(2, size * 0.05))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };

            switch (condition)
            {
                case WeatherCondition.Clear:
                    DrawSun(ctx, stroke, size);
                    break;
                case WeatherCondition.Cloudy:
                    DrawCloud(ctx, stroke, size, isDark: false);
                    break;
                case WeatherCondition.Rain:
                    DrawCloud(ctx, stroke, size, isDark: true);
                    DrawRain(ctx, size);
                    break;
                case WeatherCondition.Snow:
                    DrawCloud(ctx, stroke, size, isDark: true);
                    DrawSnow(ctx, size);
                    break;
                case WeatherCondition.Thunder:
                    DrawCloud(ctx, stroke, size, isDark: true);
                    DrawLightning(ctx, stroke, size);
                    break;
                default:
                    DrawCloud(ctx, stroke, size, isDark: false);
                    break;
            }
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource? TryLoadAsset(WeatherCondition condition, int size)
    {
        var name = condition switch
        {
            WeatherCondition.Clear => "clear",
            WeatherCondition.Cloudy => "cloudy",
            WeatherCondition.Rain => "rain",
            WeatherCondition.Snow => "snow",
            WeatherCondition.Thunder => "thunder",
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
            bmp.DecodePixelWidth = size;
            bmp.DecodePixelHeight = size;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSun(DrawingContext ctx, Pen stroke, int size)
    {
        var center = new Point(size * 0.50, size * 0.52);
        var radius = size * 0.22;
        var fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x55));
        ctx.DrawEllipse(fill, stroke, center, radius, radius);

        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var inner = new Point(center.X + Math.Cos(angle) * radius * 1.20, center.Y + Math.Sin(angle) * radius * 1.20);
            var outer = new Point(center.X + Math.Cos(angle) * radius * 1.55, center.Y + Math.Sin(angle) * radius * 1.55);
            ctx.DrawLine(stroke, inner, outer);
        }
    }

    private static void DrawCloud(DrawingContext ctx, Pen stroke, int size, bool isDark)
    {
        var fill = new SolidColorBrush(isDark ? Color.FromRgb(0xB9, 0xC6, 0xD8) : Color.FromRgb(0xE7, 0xF0, 0xFF));
        var baseY = size * 0.62;
        var x = size * 0.22;
        var w = size * 0.56;
        var h = size * 0.26;

        ctx.DrawRoundedRectangle(fill, stroke, new Rect(x, baseY - h * 0.55, w, h), h * 0.55, h * 0.55);
        ctx.DrawEllipse(fill, stroke, new Point(x + w * 0.25, baseY - h * 0.55), h * 0.50, h * 0.42);
        ctx.DrawEllipse(fill, stroke, new Point(x + w * 0.55, baseY - h * 0.72), h * 0.58, h * 0.48);
        ctx.DrawEllipse(fill, stroke, new Point(x + w * 0.80, baseY - h * 0.55), h * 0.50, h * 0.42);
    }

    private static void DrawRain(DrawingContext ctx, int size)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x43, 0x82, 0xFF)), Math.Max(2, size * 0.05))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        for (var i = 0; i < 3; i++)
        {
            var x = size * (0.35 + i * 0.15);
            ctx.DrawLine(pen, new Point(x, size * 0.70), new Point(x - size * 0.04, size * 0.86));
        }
    }

    private static void DrawSnow(DrawingContext ctx, int size)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x77, 0xC7, 0xFF)), Math.Max(2, size * 0.04))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        for (var i = 0; i < 3; i++)
        {
            var x = size * (0.35 + i * 0.15);
            var y = size * 0.78;
            ctx.DrawLine(pen, new Point(x - size * 0.03, y), new Point(x + size * 0.03, y));
            ctx.DrawLine(pen, new Point(x, y - size * 0.03), new Point(x, y + size * 0.03));
            ctx.DrawLine(pen, new Point(x - size * 0.02, y - size * 0.02), new Point(x + size * 0.02, y + size * 0.02));
            ctx.DrawLine(pen, new Point(x - size * 0.02, y + size * 0.02), new Point(x + size * 0.02, y - size * 0.02));
        }
    }

    private static void DrawLightning(DrawingContext ctx, Pen stroke, int size)
    {
        var fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3A));
        var geo = Geometry.Parse("M 0,0 L 14,0 L 6,14 L 16,14 L 2,32 L 8,18 L 0,18 Z");

        var scale = size / 64.0;
        var t = new TransformGroup();
        t.Children.Add(new ScaleTransform(scale, scale));
        t.Children.Add(new TranslateTransform(size * 0.40, size * 0.62));
        geo.Transform = t;

        ctx.DrawGeometry(fill, stroke, geo);
    }
}
