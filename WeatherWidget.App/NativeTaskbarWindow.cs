using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

public sealed class NativeTaskbarWindow : IDisposable
{
    #region Win32 API
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? lp, string? ln);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string? cl, string? ln);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr i, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr CreateWindowEx(uint ex, string cl, string ln, uint st, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr i, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern ushort RegisterClass(ref WNDCLASS wc);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr d, ref POINT pd, ref SIZE ps, IntPtr s, ref POINT pp, int k, ref BLENDFUNCTION b, uint f);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint u, out IntPtr bits, IntPtr hSection, uint o);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? n);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr h, int n, IntPtr dw);
    [DllImport("user32.dll", EntryPoint="SetWindowLong")] private static extern int SetWindowLong32(IntPtr h, int n, int dw);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct WNDCLASS { public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private readonly Action<bool>? _onHoverChanged;
    private IntPtr _hwnd, _taskbarHwnd;
    private System.Threading.Timer? _updateTimer, _positionTimer, _hoverTimer;
    private byte[]? _bitmapBuffer;
    private int _width = 150, _height = 40, _lastX = int.MinValue, _lastY = int.MinValue;
    private bool _disposed, _isHovering;
    private WndProcDelegate? _wndProc; // 防止委托被 GC 回收导致闪退

    public NativeTaskbarWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer, Action<bool>? onHoverChanged = null) { _panelViewModel = panelViewModel; _iconRenderer = iconRenderer; _onHoverChanged = onHoverChanged; }

    public IntPtr Handle => _hwnd;

    public bool TryCreate()
    {
        try
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd)) return true;
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero) return false;

            _wndProc = new WndProcDelegate(WndProc); // 保存为成员变量防止 GC 回收
            var wndClass = new WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc), hInstance = GetModuleHandle(null), lpszClassName = "WeatherWidgetTaskbar" };
            RegisterClass(ref wndClass);

            _hwnd = CreateWindowEx(0x00000080 | 0x08000000 | 0x00080000 | 0x00000008 | 0x00000020, "WeatherWidgetTaskbar", "WeatherWidget", 0x80000000 | 0x10000000, 0, 0, _width, _height, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return false;

            if (IntPtr.Size == 8) SetWindowLongPtr64(_hwnd, -8, _taskbarHwnd);
            else SetWindowLong32(_hwnd, -8, (int)_taskbarHwnd);

            _positionTimer ??= new System.Threading.Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(() => AdjustPosition(false)), null, 500, 200);
            _updateTimer ??= new System.Threading.Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(() => Invalidate()), null, 0, 5000);
            _hoverTimer ??= new System.Threading.Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(CheckHover), null, 500, 100);
            return true;
        }
        catch { return false; }
    }

    private void AdjustPosition(bool force)
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;
        try
        {
            // 检测全屏：任务栏不可见或前台窗口覆盖整个显示器
            if (!IsWindowVisible(_taskbarHwnd) || IsFullscreenAppRunning())
            {
                if (IsWindowVisible(_hwnd)) ShowWindow(_hwnd, 0);
                return;
            }
            if (!IsWindowVisible(_hwnd)) ShowWindow(_hwnd, 4);
            if (!GetWindowRect(_taskbarHwnd, out var rt) || !GetClientRect(_taskbarHwnd, out var rc)) return;

            UpdateDesiredLayout(rc);

            var tray = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
            int notifyX = (tray != IntPtr.Zero && GetWindowRect(tray, out var rr)) ? rr.Left : rt.Right - 100;

            // 应用用户设置的左右偏移
            var offsetX = (int)Math.Round((_panelViewModel.Settings.Embedded ?? Settings.Default.Embedded).OffsetX);
            int tx = notifyX - _width - 8 + offsetX, ty = rt.Top + (rt.Height - _height) / 2;
            // 任务栏/通知区右键菜单弹出期间，避免频繁“置顶保活”打乱菜单的 Z 顺序，导致菜单被遮挡。
            var popupMenu = FindWindow("#32768", null);
            var popupMenuVisible = popupMenu != IntPtr.Zero && IsWindowVisible(popupMenu);

            if (!force && tx == _lastX && ty == _lastY)
            {
                if (!popupMenuVisible)
                {
                    SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
                }
                return;
            }

            _lastX = tx; _lastY = ty;
            if (popupMenuVisible)
            {
                SetWindowPos(_hwnd, IntPtr.Zero, tx, ty, _width, _height, 0x0010 | 0x0040 | 0x0004);
            }
            else
            {
                SetWindowPos(_hwnd, new IntPtr(-1), tx, ty, _width, _height, 0x0010 | 0x0040);
            }
            Invalidate();
        }
        catch { }
    }

    private bool IsFullscreenAppRunning()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetMonitorInfo(MonitorFromWindow(fg, 1), ref mi)) return false;
        if (!GetWindowRect(fg, out var wr)) return false;
        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
               wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
    }

    private IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l) { if (m == 0x0084) return (IntPtr)(-1); return DefWindowProc(h, m, w, l); }

    private void UpdateDesiredLayout(RECT taskbarClientRect)
    {
        var snapshot = _panelViewModel.Snapshot; if (snapshot is null) { _width = 100; return; }
        var settings = _panelViewModel.Settings; var now = snapshot.Now;
        var embedded = settings.Embedded ?? Settings.Default.Embedded;
        var tf = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(embedded.FontFamily) ? "Segoe UI" : embedded.FontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        // 计算实际布局宽度：UV条 + 间距 + 图标 + 间距 + 文字
        var uvBarWidth = 12;
        var uvToIconGap = Math.Max(embedded.UvToIconGap, 2);
        var iconToTextGap = Math.Max(embedded.IconToTextGap, 2);
        var baseIconSize = taskbarClientRect.Height - 8;
        var iconSize = (int)(baseIconSize * embedded.IconScale);
        iconSize = Math.Clamp(iconSize, 16, taskbarClientRect.Height - 4);

        var baseFontSize = Math.Min((taskbarClientRect.Height - 8) / 2.0 * 0.9, 14);
        var tempSize = baseFontSize * embedded.TemperatureFontScale;
        var humiditySize = baseFontSize * embedded.HumidityFontScale;
        var tempText = ApplyTemplate(embedded.TemperatureFormat, $"{Math.Round(now.TemperatureC):0}", "{value}°");
        var humidityText = ApplyTemplate(embedded.HumidityFormat, $"{now.RelativeHumidityPercent:0}", "{value}%");
        var textWidth = Math.Max(
            MeasureTextWidth(tempText, tf, tempSize, 1.0),
            MeasureTextWidth(humidityText, tf, humiditySize, 1.0));

        _width = (int)(4 + uvBarWidth + uvToIconGap + iconSize + iconToTextGap + textWidth + 8);
        _height = taskbarClientRect.Height;
    }

    private void Invalidate()
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd) || _disposed) return;
        try
        {
            var snp = _panelViewModel.Snapshot; if (snp == null) return;
            var rendered = RenderContent(snp.Now, _panelViewModel.Settings);
            if (rendered is not BitmapSource bmp) return;

            int w = bmp.PixelWidth, h = bmp.PixelHeight, s = w * 4;
            if (_bitmapBuffer == null || _bitmapBuffer.Length != h * s) _bitmapBuffer = new byte[h * s];
            bmp.CopyPixels(_bitmapBuffer, s, 0);

            var bi = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
            IntPtr hdcS = GetDC(IntPtr.Zero), hdcM = CreateCompatibleDC(hdcS), hBmp = CreateDIBSection(hdcM, ref bi, 0, out var bits, IntPtr.Zero, 0);
            Marshal.Copy(_bitmapBuffer, 0, bits, _bitmapBuffer.Length);
            IntPtr old = SelectObject(hdcM, hBmp);
            POINT pd = new POINT { x = _lastX, y = _lastY }, ps = new POINT { x = 0, y = 0 }; SIZE sz = new SIZE { cx = w, cy = h };
            BLENDFUNCTION bl = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(_hwnd, hdcS, ref pd, ref sz, hdcM, ref ps, 0, ref bl, 2);
            SelectObject(hdcM, old); DeleteObject(hBmp); DeleteDC(hdcM); ReleaseDC(IntPtr.Zero, hdcS);
        }
        catch { }
    }

    private ImageSource RenderContent(WeatherNow now, Settings settings)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var embedded = settings.Embedded ?? Settings.Default.Embedded;
            var tf = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(embedded.FontFamily) ? "Segoe UI" : embedded.FontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            var lineSpacing = embedded.LineSpacing;
            var uvToIconGap = Math.Max(embedded.UvToIconGap, 2);
            var iconToTextGap = Math.Max(embedded.IconToTextGap, 2);
            var uvFontScale = embedded.UvNumberFontScale < 0.5 ? 2.0 : embedded.UvNumberFontScale; // 默认2.0，最小0.5

            // 布局：[UV进度条+数字] [天气图标] [温度/湿度2行]
            var padding = 4;
            var uvBarWidth = 12;

            // UV数字字号（基础8px * 缩放因子）
            var uvFontSize = 8 * uvFontScale;
            var uvValue = Math.Clamp(now.UvIndex ?? 0, 0, 11);
            var uvText = ApplyTemplate(embedded.UvNumberFormat, $"{uvValue:0.#}", "{value}");
            var uvColor = ParseColor(embedded.UvNumberColor, Colors.White);
            var uvFt = new FormattedText(uvText, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, tf, uvFontSize, new SolidColorBrush(uvColor), 1.0);

            // 进度条高度需要避让UV数字
            var uvTextHeight = uvFt.Height + 2;
            var uvBarHeight = _height - 8 - uvTextHeight;
            var uvBarX = padding;
            var uvBarY = 4;

            // 1. 绘制UV竖向进度条（左侧）
            var uvProgress = uvValue / 11.0;

            var uvBg = ParseColor(embedded.UvBarBackgroundColor, Color.FromArgb(0x80, 0x80, 0x80, 0x80));
            var uvFill = ParseColor(embedded.UvBarFillColor, Color.FromRgb(0xDA, 0x70, 0xD6));

            ctx.DrawRoundedRectangle(
                new SolidColorBrush(uvBg),
                null,
                new Rect(uvBarX, uvBarY, uvBarWidth, uvBarHeight),
                3, 3);

            // 填充（从底部向上）
            var fillHeight = uvBarHeight * uvProgress;
            if (fillHeight > 2)
            {
                ctx.DrawRoundedRectangle(
                    new SolidColorBrush(uvFill),
                    null,
                    new Rect(uvBarX, uvBarY + uvBarHeight - fillHeight, uvBarWidth, fillHeight),
                    3, 3);
            }

            // UV数字（进度条下方，居中）
            ctx.DrawText(uvFt, new Point(uvBarX + (uvBarWidth - uvFt.Width) / 2, uvBarY + uvBarHeight + 1));

            // 2. 天气图标（UV进度条右侧，应用间距配置）
            var iconX = uvBarX + uvBarWidth + uvToIconGap;
            var baseIconSize = _height - 8;
            var iconSize = (int)(baseIconSize * embedded.IconScale);
            iconSize = Math.Clamp(iconSize, 16, _height - 4);

            var icon = _iconRenderer.RenderWeatherIcon(now, embedded, iconSize);
            ctx.DrawImage(icon, new Rect(iconX, (_height - iconSize) / 2.0, iconSize, iconSize));

            // 3. 文字区域（2行：温度、湿度，Y轴居中）
            var textX = iconX + iconSize + iconToTextGap;

            var textColor = ParseColor(embedded.TemperatureColor, Colors.White);
            var humidityColor = ParseColor(embedded.HumidityColor, Colors.White);

            // 2行布局，整体垂直居中，应用行间距
            var baseFontSize = Math.Min((_height - 8) / 2.0 * 0.9, 14);
            var fontSize = baseFontSize * embedded.TemperatureFontScale;
            var humidityFontSize = baseFontSize * embedded.HumidityFontScale;

            // 计算实际文字高度
            var tempText = ApplyTemplate(embedded.TemperatureFormat, $"{Math.Round(now.TemperatureC):0}", "{value}°");
            var humidityText = ApplyTemplate(embedded.HumidityFormat, $"{now.RelativeHumidityPercent:0}", "{value}%");

            var ft1 = new FormattedText(tempText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, fontSize, new SolidColorBrush(textColor), 1.0);
            var ft2 = new FormattedText(humidityText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, humidityFontSize, new SolidColorBrush(humidityColor), 1.0);

            var totalTextHeight = ft1.Height + lineSpacing + ft2.Height;
            var textStartY = (_height - totalTextHeight) / 2.0;

            // 第1行：温度
            DrawTextWithStroke(ctx, tempText, textX, textStartY, fontSize, tf, textColor, embedded.TextStrokeWidth);

            // 第2行：湿度（应用行间距）
            DrawTextWithStroke(ctx, humidityText, textX, textStartY + ft1.Height + lineSpacing, humidityFontSize, tf, humidityColor, embedded.TextStrokeWidth);
        }
        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual); return bmp;
    }

    private static void DrawTextWithStroke(DrawingContext ctx, string text, double x, double y, double fontSize, Typeface typeface, Color color, double strokeWidth)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            1.0);

        if (strokeWidth <= 0)
        {
            ctx.DrawText(formatted, new Point(x, y));
            return;
        }

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

    private static double MeasureTextWidth(string t, Typeface tf, double sz, double dp) => new FormattedText(t, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, sz, Brushes.White, dp).WidthIncludingTrailingWhitespace;
    private static string ApplyTemplate(string t, string v, string f) => (string.IsNullOrWhiteSpace(t) ? f : t).Replace("{value}", v, StringComparison.OrdinalIgnoreCase);

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

    private void CheckHover()
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;
        try
        {
            if (!GetCursorPos(out var pt)) return;
            if (!GetWindowRect(_hwnd, out var rect)) return;
            var isInside = pt.x >= rect.Left && pt.x <= rect.Right && pt.y >= rect.Top && pt.y <= rect.Bottom;
            if (isInside != _isHovering)
            {
                _isHovering = isInside;
                _onHoverChanged?.Invoke(isInside);
            }
        }
        catch { }
    }

    public void Dispose() { _disposed = true; _updateTimer?.Dispose(); _positionTimer?.Dispose(); _hoverTimer?.Dispose(); if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd); }
}
