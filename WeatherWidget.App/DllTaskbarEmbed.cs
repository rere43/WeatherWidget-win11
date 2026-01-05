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
    private static extern bool TaskbarEmbed_AdjustPosition(IntPtr hwnd, int width, int height, int offsetX);

    [DllImport("TaskbarEmbedDll.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void TaskbarEmbed_BringToTop(IntPtr hwnd);

    // Win32 API for window visibility
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

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
    private System.Threading.Timer? _topMostTimer;  // 专门用于保持置顶
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
        AppLogger.Info("DllTaskbarEmbed: TryCreate called");
        try
        {
            AppLogger.Info("DllTaskbarEmbed: Calling GetTaskbarInfo...");
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

            // 定时检查窗口可见性和位置（2秒间隔）
            _visibilityTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(EnsureVisible);
            }, null, 2000, 2000);

            // 高频置顶定时器（200ms，对抗预览窗口遮挡）
            _topMostTimer = new System.Threading.Timer(_ =>
            {
                if (_hwnd != IntPtr.Zero && !_disposed)
                {
                    TaskbarEmbed_BringToTop(_hwnd);
                }
            }, null, 500, 200);

            // 订阅天气更新
            _panelViewModel.WeatherUpdated += OnWeatherUpdated;

            return true;
        }
        catch (DllNotFoundException ex)
        {
            AppLogger.Info($"DllTaskbarEmbed: DLL not found: {ex.Message}");
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            AppLogger.Info($"DllTaskbarEmbed: Entry point not found: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"DllTaskbarEmbed: TryCreate failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
            // 检测是否有全屏窗口，如有则隐藏
            if (IsFullscreenAppRunning())
            {
                if (IsWindowVisible(_hwnd))
                    ShowWindow(_hwnd, 0); // SW_HIDE
                return;
            }

            // 动态调整位置（跟随通知区变化 + 用户偏移）
            var offsetX = (int)_panelViewModel.Settings.EmbeddedOffsetX;
            TaskbarEmbed_AdjustPosition(_hwnd, _width, _height, offsetX);

            // 只在窗口不可见时才重新显示，避免干扰任务栏交互
            if (!IsWindowVisible(_hwnd))
            {
                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
                UpdateContent();
            }
        }
        catch
        {
            // 忽略错误
        }
    }

    private bool IsFullscreenAppRunning()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        var mon = MonitorFromWindow(fg, 1); // MONITOR_DEFAULTTOPRIMARY
        if (!GetMonitorInfo(mon, ref mi)) return false;

        if (!GetWindowRect(fg, out var wr)) return false;

        // 前台窗口覆盖整个显示器则视为全屏
        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
               wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
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
            var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.BadgeFontFamily) ? "Segoe UI" : settings.BadgeFontFamily);
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            // 布局：[UV进度条] [天气图标] [温度/湿度2行]
            var padding = 4;
            var uvBarWidth = 12;
            var uvBarHeight = _height - 16;
            var uvBarX = padding;
            var uvBarY = 6;

            // 1. 绘制UV竖向进度条（左侧）
            var uvValue = Math.Clamp(now.UvIndex ?? 0, 0, 11);
            var uvProgress = uvValue / 11.0;

            // 底色灰
            ctx.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
                null,
                new Rect(uvBarX, uvBarY, uvBarWidth, uvBarHeight),
                3, 3);

            // 填充粉紫色（从底部向上填充）
            var fillHeight = uvBarHeight * uvProgress;
            if (fillHeight > 2)
            {
                ctx.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(0xDA, 0x70, 0xD6)), // 粉紫色
                    null,
                    new Rect(uvBarX, uvBarY + uvBarHeight - fillHeight, uvBarWidth, fillHeight),
                    3, 3);
            }

            // UV数字（进度条下方）
            var uvFontSize = 8;
            var uvText = $"{uvValue:0.#}";
            var uvFt = new FormattedText(uvText, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, uvFontSize, Brushes.White, 1.0);
            ctx.DrawText(uvFt, new Point(uvBarX + (uvBarWidth - uvFt.Width) / 2, uvBarY + uvBarHeight + 1));

            // 2. 天气图标（UV进度条右侧）
            var iconX = uvBarX + uvBarWidth + 4;
            var baseIconSize = Math.Min(_height - 8, 48);
            var iconSize = (int)(baseIconSize * settings.EmbeddedIconScale);
            iconSize = Math.Clamp(iconSize, 16, _height - 4);

            var artProvider = new WeatherArtProvider();
            var weatherArt = artProvider.RenderBaseArt(now.WeatherCode, iconSize);
            ctx.DrawImage(weatherArt, new Rect(iconX, (_height - iconSize) / 2.0, iconSize, iconSize));

            // 3. 文字区域（2行：温度、湿度，Y轴居中）
            var textX = iconX + iconSize + 6;
            var tempColor = ParseColor(settings.TempBadgeColor, Colors.White);
            var humidityColor = ParseColor(settings.CornerBadgeColor, Colors.White);

            // 2行布局，整体垂直居中
            var lineHeight = Math.Min((_height - 8) / 2.0, 20);
            var fontSize = Math.Min(lineHeight * 0.9, 14) * settings.TempBadgeFontScale;
            var totalTextHeight = lineHeight * 2;
            var textStartY = (_height - totalTextHeight) / 2.0;

            // 第1行：温度
            var tempText = settings.TempBadgeFormat.Replace("{value}", $"{Math.Round(now.TemperatureC):0}");
            DrawTextWithStroke(ctx, tempText, textX, textStartY, fontSize, typeface, tempColor, settings.BadgeStrokeWidth);

            // 第2行：湿度
            var humidityText = (settings.CornerHumidityFormat ?? "{value}%").Replace("{value}", $"{now.RelativeHumidityPercent:0}");
            var humidityFontSize = fontSize * settings.CornerBadgeFontScale;
            DrawTextWithStroke(ctx, humidityText, textX, textStartY + lineHeight, humidityFontSize, typeface, humidityColor, settings.BadgeStrokeWidth);
        }

        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White, 1.0);
        return ft.WidthIncludingTrailingWhitespace;
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
        _topMostTimer?.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            TaskbarEmbed_Destroy(_hwnd);
            _hwnd = IntPtr.Zero;
            AppLogger.Info("DllTaskbarEmbed: Destroyed");
        }
    }
}
