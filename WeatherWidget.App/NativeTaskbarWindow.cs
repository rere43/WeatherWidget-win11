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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
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

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_DESTROY = 0x0002;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    #endregion

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private IntPtr _hwnd;
    private IntPtr _taskbarHwnd;
    private WndProcDelegate? _wndProc;
    private System.Threading.Timer? _updateTimer;
    private byte[]? _bitmapBuffer;
    private int _width = 120;
    private int _height = 40;
    private bool _isEmbedded;
    private bool _disposed;

    public NativeTaskbarWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer)
    {
        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;
    }

    public bool TryCreate()
    {
        try
        {
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

            // 直接以任务栏为父窗口创建子窗口
            var x = taskbarRect.Width - _width - 200;
            var y = (taskbarRect.Height - _height) / 2;
            AppLogger.Info($"NativeTaskbarWindow: Creating at x={x}, y={y}, size={_width}x{_height}");

            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW,
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

            // 定时更新
            _updateTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(Invalidate);
            }, null, 0, 5000);

            _panelViewModel.WeatherUpdated += (_, _) => Application.Current?.Dispatcher?.BeginInvoke(Invalidate);

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
        if (_hwnd != IntPtr.Zero)
        {
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                OnPaint(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnPaint(IntPtr hWnd)
    {
        AppLogger.Info("NativeTaskbarWindow: OnPaint called");
        var hdc = BeginPaint(hWnd, out var ps);
        try
        {
            var snapshot = _panelViewModel.Snapshot;
            if (snapshot is null)
            {
                AppLogger.Info("NativeTaskbarWindow: OnPaint - snapshot is null");
                return;
            }

            // 用 WPF 渲染内容到位图
            var bitmap = RenderContent(snapshot.Now, _panelViewModel.Settings);
            if (bitmap is not BitmapSource bmp)
            {
                AppLogger.Info("NativeTaskbarWindow: OnPaint - bitmap is null");
                return;
            }

            // 转换为 DIB 并绘制
            DrawBitmapToHdc(hdc, bmp, 0, 0);
            AppLogger.Info("NativeTaskbarWindow: OnPaint - drawn successfully");
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
        using (var ctx = visual.RenderOpen())
        {
            // 半透明背景
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                null,
                new Rect(0, 0, _width, _height));

            // 天气图标
            var iconSize = _height - 8;
            var artProvider = new WeatherArtProvider();
            var weatherArt = artProvider.RenderBaseArt(now.WeatherCode, iconSize);
            ctx.DrawImage(weatherArt, new Rect(4, 4, iconSize, iconSize));

            // 温度文字
            var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(settings.BadgeFontFamily) ? "Segoe UI" : settings.BadgeFontFamily);
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            var tempText = $"{Math.Round(now.TemperatureC):0}°";
            var formatted = new FormattedText(
                tempText,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                _height * 0.45,
                Brushes.White,
                1.0);

            var textX = iconSize + 8;
            var textY = (_height - formatted.Height) / 2;
            ctx.DrawText(formatted, new Point(textX, textY));
        }

        var bmp = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private void DrawBitmapToHdc(IntPtr hdc, BitmapSource bmp, int x, int y)
    {
        var width = bmp.PixelWidth;
        var height = bmp.PixelHeight;
        var stride = width * 4;

        if (_bitmapBuffer == null || _bitmapBuffer.Length != height * stride)
            _bitmapBuffer = new byte[height * stride];

        bmp.CopyPixels(_bitmapBuffer, stride, 0);

        // 创建 DIB
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

        var hdcMem = CreateCompatibleDC(hdc);
        var hBitmap = CreateDIBSection(hdcMem, ref bmi, 0, out var bits, IntPtr.Zero, 0);

        if (hBitmap != IntPtr.Zero && bits != IntPtr.Zero)
        {
            Marshal.Copy(_bitmapBuffer, 0, bits, _bitmapBuffer.Length);
            var oldBmp = SelectObject(hdcMem, hBitmap);
            BitBlt(hdc, x, y, width, height, hdcMem, 0, 0, SRCCOPY);
            SelectObject(hdcMem, oldBmp);
            DeleteObject(hBitmap);
        }

        DeleteDC(hdcMem);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer?.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
