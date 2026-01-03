using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

/// <summary>
/// 使用 C++ DLL 实现任务栏嵌入的封装类。
/// </summary>
public sealed class DllTaskbarEmbed : IDisposable
{
    #region P/Invoke

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr TaskbarEmbed_Create(int width, int height);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void TaskbarEmbed_Destroy(IntPtr hwnd);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool TaskbarEmbed_UpdateBitmap(IntPtr hwnd, IntPtr bitmapData, int width, int height);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool TaskbarEmbed_SetPosition(IntPtr hwnd, int x, int y);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool TaskbarEmbed_GetTaskbarInfo(out int width, out int height, out int left, out int top);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool TaskbarEmbed_IsWindows11Taskbar();

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool TaskbarEmbed_AdjustPosition(IntPtr hwnd, int width, int height);

    // Win32 API for window visibility
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_SHOWNOACTIVATE = 4;

    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private IntPtr _hwnd;
    private System.Threading.Timer? _updateTimer;
    private System.Threading.Timer? _visibilityTimer;
    private int _width = 180;
    private int _height = 64;  // 增大高度容纳图标和文字
    private byte[]? _bitmapBuffer;
    private bool _disposed;

    public bool IsCreated => _hwnd != IntPtr.Zero;

    public DllTaskbarEmbed(PanelViewModel panelViewModel, IconRenderer iconRenderer)
    {
        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;
    }

    public bool TryCreate()
    {
        try
        {
            // 获取任务栏信息
            if (TaskbarEmbed_GetTaskbarInfo(out var tbWidth, out var tbHeight, out var tbLeft, out var tbTop))
            {
                _height = Math.Max(tbHeight - 8, 48);  // 使用任务栏高度，最小48
                AppLogger.Info($"DllTaskbarEmbed: Taskbar size={tbWidth}x{tbHeight} at ({tbLeft},{tbTop}), using height={_height}");
            }

            // 检测 Win11
            var isWin11 = TaskbarEmbed_IsWindows11Taskbar();
            AppLogger.Info($"DllTaskbarEmbed: IsWindows11={isWin11}");

            // 创建嵌入窗口
            _hwnd = TaskbarEmbed_Create(_width, _height);

            if (_hwnd == IntPtr.Zero)
            {
                AppLogger.Info("DllTaskbarEmbed: TaskbarEmbed_Create returned NULL");
                return false;
            }

            AppLogger.Info($"DllTaskbarEmbed: Created hwnd=0x{_hwnd:X}");

            // 立即更新一次
            UpdateContent();

            // 定时更新内容
            _updateTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(UpdateContent);
            }, null, 1000, 5000);

            // 定时检查窗口可见性（每 500ms）
            _visibilityTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(EnsureVisible);
            }, null, 500, 500);

            // 订阅天气更新
            _panelViewModel.WeatherUpdated += OnWeatherUpdated;

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"DllTaskbarEmbed: TryCreate failed: {ex.Message}");
            return false;
        }
    }

    private void OnWeatherUpdated(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(UpdateContent);
    }

    private void EnsureVisible()
    {
        if (_hwnd == IntPtr.Zero || _disposed)
            return;

        try
        {
            // 动态调整位置（跟随通知区变化）
            TaskbarEmbed_AdjustPosition(_hwnd, _width, _height);

            // 强制窗口保持可见和置顶
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // 如果窗口不可见，重新显示
            if (!IsWindowVisible(_hwnd))
            {
                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
                UpdateContent(); // 重新绘制
            }
        }
        catch
        {
            // 忽略错误
        }
    }

    private void UpdateContent()
    {
        if (_hwnd == IntPtr.Zero || _disposed)
            return;

        var snapshot = _panelViewModel.Snapshot;
        if (snapshot is null)
            return;

        try
        {
            // 渲染内容
            var bitmap = RenderContent(snapshot.Now, _panelViewModel.Settings);
            if (bitmap is not BitmapSource bmp)
                return;

            // 转换为 BGRA 数组
            var width = bmp.PixelWidth;
            var height = bmp.PixelHeight;
            var stride = width * 4;

            if (_bitmapBuffer == null || _bitmapBuffer.Length != height * stride)
                _bitmapBuffer = new byte[height * stride];

            bmp.CopyPixels(_bitmapBuffer, stride, 0);

            // 传给 DLL
            var handle = GCHandle.Alloc(_bitmapBuffer, GCHandleType.Pinned);
            try
            {
                var result = TaskbarEmbed_UpdateBitmap(_hwnd, handle.AddrOfPinnedObject(), width, height);
                if (!result)
                {
                    AppLogger.Info("DllTaskbarEmbed: UpdateBitmap failed");
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"DllTaskbarEmbed: UpdateContent failed: {ex.Message}");
        }
    }

    private ImageSource RenderContent(WeatherNow now, Settings settings)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            // 不绘制背景，保持透明

            // 天气图标（左侧，48px 与主程序图标一致）
            var iconSize = 48;
            var artProvider = new WeatherArtProvider();
            var weatherArt = artProvider.RenderBaseArt(now.WeatherCode, iconSize);
            ctx.DrawImage(weatherArt, new Rect(4, (_height - iconSize) / 2, iconSize, iconSize));

            // 文字区域（右侧，3行）
            var textX = iconSize + 10;
            var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.BadgeFontFamily) ? "Segoe UI" : settings.BadgeFontFamily);
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            // 解析颜色
            var tempColor = ParseColor(settings.TempBadgeColor, Colors.White);
            var cornerColor = ParseColor(settings.CornerBadgeColor, Colors.White);
            var extraColor = ParseColor(settings.ExtraBadgeColor, Colors.White);

            // 3行文字，根据窗口高度计算行高（避免重叠）
            var lineHeight = (_height - 8) / 3.0;  // 上下各留4px
            var baseFontSize = Math.Min(lineHeight * 0.85, 16);  // 最大16px字体

            // 第1行：温度
            var line1FontSize = baseFontSize * settings.TempBadgeFontScale;
            var line1Text = settings.TempBadgeFormat.Replace("{value}", $"{Math.Round(now.TemperatureC):0}");
            DrawTextWithStroke(ctx, line1Text, textX, 4, line1FontSize, typeface, tempColor, settings.BadgeStrokeWidth);

            // 第2行：角标数据
            var line2FontSize = baseFontSize * settings.CornerBadgeFontScale;
            var line2Text = GetCornerText(now, settings);
            DrawTextWithStroke(ctx, line2Text, textX, 4 + lineHeight, line2FontSize, typeface, cornerColor, settings.BadgeStrokeWidth);

            // 第3行：额外信息
            if (settings.ExtraBadgeEnabled && !string.IsNullOrWhiteSpace(settings.ExtraBadgeFormat))
            {
                var line3FontSize = baseFontSize * settings.ExtraBadgeFontScale;
                var line3Text = FormatExtraBadge(settings.ExtraBadgeFormat, now);
                DrawTextWithStroke(ctx, line3Text, textX, 4 + lineHeight * 2, line3FontSize, typeface, extraColor, settings.BadgeStrokeWidth);
            }
        }

        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private void DrawTextWithStroke(DrawingContext ctx, string text, double x, double y, double fontSize, Typeface typeface, Color color, double strokeWidth)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            1.0);

        var textGeo = formatted.BuildGeometry(new Point(x, y));

        // 深色描边
        var strokePen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)), strokeWidth)
        {
            LineJoin = PenLineJoin.Round
        };
        ctx.DrawGeometry(null, strokePen, textGeo);

        // 填充
        ctx.DrawGeometry(new SolidColorBrush(color), null, textGeo);
    }

    private string GetCornerText(WeatherNow now, Settings settings)
    {
        return settings.IconCornerMetric switch
        {
            Models.IconCornerMetric.UvIndex => settings.CornerUvFormat.Replace("{value}", $"{now.UvIndex:0.#}"),
            Models.IconCornerMetric.Humidity => settings.CornerHumidityFormat.Replace("{value}", $"{now.RelativeHumidityPercent:0}"),
            _ => ""
        };
    }

    private string FormatExtraBadge(string format, WeatherNow now)
    {
        return format
            .Replace("{temp}", $"{Math.Round(now.TemperatureC):0}")
            .Replace("{uv}", $"{now.UvIndex:0.#}")
            .Replace("{humidity}", $"{now.RelativeHumidityPercent:0}");
    }

    private static Color ParseColor(string hex, Color defaultColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return defaultColor;
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return c;
        }
        catch
        {
            return defaultColor;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _panelViewModel.WeatherUpdated -= OnWeatherUpdated;
        _updateTimer?.Dispose();
        _visibilityTimer?.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            TaskbarEmbed_Destroy(_hwnd);
            _hwnd = IntPtr.Zero;
            AppLogger.Info("DllTaskbarEmbed: Destroyed");
        }
    }
}
