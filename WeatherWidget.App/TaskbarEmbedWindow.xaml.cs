using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

/// <summary>
/// 嵌入任务栏的天气显示窗口，类似 TrafficMonitor 的实现方式。
/// 通过 SetParent 将窗口嵌入到任务栏中，可以自定义布局和间距。
/// </summary>
public partial class TaskbarEmbedWindow : Window
{
    #region Win32 API

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;

    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private IntPtr _rebarHwnd;
    private bool _isEmbedded;
    private System.Windows.Threading.DispatcherTimer? _positionTimer;

    public IntPtr Handle => _hwnd;

    public TaskbarEmbedWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer)
    {
        InitializeComponent();

        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;

        Loaded += OnLoaded;
        _panelViewModel.WeatherUpdated += (_, _) => UpdateContent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // 尝试嵌入任务栏
        if (TryEmbedToTaskbar())
        {
            _isEmbedded = true;
            AppLogger.Info("TaskbarEmbedWindow: Successfully embedded to taskbar");

            // 定时检查位置（任务栏可能被移动或调整大小）
            _positionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _positionTimer.Tick += (_, _) => AdjustPosition();
            _positionTimer.Start();
        }
        else
        {
            AppLogger.Info("TaskbarEmbedWindow: Failed to embed, falling back to overlay mode");
            // 回退到悬浮模式
            SetupOverlayMode();
        }

        UpdateContent();
    }

    private bool TryEmbedToTaskbar()
    {
        try
        {
            // 查找主任务栏
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero)
            {
                AppLogger.Info("TaskbarEmbedWindow: Shell_TrayWnd not found");
                return false;
            }

            AppLogger.Info($"TaskbarEmbedWindow: Found Shell_TrayWnd=0x{_taskbarHwnd:X}");

            // Win11 和 Win10 的任务栏结构不同，尝试多种父窗口
            // 优先级: MSTaskSwWClass > MSTaskListWClass > ReBarWindow32 > Shell_TrayWnd
            var candidates = new[]
            {
                ("MSTaskSwWClass", FindWindowEx(_taskbarHwnd, IntPtr.Zero, "MSTaskSwWClass", null)),
                ("MSTaskListWClass", FindNestedWindow(_taskbarHwnd, "MSTaskListWClass")),
                ("ReBarWindow32", FindWindowEx(_taskbarHwnd, IntPtr.Zero, "ReBarWindow32", null)),
                ("Shell_TrayWnd", _taskbarHwnd),
            };

            foreach (var (name, hwnd) in candidates)
            {
                if (hwnd == IntPtr.Zero)
                {
                    AppLogger.Info($"TaskbarEmbedWindow: {name} not found");
                    continue;
                }

                AppLogger.Info($"TaskbarEmbedWindow: Trying {name}=0x{hwnd:X}");

                if (TryEmbedTo(hwnd))
                {
                    _rebarHwnd = hwnd;
                    AppLogger.Info($"TaskbarEmbedWindow: Successfully embedded to {name}");
                    return true;
                }
            }

            AppLogger.Info("TaskbarEmbedWindow: All embed attempts failed");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"TaskbarEmbedWindow: Embed failed: {ex.Message}");
            return false;
        }
    }

    private IntPtr FindNestedWindow(IntPtr parent, string className)
    {
        // 递归查找嵌套窗口（Win11 任务栏结构更深）
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var found = FindWindowEx(child, IntPtr.Zero, className, null);
            if (found != IntPtr.Zero)
                return found;

            // 递归搜索
            found = FindNestedWindow(child, className);
            if (found != IntPtr.Zero)
                return found;

            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }

    private bool TryEmbedTo(IntPtr parentHwnd)
    {
        try
        {
            // 获取父窗口尺寸
            if (!GetClientRect(parentHwnd, out var parentRect))
            {
                AppLogger.Info($"TaskbarEmbedWindow: GetClientRect failed for 0x{parentHwnd:X}");
                return false;
            }

            AppLogger.Info($"TaskbarEmbedWindow: Parent rect={parentRect.Width}x{parentRect.Height}");

            // 先设置父窗口，再修改样式（某些情况下顺序很重要）
            var oldParent = SetParent(_hwnd, parentHwnd);
            if (oldParent == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                AppLogger.Info($"TaskbarEmbedWindow: SetParent failed, error={err}");

                // 尝试先修改样式再 SetParent
                var style = GetWindowLong(_hwnd, GWL_STYLE);
                style &= ~WS_POPUP;
                style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
                SetWindowLong(_hwnd, GWL_STYLE, style);

                oldParent = SetParent(_hwnd, parentHwnd);
                if (oldParent == IntPtr.Zero)
                {
                    err = Marshal.GetLastWin32Error();
                    AppLogger.Info($"TaskbarEmbedWindow: SetParent retry failed, error={err}");
                    return false;
                }
            }

            // 修改窗口样式
            var newStyle = GetWindowLong(_hwnd, GWL_STYLE);
            newStyle &= ~WS_POPUP;
            newStyle |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
            SetWindowLong(_hwnd, GWL_STYLE, newStyle);

            var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            exStyle &= ~WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

            // 设置位置
            var dpi = VisualTreeHelper.GetDpi(this);
            var widthPx = (int)(Width * dpi.DpiScaleX);
            var heightPx = (int)(Height * dpi.DpiScaleY);

            var x = parentRect.Width - widthPx - 10;
            var y = (parentRect.Height - heightPx) / 2;

            SetWindowPos(_hwnd, IntPtr.Zero, x, y, widthPx, heightPx,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"TaskbarEmbedWindow: TryEmbedTo failed: {ex.Message}");
            return false;
        }
    }

    private void AdjustPosition()
    {
        if (!_isEmbedded || _rebarHwnd == IntPtr.Zero)
            return;

        try
        {
            if (!GetClientRect(_rebarHwnd, out var rebarRect))
                return;

            var dpi = VisualTreeHelper.GetDpi(this);
            var widthPx = (int)(Width * dpi.DpiScaleX);
            var heightPx = (int)(Height * dpi.DpiScaleY);

            // 居中放置在任务栏高度方向，水平靠右（在系统托盘左边）
            var x = rebarRect.Width - widthPx - 10;
            var y = (rebarRect.Height - heightPx) / 2;

            SetWindowPos(_hwnd, IntPtr.Zero, x, y, widthPx, heightPx,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            AppLogger.Info($"TaskbarEmbedWindow: AdjustPosition failed: {ex.Message}");
        }
    }

    private void SetupOverlayMode()
    {
        // 悬浮模式：窗口贴在任务栏上方
        try
        {
            // 获取任务栏位置和尺寸
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd != IntPtr.Zero && GetWindowRect(_taskbarHwnd, out var taskbarRect))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var widthPx = Width * dpi.DpiScaleX;
                var heightPx = Height * dpi.DpiScaleY;

                // 任务栏在底部：窗口放在任务栏上方，靠右（系统托盘左边）
                if (taskbarRect.Top > 100) // 底部任务栏
                {
                    Left = (taskbarRect.Right - widthPx - 120) / dpi.DpiScaleX;
                    Top = (taskbarRect.Top - heightPx - 4) / dpi.DpiScaleY;
                }
                // 任务栏在顶部
                else if (taskbarRect.Bottom < 200)
                {
                    Left = (taskbarRect.Right - widthPx - 120) / dpi.DpiScaleX;
                    Top = (taskbarRect.Bottom + 4) / dpi.DpiScaleY;
                }
                // 任务栏在左边
                else if (taskbarRect.Right < 200)
                {
                    Left = (taskbarRect.Right + 4) / dpi.DpiScaleX;
                    Top = (taskbarRect.Bottom - heightPx - 100) / dpi.DpiScaleY;
                }
                // 任务栏在右边
                else
                {
                    Left = (taskbarRect.Left - widthPx - 4) / dpi.DpiScaleX;
                    Top = (taskbarRect.Bottom - heightPx - 100) / dpi.DpiScaleY;
                }

                AppLogger.Info($"TaskbarEmbedWindow: Overlay at ({Left:0},{Top:0}), taskbar=({taskbarRect.Left},{taskbarRect.Top},{taskbarRect.Right},{taskbarRect.Bottom})");
            }
            else
            {
                // 回退：右下角
                var screen = SystemParameters.WorkArea;
                Left = screen.Right - Width - 10;
                Top = screen.Bottom - Height - 10;
            }

            // 保持窗口置顶
            Topmost = true;

            // 定时调整位置（任务栏可能被移动）
            _positionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _positionTimer.Tick += (_, _) => SetupOverlayMode();
            _positionTimer.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Info($"TaskbarEmbedWindow: SetupOverlayMode failed: {ex.Message}");
            Left = 100;
            Top = 100;
        }
    }

    private void UpdateContent()
    {
        var snapshot = _panelViewModel.Snapshot;
        if (snapshot is null)
            return;

        try
        {
            var settings = _panelViewModel.Settings;
            var image = RenderEmbedContent(snapshot.Now, settings);
            ContentImage.Source = image;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"TaskbarEmbedWindow: UpdateContent failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 渲染嵌入内容：UV条数字 + 天气图标 + 气温 + 湿度
    /// </summary>
    private ImageSource RenderEmbedContent(Models.WeatherNow now, Models.Settings settings)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var widthDip = Width;
        var heightDip = Height;
        var widthPx = (int)(widthDip * dpi.DpiScaleX);
        var heightPx = (int)(heightDip * dpi.DpiScaleY);
        var embedded = settings.Embedded ?? Models.Settings.Default.Embedded;

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(embedded.FontFamily) ? "Segoe UI" : embedded.FontFamily);
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            var lineSpacing = embedded.LineSpacing;
            var uvToIconGap = Math.Max(embedded.UvToIconGap, 2);
            var iconToTextGap = Math.Max(embedded.IconToTextGap, 2);
            var uvFontScale = embedded.UvNumberFontScale < 0.5 ? 2.0 : embedded.UvNumberFontScale;

            var padding = 4.0;
            var uvBarWidth = 12.0;

            // UV 数字
            var uvValue = Math.Clamp(now.UvIndex ?? 0, 0, 11);
            var uvText = ApplyTemplate(embedded.UvNumberFormat, $"{uvValue:0.#}", "{value}");
            var uvFontSize = 8 * uvFontScale;
            var uvTextColor = ParseColor(embedded.UvNumberColor, Colors.White);
            var uvFt = new FormattedText(uvText, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, uvFontSize, new SolidColorBrush(uvTextColor), dpi.PixelsPerDip);

            var uvTextHeight = uvFt.Height + 2;
            var uvBarHeight = Math.Max(2, heightDip - 8 - uvTextHeight);
            var uvBarX = padding;
            var uvBarY = 4.0;

            // UV 竖条
            var uvProgress = uvValue / 11.0;
            var uvBg = ParseColor(embedded.UvBarBackgroundColor, Color.FromArgb(0x80, 0x80, 0x80, 0x80));
            var uvFill = ParseColor(embedded.UvBarFillColor, Color.FromRgb(0xDA, 0x70, 0xD6));

            ctx.DrawRoundedRectangle(new SolidColorBrush(uvBg), null, new Rect(uvBarX, uvBarY, uvBarWidth, uvBarHeight), 3, 3);

            var fillHeight = uvBarHeight * uvProgress;
            if (fillHeight > 2)
            {
                ctx.DrawRoundedRectangle(
                    new SolidColorBrush(uvFill),
                    null,
                    new Rect(uvBarX, uvBarY + uvBarHeight - fillHeight, uvBarWidth, fillHeight),
                    3, 3);
            }

            ctx.DrawText(uvFt, new Point(uvBarX + (uvBarWidth - uvFt.Width) / 2, uvBarY + uvBarHeight + 1));

            // 天气图标
            var iconX = uvBarX + uvBarWidth + uvToIconGap;
            var baseIconSizeDip = heightDip - 8;
            var iconSizeDip = Math.Clamp(baseIconSizeDip * embedded.IconScale, 16, heightDip - 4);
            var iconSizePx = Math.Max(16, (int)Math.Round(iconSizeDip * dpi.DpiScaleX));

            var icon = _iconRenderer.RenderWeatherIcon(now, embedded, iconSizePx);
            ctx.DrawImage(icon, new Rect(iconX, (heightDip - iconSizeDip) / 2.0, iconSizeDip, iconSizeDip));

            // 文字（温度/湿度两行）
            var textX = iconX + iconSizeDip + iconToTextGap;
            var baseFontSize = Math.Min((heightDip - 8) / 2.0 * 0.9, 14);
            var tempFontSize = baseFontSize * embedded.TemperatureFontScale;
            var humidityFontSize = baseFontSize * embedded.HumidityFontScale;

            var tempText = ApplyTemplate(embedded.TemperatureFormat, $"{Math.Round(now.TemperatureC):0}", "{value}°");
            var humidityText = ApplyTemplate(embedded.HumidityFormat, $"{now.RelativeHumidityPercent:0}", "{value}%");

            var tempColor = ParseColor(embedded.TemperatureColor, Colors.White);
            var humidityColor = ParseColor(embedded.HumidityColor, Colors.White);

            var ft1 = new FormattedText(tempText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, tempFontSize, Brushes.White, dpi.PixelsPerDip);
            var ft2 = new FormattedText(humidityText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, humidityFontSize, Brushes.White, dpi.PixelsPerDip);

            var totalTextHeight = ft1.Height + lineSpacing + ft2.Height;
            var textStartY = (heightDip - totalTextHeight) / 2.0;

            DrawTextWithStroke(ctx, tempText, textX, textStartY, tempFontSize, typeface, tempColor, embedded.TextStrokeWidth, dpi.PixelsPerDip);
            DrawTextWithStroke(ctx, humidityText, textX, textStartY + ft1.Height + lineSpacing, humidityFontSize, typeface, humidityColor, embedded.TextStrokeWidth, dpi.PixelsPerDip);
        }

        var bmp = new RenderTargetBitmap(widthPx, heightPx, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void DrawTextWithStroke(
        DrawingContext ctx,
        string text,
        double x,
        double y,
        double fontSize,
        Typeface typeface,
        Color color,
        double strokeWidth,
        double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            pixelsPerDip);

        if (strokeWidth <= 0)
        {
            ctx.DrawText(formatted, new Point(x, y));
            return;
        }

        var textGeo = formatted.BuildGeometry(new Point(x, y));
        var strokePen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)), strokeWidth)
        {
            LineJoin = PenLineJoin.Round
        };
        ctx.DrawGeometry(null, strokePen, textGeo);
        ctx.DrawGeometry(new SolidColorBrush(color), null, textGeo);
    }

    private static string ApplyTemplate(string template, string value, string fallbackTemplate)
    {
        template = string.IsNullOrWhiteSpace(template) ? fallbackTemplate : template.Trim();
        value ??= string.Empty;

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

    private static Color ParseColor(string hex, Color defaultColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return defaultColor;
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return defaultColor;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _positionTimer?.Stop();
        base.OnClosed(e);
    }
}
