using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

/// <summary>
/// 使用原生 Win32 窗口嵌入任务栏，绕过 WPF 窗口的限制。
/// </summary>
public sealed class NativeTaskbarWindow : IDisposable
{
    #region Win32 API

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")]
    private static extern bool UpdateLayeredWindowOptional(
        IntPtr hwnd,
        IntPtr hdcDst,
        IntPtr pptDst,
        IntPtr psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("msimg32.dll", SetLastError = true)]
    private static extern bool AlphaBlend(
        IntPtr hdcDest, int xoriginDest, int yoriginDest, int wDest, int hDest,
        IntPtr hdcSrc, int xoriginSrc, int yoriginSrc, int wSrc, int hSrc,
        BLENDFUNCTION blendFunction);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const uint WM_SHOWWINDOW = 0x0018;
    private const uint WM_WINDOWPOSCHANGING = 0x0046;
    private const uint WM_WINDOWPOSCHANGED = 0x0047;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint ULW_ALPHA = 0x00000002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private const int DWMWA_CLOAKED = 14;

    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private IntPtr _trayNotifyHwnd;
    private IntPtr _startButtonHwnd;
    private WndProcDelegate? _wndProc;
    private System.Threading.Timer? _updateTimer;
    private System.Threading.Timer? _positionTimer;
    private byte[]? _bitmapBuffer;
    private int _width = 120;
    private int _height = 40;
    private bool _isEmbedded;
    private bool _disposed;
    private bool _eventSubscribed;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastHeight = int.MinValue;
    private int _lastWidth = int.MinValue;
    private int _contentIconSize = 32;
    private string _contentTempText = string.Empty;
    private string? _contentCornerText;
    private string? _contentExtraText;
    private int _contentGapAfterIcon = 8;
    private int _contentGapAfterTemp = 8;
    private int _contentGapAfterCorner = 8;
    private bool? _traceLastVisible;
    private int? _traceLastCloaked;

    public NativeTaskbarWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer)
    {
        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;
    }

    public bool TryCreate()
    {
        try
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
            {
                return true;
            }

            // 找任务栏
            _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero)
            {
                AppLogger.Info("NativeTaskbarWindow: Shell_TrayWnd not found");
                return false;
            }

            // 获取任务栏尺寸
            if (!GetClientRect(_taskbarHwnd, out var taskbarRect))
            {
                AppLogger.Info("NativeTaskbarWindow: GetClientRect failed");
                return false;
            }

            _height = taskbarRect.Height > 0 ? taskbarRect.Height : 40;
            AppLogger.Info($"NativeTaskbarWindow: Taskbar rect={taskbarRect.Width}x{taskbarRect.Height}");

            // 注册窗口类
            _wndProc = WndProc;
            var wndClass = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = "WeatherWidgetTaskbar",
                hbrBackground = IntPtr.Zero,
            };

            var atom = RegisterClass(ref wndClass);
            if (atom == 0)
            {
                var err = Marshal.GetLastWin32Error();
                // 1410 = 类已存在，可以继续
                if (err != 1410)
                {
                    AppLogger.Info($"NativeTaskbarWindow: RegisterClass failed, error={err}");
                    return false;
                }
            }

            // 直接以任务栏为父窗口创建子窗口（位置后续会由 AdjustPosition() 计算）
            var x = 0;
            var y = 0;
            AppLogger.Info($"NativeTaskbarWindow: Creating at x={x}, y={y}, size={_width}x{_height}");

            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED,
                "WeatherWidgetTaskbar",
                "WeatherWidget",
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                x,
                y,
                _width,
                _height,
                _taskbarHwnd,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                AppLogger.Info($"NativeTaskbarWindow: CreateWindowEx failed, error={err}");
                return false;
            }

            _isEmbedded = true;
            AppLogger.Info($"NativeTaskbarWindow: Created hwnd=0x{_hwnd:X}");
            TraceWindowState("created");

            UpdateTaskbarAnchors();
            AdjustPosition(forceAdjust: true);
            Invalidate();

            // 定时更新
            _updateTimer ??= new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd))
                    {
                        TryCreate();
                        return;
                    }

                    Invalidate();
                });
            }, null, 0, 5000);

            // 定时调整位置（类似 TrafficMonitor：按任务栏/托盘变化纠偏，但不做“强制置顶/可见性抢救”）
            _positionTimer ??= new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd))
                    {
                        TryCreate();
                        return;
                    }

                    AdjustPosition(forceAdjust: false);
                    TraceWindowState("positionTimer");
                });
            }, null, 0, 1000);

            if (!_eventSubscribed)
            {
                _eventSubscribed = true;
                _panelViewModel.WeatherUpdated += (_, _) => Application.Current?.Dispatcher?.BeginInvoke(() => AdjustPosition(forceAdjust: true));
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Info($"NativeTaskbarWindow: TryCreate failed: {ex.Message}");
            return false;
        }
    }

    private void Invalidate()
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd))
        {
            return;
        }

        try
        {
            UpdateLayeredVisual();
        }
        catch (Exception ex)
        {
            AppLogger.Info($"NativeTaskbarWindow: Invalidate failed: {ex.Message}");
        }
    }

    private void UpdateTaskbarAnchors()
    {
        if (_taskbarHwnd == IntPtr.Zero)
        {
            return;
        }

        // Win11/Win10：系统托盘区域
        _trayNotifyHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        // Win11：开始按钮（用于左侧对齐场景；这里只做句柄缓存）
        _startButtonHwnd = FindWindowEx(_taskbarHwnd, IntPtr.Zero, "Start", null);
    }

    private void UpdateDesiredLayout(in RECT taskbarClientRect)
    {
        var snapshot = _panelViewModel.Snapshot;
        if (snapshot is null)
        {
            _contentTempText = string.Empty;
            _contentCornerText = null;
            _contentExtraText = null;
            _contentIconSize = Math.Clamp(taskbarClientRect.Height - 8, 16, 64);
            _width = Math.Max(120, _contentIconSize + 12);
            return;
        }

        var now = snapshot.Now;
        var settings = _panelViewModel.Settings;

        // 图标大小（可配置缩放）
        var iconScale = double.IsFinite(settings.EmbeddedIconScale) ? settings.EmbeddedIconScale : 1.0;
        iconScale = Math.Clamp(iconScale, 0.5, 1.6);
        _contentIconSize = (int)Math.Round(Math.Clamp((taskbarClientRect.Height - 8) * iconScale, 16, taskbarClientRect.Height));

        // 文本：复用三组角标配置（格式/字号/颜色/间隔）
        var tempValue = $"{Math.Round(now.TemperatureC):0}";
        _contentTempText = ApplyTemplate(settings.TempBadgeFormat, tempValue, "{value}°");

        _contentCornerText = settings.IconCornerMetric switch
        {
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

        _contentExtraText = null;
        if (settings.ExtraBadgeEnabled && !string.IsNullOrWhiteSpace(settings.ExtraBadgeFormat))
        {
            var extraText = ApplyMultiTemplate(settings.ExtraBadgeFormat, now);
            if (!string.IsNullOrWhiteSpace(extraText))
            {
                _contentExtraText = extraText;
            }
        }

        // 间隔：复用角标 OffsetX（X=间隔微调）
        const int baseGapPx = 8;
        _contentGapAfterIcon = Math.Max(0, baseGapPx + (int)Math.Round(settings.TempBadgeOffsetX));
        _contentGapAfterTemp = Math.Max(0, baseGapPx + (int)Math.Round(settings.CornerBadgeOffsetX));
        _contentGapAfterCorner = Math.Max(0, baseGapPx + (int)Math.Round(settings.ExtraBadgeOffsetX));

        // 计算需要的宽度，避免字号变大后被裁剪
        var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.BadgeFontFamily) ? "Segoe UI" : settings.BadgeFontFamily);
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double baseFontSize;
        double rowLimit;
        if (settings.EmbeddedTextLayout == EmbeddedTextLayout.ThreeLines)
        {
            baseFontSize = taskbarClientRect.Height / 3.0 * 0.82;
            rowLimit = taskbarClientRect.Height / 3.0 * 0.98;
        }
        else
        {
            baseFontSize = taskbarClientRect.Height * 0.45;
            rowLimit = double.PositiveInfinity;
        }

        var tempFontSize = Math.Min(baseFontSize * Math.Clamp(settings.TempBadgeFontScale, 0.5, 3.0), rowLimit);
        var cornerFontSize = Math.Min(baseFontSize * Math.Clamp(settings.CornerBadgeFontScale, 0.5, 3.0), rowLimit);
        var extraFontSize = Math.Min(baseFontSize * Math.Clamp(settings.ExtraBadgeFontScale, 0.5, 3.0), rowLimit);

        var pixelsPerDip = GetPixelsPerDip();
        var tempWidth = MeasureTextWidth(_contentTempText, typeface, tempFontSize, pixelsPerDip);
        var cornerWidth = string.IsNullOrWhiteSpace(_contentCornerText) ? 0 : MeasureTextWidth(_contentCornerText!, typeface, cornerFontSize, pixelsPerDip);
        var extraWidth = string.IsNullOrWhiteSpace(_contentExtraText) ? 0 : MeasureTextWidth(_contentExtraText!, typeface, extraFontSize, pixelsPerDip);

        const int paddingLeft = 4;
        const int paddingRight = 4;

        var width = paddingLeft + _contentIconSize;
        if (settings.EmbeddedTextLayout == EmbeddedTextLayout.ThreeLines)
        {
            // 三行：宽度以最长一行决定
            var maxLineWidth = Math.Max(tempWidth, Math.Max(cornerWidth, extraWidth));
            width += _contentGapAfterIcon + (int)Math.Ceiling(maxLineWidth) + paddingRight;
        }
        else
        {
            // 单行：横向累加
            if (!string.IsNullOrWhiteSpace(_contentTempText))
            {
                width += _contentGapAfterIcon + (int)Math.Ceiling(tempWidth);
            }

            if (!string.IsNullOrWhiteSpace(_contentCornerText))
            {
                width += _contentGapAfterTemp + (int)Math.Ceiling(cornerWidth);
            }

            if (!string.IsNullOrWhiteSpace(_contentExtraText))
            {
                width += _contentGapAfterCorner + (int)Math.Ceiling(extraWidth);
            }

            width += paddingRight;
        }
        width = Math.Clamp(width, 80, Math.Max(80, taskbarClientRect.Width));

        _width = width;
    }

    private void AdjustPosition(bool forceAdjust)
    {
        if (_hwnd == IntPtr.Zero || !_isEmbedded || _disposed)
        {
            return;
        }

        try
        {
            // Explorer 重启/任务栏重建后，句柄可能失效：尽量自愈
            if (_taskbarHwnd == IntPtr.Zero || !IsWindow(_taskbarHwnd))
            {
                _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
                if (_taskbarHwnd == IntPtr.Zero)
                {
                    return;
                }
                UpdateTaskbarAnchors();
                forceAdjust = true;
            }

            if (_trayNotifyHwnd != IntPtr.Zero && !IsWindow(_trayNotifyHwnd))
            {
                _trayNotifyHwnd = IntPtr.Zero;
            }

            if (_startButtonHwnd != IntPtr.Zero && !IsWindow(_startButtonHwnd))
            {
                _startButtonHwnd = IntPtr.Zero;
            }

            if (_trayNotifyHwnd == IntPtr.Zero || _startButtonHwnd == IntPtr.Zero)
            {
                UpdateTaskbarAnchors();
            }

            if (!GetClientRect(_taskbarHwnd, out var taskbarClientRect) || taskbarClientRect.Width <= 0 || taskbarClientRect.Height <= 0)
            {
                return;
            }

            if (!GetWindowRect(_taskbarHwnd, out var taskbarScreenRect))
            {
                return;
            }

            // Win11 某些设备/版本下 Shell_TrayWnd 的 client 高度会大于可见任务栏条带高度（例如触屏/无图标时）。
            // 参考 TrafficMonitor：用 Start/TrayNotifyWnd 的矩形推算“真实条带”的 top/height，避免窗口跑到任务栏外面。
            var bandTopY = 0;
            var bandHeight = taskbarClientRect.Height;
            if (_startButtonHwnd != IntPtr.Zero && GetWindowRect(_startButtonHwnd, out var startRect) && startRect.Height > 0)
            {
                bandTopY = Math.Max(0, startRect.Top - taskbarScreenRect.Top);
                bandHeight = Math.Min(taskbarClientRect.Height, startRect.Height);
            }
            else if (_trayNotifyHwnd != IntPtr.Zero && GetWindowRect(_trayNotifyHwnd, out var trayRect) && trayRect.Height > 0)
            {
                bandTopY = Math.Max(0, trayRect.Top - taskbarScreenRect.Top);
                bandHeight = Math.Min(taskbarClientRect.Height, trayRect.Height);
            }

            if (bandHeight <= 0)
            {
                bandHeight = _height;
            }

            var layoutRect = taskbarClientRect;
            layoutRect.Bottom = layoutRect.Top + bandHeight;
            UpdateDesiredLayout(layoutRect);

            // 高度贴合可见条带高度（避免被裁剪或跑出任务栏）
            var targetHeight = bandHeight;

            // 计算通知区左边界（相对 taskbar client）
            var notifyLeftX = 0;
            if (_trayNotifyHwnd != IntPtr.Zero && GetWindowRect(_trayNotifyHwnd, out var trayScreenRect) && trayScreenRect.Width > 0)
            {
                notifyLeftX = trayScreenRect.Left - taskbarScreenRect.Left;
            }
            else
            {
                // Win11 某些情况下拿不到 TrayNotifyWnd（尤其副屏），参考 TrafficMonitor 用一个保底“时钟区域宽度”
                const int fallbackRightReservePx = 88;
                notifyLeftX = taskbarClientRect.Width - fallbackRightReservePx;
            }

            const int marginPx = 2;
            var targetX = notifyLeftX - _width + marginPx;
            var offsetX = double.IsFinite(_panelViewModel.Settings.EmbeddedOffsetX) ? _panelViewModel.Settings.EmbeddedOffsetX : 0;
            targetX += (int)Math.Round(offsetX);
            if (targetX < marginPx)
            {
                targetX = marginPx;
            }

            var targetY = bandTopY + (bandHeight - targetHeight) / 2;
            if (targetY < 0)
            {
                targetY = 0;
            }

            var screenX = taskbarScreenRect.Left + targetX;
            var screenY = taskbarScreenRect.Top + targetY;

            var shouldUpdateVisual = forceAdjust || targetHeight != _lastHeight || _width != _lastWidth;

            if (!forceAdjust && screenX == _lastX && screenY == _lastY && targetHeight == _lastHeight && _width == _lastWidth)
            {
                return;
            }

            _height = targetHeight;
            _lastX = screenX;
            _lastY = screenY;
            _lastHeight = targetHeight;
            _lastWidth = _width;

            var flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
            // 子窗口：坐标以父窗口 client 为基准；默认不频繁改 Z-order，避免抖动
            var insertAfter = IntPtr.Zero; // HWND_TOP
            if (!forceAdjust)
            {
                flags |= SWP_NOZORDER;
            }

            SetWindowPos(_hwnd, insertAfter, targetX, targetY, _width, targetHeight, flags);

            if (shouldUpdateVisual)
            {
                Invalidate();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"NativeTaskbarWindow: AdjustPosition failed: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SHOWWINDOW:
                TraceWindowState($"WM_SHOWWINDOW wParam={wParam}");
                break;

            case WM_PAINT:
                OnPaint(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                return IntPtr.Zero;

            case WM_ERASEBKGND:
                // 不擦除背景，避免把透明区域刷成黑块
                return (IntPtr)1;

            // 让鼠标穿透，避免抢占任务栏交互
            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;

            case WM_MOUSEACTIVATE:
                return (IntPtr)MA_NOACTIVATE;

            case WM_WINDOWPOSCHANGING:
                // 任务栏显示预览缩略图/切换窗口时，Explorer 可能会对嵌入窗口做隐藏或调整 Z-order，导致“闪一下/消失”。
                // 这里尽量拦截隐藏，避免靠高频 SetWindowPos “保活”造成体验抖动。
                try
                {
                    var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                    var changed = false;

                    if ((pos.flags & SWP_HIDEWINDOW) != 0)
                    {
                        pos.flags &= ~SWP_HIDEWINDOW;
                        changed = true;
                    }

                    if ((pos.flags & SWP_NOZORDER) == 0 && pos.hwndInsertAfter != IntPtr.Zero)
                    {
                        pos.hwndInsertAfter = IntPtr.Zero; // HWND_TOP
                        changed = true;
                    }

                    if (changed)
                    {
                        Marshal.StructureToPtr(pos, lParam, false);
                        TraceWindowState("WM_WINDOWPOSCHANGING");
                    }
                }
                catch
                {
                    // ignored
                }

                break;

            case WM_WINDOWPOSCHANGED:
                TraceWindowState("WM_WINDOWPOSCHANGED");
                break;

            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
            case WM_LBUTTONUP:
            case WM_RBUTTONUP:
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnPaint(IntPtr hWnd)
    {
        BeginPaint(hWnd, out var ps);
        try
        {
            // layered window 由 UpdateLayeredWindow 更新像素；这里只负责验证重绘区域
        }
        catch (Exception ex)
        {
            AppLogger.Info($"NativeTaskbarWindow: OnPaint failed: {ex.Message}");
        }
        finally
        {
            EndPaint(hWnd, ref ps);
        }
    }

    private ImageSource RenderContent(WeatherNow now, Settings settings)
    {
        var visual = new DrawingVisual();
        ConfigureVisualQuality(visual);
        using (var ctx = visual.RenderOpen())
        {
            // 透明背景：不绘制底色（由 layered window 让任务栏背景透出）
            // 图标
            var iconSize = Math.Clamp(_contentIconSize, 16, Math.Max(16, _height));
            var icon = _iconRenderer.RenderWeatherOnlyIcon(now, settings, iconSize) as BitmapSource;
            if (icon is not null)
            {
                var iconY = (_height - iconSize) / 2;
                ctx.DrawImage(icon, new Rect(4, iconY, iconSize, iconSize));
            }

            // 文本（复用三组角标配置：字号/颜色/间隔/格式）
            var pixelsPerDip = GetPixelsPerDip();
            var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.BadgeFontFamily) ? "Segoe UI" : settings.BadgeFontFamily);
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            if (settings.EmbeddedTextLayout == EmbeddedTextLayout.ThreeLines)
            {
                var baseFontSize = _height / 3.0 * 0.82;
                var tempFontSize = Math.Min(baseFontSize * settings.TempBadgeFontScale, _height / 3.0 * 0.98);
                var cornerFontSize = Math.Min(baseFontSize * settings.CornerBadgeFontScale, _height / 3.0 * 0.98);
                var extraFontSize = Math.Min(baseFontSize * settings.ExtraBadgeFontScale, _height / 3.0 * 0.98);

                var tempText = CreateFormattedText(_contentTempText, typeface, tempFontSize, ParseColor(settings.TempBadgeColor), pixelsPerDip);
                var cornerLine = string.IsNullOrWhiteSpace(_contentCornerText) ? string.Empty : _contentCornerText!;
                var extraLine = string.IsNullOrWhiteSpace(_contentExtraText) ? string.Empty : _contentExtraText!;
                var cornerText = CreateFormattedText(string.IsNullOrWhiteSpace(cornerLine) ? " " : cornerLine, typeface, cornerFontSize, ParseColor(settings.CornerBadgeColor), pixelsPerDip);
                var extraText = CreateFormattedText(string.IsNullOrWhiteSpace(extraLine) ? " " : extraLine, typeface, extraFontSize, ParseColor(settings.ExtraBadgeColor), pixelsPerDip);

                var paddingLeft = 4 + iconSize + _contentGapAfterIcon;
                var paddingRight = 4;
                var textAreaWidth = Math.Max(1, _width - paddingLeft - paddingRight);

                var rowHeight = _height / 3.0;
                DrawAlignedLine(ctx, tempText, paddingLeft, textAreaWidth, rowTopY: 0 * rowHeight, rowHeight: rowHeight, alignment: settings.EmbeddedTextAlignment);
                if (!string.IsNullOrWhiteSpace(cornerLine))
                {
                    DrawAlignedLine(ctx, cornerText, paddingLeft, textAreaWidth, rowTopY: 1 * rowHeight, rowHeight: rowHeight, alignment: settings.EmbeddedTextAlignment);
                }
                if (!string.IsNullOrWhiteSpace(extraLine))
                {
                    DrawAlignedLine(ctx, extraText, paddingLeft, textAreaWidth, rowTopY: 2 * rowHeight, rowHeight: rowHeight, alignment: settings.EmbeddedTextAlignment);
                }
            }
            else
            {
                // 单行：横向排布，统一基线
                var baseFontSize = _height * 0.45;

                FormattedText? tempText = null;
                FormattedText? cornerText = null;
                FormattedText? extraText = null;

                var segments = new List<FormattedText>(capacity: 3);

                if (!string.IsNullOrWhiteSpace(_contentTempText))
                {
                    tempText = CreateFormattedText(_contentTempText, typeface, baseFontSize * settings.TempBadgeFontScale, ParseColor(settings.TempBadgeColor), pixelsPerDip);
                    segments.Add(tempText);
                }

                if (!string.IsNullOrWhiteSpace(_contentCornerText))
                {
                    cornerText = CreateFormattedText(_contentCornerText!, typeface, baseFontSize * settings.CornerBadgeFontScale, ParseColor(settings.CornerBadgeColor), pixelsPerDip);
                    segments.Add(cornerText);
                }

                if (!string.IsNullOrWhiteSpace(_contentExtraText))
                {
                    extraText = CreateFormattedText(_contentExtraText!, typeface, baseFontSize * settings.ExtraBadgeFontScale, ParseColor(settings.ExtraBadgeColor), pixelsPerDip);
                    segments.Add(extraText);
                }

                var baselineY = GetBaselineY(_height, segments);

                var paddingLeft = 4 + iconSize + _contentGapAfterIcon;
                var paddingRight = 4;

                var totalTextWidth = 0.0;
                if (tempText is not null)
                {
                    totalTextWidth += tempText.WidthIncludingTrailingWhitespace;
                }
                if (cornerText is not null)
                {
                    totalTextWidth += _contentGapAfterTemp + cornerText.WidthIncludingTrailingWhitespace;
                }
                if (extraText is not null)
                {
                    totalTextWidth += _contentGapAfterCorner + extraText.WidthIncludingTrailingWhitespace;
                }

                var textAreaWidth = Math.Max(1, _width - paddingLeft - paddingRight);
                var startX = settings.EmbeddedTextAlignment switch
                {
                    EmbeddedTextAlignment.Center => paddingLeft + (textAreaWidth - totalTextWidth) / 2,
                    EmbeddedTextAlignment.Right => paddingLeft + (textAreaWidth - totalTextWidth),
                    _ => paddingLeft,
                };
                if (startX < paddingLeft)
                {
                    startX = paddingLeft;
                }

                var x = (int)Math.Round(startX);
                if (tempText is not null)
                {
                    DrawTextOnBaseline(ctx, tempText, x, baselineY);
                    x = AdvanceX(tempText, x);
                }

                if (cornerText is not null)
                {
                    x += _contentGapAfterTemp;
                    DrawTextOnBaseline(ctx, cornerText, x, baselineY);
                    x = AdvanceX(cornerText, x);
                }

                if (extraText is not null)
                {
                    x += _contentGapAfterCorner;
                    DrawTextOnBaseline(ctx, extraText, x, baselineY);
                }
            }
        }

        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static double GetPixelsPerDip()
    {
        try
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow is not null)
            {
                return VisualTreeHelper.GetDpi(mainWindow).PixelsPerDip;
            }
        }
        catch
        {
            // ignored
        }

        return 1.0;
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        fontSize = Math.Max(6, fontSize);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            pixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static FormattedText CreateFormattedText(string text, Typeface typeface, double fontSize, Color color, double pixelsPerDip)
    {
        fontSize = Math.Max(6, fontSize);
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            pixelsPerDip);
    }

    private static int AdvanceX(FormattedText formatted, int x)
    {
        return x + (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    private static double GetLineAscent(IReadOnlyList<FormattedText> segments)
    {
        var ascent = 0.0;
        foreach (var s in segments)
        {
            ascent = Math.Max(ascent, s.Baseline);
        }
        return ascent;
    }

    private static double GetLineDescent(IReadOnlyList<FormattedText> segments)
    {
        var descent = 0.0;
        foreach (var s in segments)
        {
            descent = Math.Max(descent, s.Height - s.Baseline);
        }
        return descent;
    }

    private static double GetBaselineY(int height, IReadOnlyList<FormattedText> segments)
    {
        if (segments.Count == 0)
        {
            return height / 2.0;
        }

        var ascent = GetLineAscent(segments);
        var descent = GetLineDescent(segments);
        var lineHeight = ascent + descent;
        return (height - lineHeight) / 2 + ascent;
    }

    private static void DrawTextOnBaseline(DrawingContext ctx, FormattedText formatted, int x, double baselineY)
    {
        var topY = baselineY - formatted.Baseline;
        ctx.DrawText(formatted, new Point(x, topY));
    }

    private static void DrawAlignedLine(
        DrawingContext ctx,
        FormattedText formatted,
        int areaLeft,
        int areaWidth,
        double rowTopY,
        double rowHeight,
        EmbeddedTextAlignment alignment)
    {
        var width = formatted.WidthIncludingTrailingWhitespace;
        double x = alignment switch
        {
            EmbeddedTextAlignment.Center => areaLeft + (areaWidth - width) / 2,
            EmbeddedTextAlignment.Right => areaLeft + (areaWidth - width),
            _ => areaLeft,
        };

        if (x < areaLeft)
        {
            x = areaLeft;
        }

        var y = rowTopY + (rowHeight - formatted.Height) / 2;
        ctx.DrawText(formatted, new Point(x, y));
    }

    private static void ConfigureVisualQuality(Visual visual)
    {
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
    }

    private int DrawInlineText(DrawingContext ctx, string text, Typeface typeface, double fontSize, Color color, int x)
    {
        var formatted = CreateFormattedText(text, typeface, fontSize, color, GetPixelsPerDip());

        // 垂直居中
        var textY = (_height - formatted.Height) / 2;
        ctx.DrawText(formatted, new Point(x, textY));
        return AdvanceX(formatted, x);
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

    private void TraceWindowState(string reason)
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd))
        {
            return;
        }

        var visible = false;
        try
        {
            visible = IsWindowVisible(_hwnd);
        }
        catch
        {
            // ignore
        }

        var cloaked = -1;
        try
        {
            if (DwmGetWindowAttribute(_hwnd, DWMWA_CLOAKED, out var v, sizeof(int)) == 0)
            {
                cloaked = v;
            }
        }
        catch
        {
            // ignore
        }

        if (_traceLastVisible == visible && _traceLastCloaked == cloaked)
        {
            return;
        }

        _traceLastVisible = visible;
        _traceLastCloaked = cloaked;
        AppLogger.Info($"NativeTaskbarWindow: state changed ({reason}) visible={visible} cloaked=0x{cloaked:X}");
    }

    private void DrawBitmapToHdc(IntPtr hdc, BitmapSource bmp, int x, int y)
    {
        // obsolete: moved to UpdateLayeredVisual()
    }

    private void UpdateLayeredVisual()
    {
        var snapshot = _panelViewModel.Snapshot;
        var settings = _panelViewModel.Settings;

        BitmapSource bmp;
        if (snapshot is null)
        {
            // 没有数据时，用“全透明帧”清空（避免残留）
            var emptyVisual = new DrawingVisual();
            ConfigureVisualQuality(emptyVisual);
            using (emptyVisual.RenderOpen())
            {
            }

            var empty = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
            empty.Render(emptyVisual);
            empty.Freeze();
            bmp = empty;
        }
        else
        {
            var rendered = RenderContent(snapshot.Now, settings);
            if (rendered is not BitmapSource renderedBmp)
            {
                return;
            }

            bmp = renderedBmp;
        }

        var width = bmp.PixelWidth;
        var height = bmp.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var stride = width * 4;
        if (_bitmapBuffer == null || _bitmapBuffer.Length != height * stride)
        {
            _bitmapBuffer = new byte[height * stride];
        }

        bmp.CopyPixels(_bitmapBuffer, stride, 0);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            }
        };

        var hdcDstOwner = _hwnd;
        var hdcDst = GetDC(_hwnd);
        if (hdcDst == IntPtr.Zero)
        {
            hdcDstOwner = IntPtr.Zero;
            hdcDst = GetDC(IntPtr.Zero);
        }

        if (hdcDst == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var hdcMem = CreateCompatibleDC(hdcDst);
            var hBitmap = CreateDIBSection(hdcMem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                DeleteDC(hdcMem);
                return;
            }

            try
            {
                Marshal.Copy(_bitmapBuffer, 0, bits, _bitmapBuffer.Length);
                var oldBmp = SelectObject(hdcMem, hBitmap);
                try
                {
                    var ptSrc = new POINT { x = 0, y = 0 };
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AC_SRC_ALPHA,
                    };

                    // 子窗口模式下不要在 UpdateLayeredWindow 里“顺带移动窗口”，避免坐标体系差异导致窗口跑偏/不可见。
                    // 位置与大小统一由 SetWindowPos/MoveWindow 负责（参考 TrafficMonitor 的调用方式：pptDst=nullptr）。
                    if (!UpdateLayeredWindowOptional(_hwnd, hdcDst, IntPtr.Zero, IntPtr.Zero, hdcMem, ref ptSrc, 0, ref blend, ULW_ALPHA))
                    {
                        var err = Marshal.GetLastWin32Error();
                        AppLogger.Info($"NativeTaskbarWindow: UpdateLayeredWindow failed, error={err}");
                    }
                }
                finally
                {
                    SelectObject(hdcMem, oldBmp);
                }
            }
            finally
            {
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
            }
        }
        finally
        {
            ReleaseDC(hdcDstOwner, hdcDst);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer?.Dispose();
        _positionTimer?.Dispose();
        _updateTimer = null;
        _positionTimer = null;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
