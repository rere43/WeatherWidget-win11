using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

/// <summary>
/// 基于 SetParent 方案的任务栏子窗口嵌入实现
/// 策略：先创建 Popup 窗口，再通过 SetParent 强制转为任务栏子窗口
/// 优势：不需要 TopMost 保活，不遮挡右键菜单，自然跟随任务栏
/// </summary>
public sealed class ChildTaskbarWindow : IDisposable
{
    #region Win32 API

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;

    private const int GWL_STYLE = -16;
    private const int GWL_HWNDPARENT = -8;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_LBUTTONUP = 0x0202;

    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private readonly Action<bool>? _onHoverChanged;

    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private IntPtr _notifyWndHwnd;
    private WndProcDelegate? _wndProc; // 防止 GC 回收
    private WinEventDelegate? _winEventProc; // 防止 GC 回收
    private IntPtr _hWinEventHook;
    private bool _disposed;
    private bool _isHovering;
    private bool _classRegistered;

    private System.Threading.Timer? _hoverTimer;
    private System.Threading.Timer? _positionDebounceTimer;

    private byte[]? _bitmapBuffer;
    private int _width = 150, _height = 40;
    private int _lastX = int.MinValue, _lastY = int.MinValue;
    private uint _msgTaskbarCreated;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int FullscreenTolerancePx = 2;

    public IntPtr Handle => _hwnd;
    public bool IsFullscreen => IsTaskbarHidden() || IsFullscreenAppRunning();

    public ChildTaskbarWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer, Action<bool>? onHoverChanged = null)
    {
        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;
        _onHoverChanged = onHoverChanged;

        _msgTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _panelViewModel.WeatherUpdated += OnWeatherUpdated;
    }

    private void OnWeatherUpdated(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                AdjustPosition();
                Invalidate();
            }
        });
    }

    public bool TryCreate()
    {
        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd)) return true;

        try
        {
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero) return false;

            // 注册窗口类
            const string className = "WeatherWidgetChild";
            if (!_classRegistered)
            {
                _wndProc = new WndProcDelegate(WndProc);
                var wndClass = new WNDCLASSW
                {
                    style = 0,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = GetModuleHandle(null),
                    lpszClassName = className
                };

                if (RegisterClassW(ref wndClass) != 0) _classRegistered = true;
                else if (Marshal.GetLastWin32Error() == 1410) _classRegistered = true; // 类已存在
            }

            if (!_classRegistered) return false;

            // 策略 F-SetParent:
            // 1. 创建 Popup 窗口
            _hwnd = CreateWindowExW(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
                className,
                "WeatherWidget",
                WS_POPUP | WS_VISIBLE, // 初始为 Popup
                0, 0, _width, _height,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero) return false;

            // 2. 修改样式为 Child
            var style = GetWindowLongPtrSafe(_hwnd, GWL_STYLE);
            style = (IntPtr)(((long)style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS);
            SetWindowLongPtrSafe(_hwnd, GWL_STYLE, style);

            // 3. SetParent 挂载到任务栏
            SetParent(_hwnd, _taskbarHwnd);

            // 设置 Hook 监听任务栏位置变化 (限制在任务栏进程以减少开销)
            GetWindowThreadProcessId(_taskbarHwnd, out var pid);
            _winEventProc = new WinEventDelegate(WinEventProc);
            _hWinEventHook = SetWinEventHook(
                EVENT_OBJECT_LOCATIONCHANGE,
                EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _winEventProc,
                pid, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // 初始化位置防抖定时器
            _positionDebounceTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() => AdjustPosition());
            }, null, Timeout.Infinite, Timeout.Infinite);

            // 启动定时器 (仅保留悬停检测)
            _hoverTimer ??= new System.Threading.Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(CheckHover), null, 500, 100);

            // 初始布局与渲染
            AdjustPosition();
            Invalidate();

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"[ERROR] ChildTaskbarWindow create failed: {ex.Message}");
            return false;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (_disposed) return;
            // 检查是否是任务栏或通知区在移动
            if (hwnd == _taskbarHwnd || hwnd == _notifyWndHwnd || _notifyWndHwnd == IntPtr.Zero)
            {
                _positionDebounceTimer?.Change(50, Timeout.Infinite);
            }
        }
    }

    private void AdjustPosition()
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;

        // 如果父窗口无效了（Explorer 重启），重新查找
        if (!IsWindow(_taskbarHwnd))
        {
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd != IntPtr.Zero)
            {
                SetParent(_hwnd, _taskbarHwnd);
            }
            else return;
        }

        if (!GetWindowRect(_taskbarHwnd, out var rt) || !GetClientRect(_taskbarHwnd, out var rc)) return;

        UpdateDesiredLayout(rc);

        // 计算位置：从通知区左侧开始
        _notifyWndHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);

        // 将通知区屏幕坐标转换为任务栏客户区坐标
        int notifyX;
        if (_notifyWndHwnd != IntPtr.Zero && GetWindowRect(_notifyWndHwnd, out var rr))
        {
            var pt = new POINT { x = rr.Left, y = rr.Top };
            // ScreenToClient(taskbarHwnd, ref pt) 手动计算
            notifyX = pt.x - rt.Left;
        }
        else
        {
            notifyX = rc.Width - 150; // Fallback
        }

        var offsetX = (int)Math.Round((_panelViewModel.Settings.Embedded ?? Settings.Default.Embedded).OffsetX);
        int tx = notifyX - _width - 8 + offsetX;
        int ty = rc.Top + (rc.Height - _height) / 2;

        if (tx != _lastX || ty != _lastY)
        {
            _lastX = tx;
            _lastY = ty;
            // SWP_NOZORDER: 既然是 Child，Z-order 由系统管理，我们不需要改动
            SetWindowPos(_hwnd, IntPtr.Zero, tx, ty, _width, _height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Invalidate();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST) return (IntPtr)(-1); // HTTRANSPARENT: 鼠标事件穿透给任务栏

        if (msg == _msgTaskbarCreated)
        {
            // Explorer 重启，重新挂载
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
                if (_taskbarHwnd != IntPtr.Zero) SetParent(_hwnd, _taskbarHwnd);
            });
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void UpdateDesiredLayout(RECT taskbarClientRect)
    {
        var snapshot = _panelViewModel.Snapshot; if (snapshot is null) { _width = 100; return; }
        var settings = _panelViewModel.Settings; var now = snapshot.Now;
        var embedded = settings.Embedded ?? Settings.Default.Embedded;
        var tf = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(embedded.FontFamily) ? "Segoe UI" : embedded.FontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        var basePadding = 4.0;
        var uvBarWidth = 12.0;
        var uvToIconGap = Math.Max(embedded.UvToIconGap, 2);
        var iconToTextGap = Math.Max(embedded.IconToTextGap, 2);

        var uvFontScale = embedded.UvNumberFontScale < 0.5 ? 2.0 : embedded.UvNumberFontScale;
        var uvValue = Math.Clamp(now.UvIndex ?? 0, 0, 11);
        var uvText = ApplyTemplate(embedded.UvNumberFormat, $"{uvValue:0.#}", "{value}");
        var uvFt = new FormattedText(uvText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, 8 * uvFontScale, Brushes.White, 1.0);
        var padding = Math.Max(basePadding, (uvFt.WidthIncludingTrailingWhitespace - uvBarWidth) / 2 - uvFt.OverhangLeading + 0.5);
        var baseIconSize = taskbarClientRect.Height - 8;
        var iconSize = (int)(baseIconSize * embedded.IconScale);
        iconSize = (int)Math.Clamp(iconSize, 16, taskbarClientRect.Height - 4);

        var baseFontSize = Math.Min((taskbarClientRect.Height - 8) / 2.0 * 0.9, 14);
        var tempSize = baseFontSize * embedded.TemperatureFontScale;
        var humiditySize = baseFontSize * embedded.HumidityFontScale;
        var tempText = ApplyTemplate(embedded.TemperatureFormat, $"{Math.Round(now.TemperatureC):0}", "{value}°");
        var humidityText = ApplyTemplate(embedded.HumidityFormat, $"{now.RelativeHumidityPercent:0}", "{value}%");
        var textWidth = Math.Max(MeasureTextWidth(tempText, tf, tempSize, 1.0), MeasureTextWidth(humidityText, tf, humiditySize, 1.0));

        _width = (int)Math.Ceiling(padding + uvBarWidth + uvToIconGap + iconSize + iconToTextGap + textWidth + 8);
        _height = taskbarClientRect.Height;
    }

    private void Invalidate()
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd) || _disposed) return;

        IntPtr hdcS = IntPtr.Zero, hdcM = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            var snp = _panelViewModel.Snapshot; if (snp == null) return;
            var rendered = RenderContent(snp.Now, _panelViewModel.Settings);
            if (rendered is not BitmapSource bmp) return;

            int w = bmp.PixelWidth, h = bmp.PixelHeight, s = w * 4;
            if (_bitmapBuffer == null || _bitmapBuffer.Length != h * s) _bitmapBuffer = new byte[h * s];
            bmp.CopyPixels(_bitmapBuffer, s, 0);

            var bi = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
            hdcS = GetDC(IntPtr.Zero);
            hdcM = CreateCompatibleDC(hdcS);
            hBmp = CreateDIBSection(hdcM, ref bi, 0, out var bits, IntPtr.Zero, 0);
            Marshal.Copy(_bitmapBuffer, 0, bits, _bitmapBuffer.Length);
            oldBmp = SelectObject(hdcM, hBmp);

            POINT pd = new POINT { x = _lastX, y = _lastY }, ps = new POINT { x = 0, y = 0 }; SIZE sz = new SIZE { cx = w, cy = h };
            BLENDFUNCTION bl = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(_hwnd, hdcS, ref pd, ref sz, hdcM, ref ps, 0, ref bl, 2);
        }
        catch { }
        finally
        {
            if (oldBmp != IntPtr.Zero) SelectObject(hdcM, oldBmp);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            if (hdcM != IntPtr.Zero) DeleteDC(hdcM);
            if (hdcS != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcS);
        }
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
            var uvFontScale = embedded.UvNumberFontScale < 0.5 ? 2.0 : embedded.UvNumberFontScale;

            var basePadding = 4.0;
            var uvBarWidth = 12.0;
            var uvFontSize = 8 * uvFontScale;
            var uvValue = Math.Clamp(now.UvIndex ?? 0, 0, 11);
            var uvText = ApplyTemplate(embedded.UvNumberFormat, $"{uvValue:0.#}", "{value}");
            var uvColor = ParseColor(embedded.UvNumberColor, Colors.White);
            var uvFt = new FormattedText(uvText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, uvFontSize, new SolidColorBrush(uvColor), 1.0);
            var padding = Math.Max(basePadding, (uvFt.WidthIncludingTrailingWhitespace - uvBarWidth) / 2 - uvFt.OverhangLeading + 0.5);

            var uvTextHeight = uvFt.Height + 2;
            var uvBarHeight = _height - 8 - uvTextHeight;
            var uvBarX = padding;
            var uvBarY = 4;

            var uvProgress = uvValue / 11.0;
            var uvBg = ParseColor(embedded.UvBarBackgroundColor, Color.FromArgb(0x80, 0x80, 0x80, 0x80));
            var uvFill = ParseColor(embedded.UvBarFillColor, Color.FromRgb(0xDA, 0x70, 0xD6));

            ctx.DrawRoundedRectangle(new SolidColorBrush(uvBg), null, new Rect(uvBarX, uvBarY, uvBarWidth, uvBarHeight), 3, 3);
            var fillHeight = uvBarHeight * uvProgress;
            if (fillHeight > 2) ctx.DrawRoundedRectangle(new SolidColorBrush(uvFill), null, new Rect(uvBarX, uvBarY + uvBarHeight - fillHeight, uvBarWidth, fillHeight), 3, 3);

            var uvTextX = uvBarX + (uvBarWidth - uvFt.WidthIncludingTrailingWhitespace) / 2;
            ctx.DrawText(uvFt, new Point(uvTextX, uvBarY + uvBarHeight + 1));

            var iconX = uvBarX + uvBarWidth + uvToIconGap;
            var baseIconSize = _height - 8;
            var iconSize = (int)(baseIconSize * embedded.IconScale);
            iconSize = (int)Math.Clamp(iconSize, 16, _height - 4);
            var icon = _iconRenderer.RenderWeatherIcon(now, embedded, iconSize);
            ctx.DrawImage(icon, new Rect(iconX, (_height - iconSize) / 2.0, iconSize, iconSize));

            var textX = iconX + iconSize + iconToTextGap;
            var textColor = ParseColor(embedded.TemperatureColor, Colors.White);
            var humidityColor = ParseColor(embedded.HumidityColor, Colors.White);
            var baseFontSize = Math.Min((_height - 8) / 2.0 * 0.9, 14);
            var fontSize = baseFontSize * embedded.TemperatureFontScale;
            var humidityFontSize = baseFontSize * embedded.HumidityFontScale;
            var tempText = ApplyTemplate(embedded.TemperatureFormat, $"{Math.Round(now.TemperatureC):0}", "{value}°");
            var humidityText = ApplyTemplate(embedded.HumidityFormat, $"{now.RelativeHumidityPercent:0}", "{value}%");

            var ft1 = new FormattedText(tempText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, fontSize, new SolidColorBrush(textColor), 1.0);
            var ft2 = new FormattedText(humidityText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, humidityFontSize, new SolidColorBrush(humidityColor), 1.0);
            var totalTextHeight = ft1.Height + lineSpacing + ft2.Height;
            var textStartY = (_height - totalTextHeight) / 2.0;

            DrawTextWithStroke(ctx, tempText, textX, textStartY, fontSize, tf, textColor, embedded.TextStrokeWidth);
            DrawTextWithStroke(ctx, humidityText, textX, textStartY + ft1.Height + lineSpacing, humidityFontSize, tf, humidityColor, embedded.TextStrokeWidth);
        }

        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual); return bmp;
    }

    private static void DrawTextWithStroke(DrawingContext ctx, string text, double x, double y, double fontSize, Typeface typeface, Color color, double strokeWidth)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, new SolidColorBrush(color), 1.0);
        if (strokeWidth <= 0) { ctx.DrawText(formatted, new Point(x, y)); return; }
        var textGeo = formatted.BuildGeometry(new Point(x, y));
        var strokePen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)), strokeWidth) { LineJoin = PenLineJoin.Round };
        ctx.DrawGeometry(null, strokePen, textGeo);
        ctx.DrawGeometry(new SolidColorBrush(color), null, textGeo);
    }

    private static double MeasureTextWidth(string t, Typeface tf, double sz, double dp) => new FormattedText(t, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, sz, Brushes.White, dp).WidthIncludingTrailingWhitespace;
    private static string ApplyTemplate(string t, string v, string f) => (string.IsNullOrWhiteSpace(t) ? f : t).Replace("{value}", v, StringComparison.OrdinalIgnoreCase);
    private static Color ParseColor(string hex, Color defaultColor) { try { return string.IsNullOrWhiteSpace(hex) ? defaultColor : (Color)ColorConverter.ConvertFromString(hex); } catch { return defaultColor; } }

    private bool IsTaskbarHidden() => _taskbarHwnd != IntPtr.Zero && IsWindow(_taskbarHwnd) && !IsWindowVisible(_taskbarHwnd);

    private bool IsFullscreenAppRunning()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd || foreground == _taskbarHwnd || foreground == _notifyWndHwnd)
        {
            return false;
        }

        var className = new StringBuilder(256);
        _ = GetClassName(foreground, className, className.Capacity);
        var windowClass = className.ToString();
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "TrayNotifyWnd")
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo) || !GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        return windowRect.Left <= monitorInfo.rcMonitor.Left + FullscreenTolerancePx &&
               windowRect.Top <= monitorInfo.rcMonitor.Top + FullscreenTolerancePx &&
               windowRect.Right >= monitorInfo.rcMonitor.Right - FullscreenTolerancePx &&
               windowRect.Bottom >= monitorInfo.rcMonitor.Bottom - FullscreenTolerancePx;
    }

    private void CheckHover()
    {
        if (_hwnd == IntPtr.Zero || _disposed) return;
        try
        {
            if (IsFullscreen)
            {
                if (_isHovering)
                {
                    _isHovering = false;
                    _onHoverChanged?.Invoke(false);
                }
                return;
            }

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

    private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);
    private static void SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong) { if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, dwNewLong); else SetWindowLong32(hWnd, nIndex, (int)dwNewLong); }

    public void Dispose()
    {
        _disposed = true;
        _panelViewModel.WeatherUpdated -= OnWeatherUpdated;
        if (_hWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hWinEventHook);
            _hWinEventHook = IntPtr.Zero;
        }
        _hoverTimer?.Dispose();
        _positionDebounceTimer?.Dispose();
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }
}
