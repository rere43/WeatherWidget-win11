using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WeatherWidget.App.Services;

/// <summary>
/// 任务栏嵌入方案实验：同时创建多个测试窗口，验证不同方法的可行性
/// </summary>
public sealed class TaskbarEmbedExperiment : IDisposable
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

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    // 窗口样式常量
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GWL_HWNDPARENT = -8;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const uint WM_PAINT = 0x000F;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_NCHITTEST = 0x0084;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    #endregion

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();

    /// <summary>测试窗口信息</summary>
    public record TestWindowInfo(
        string Name,
        string Strategy,
        IntPtr Hwnd,
        IntPtr ParentHwnd,
        string ParentClass,
        uint Color,
        int X, int Y, int Width, int Height);

    private readonly List<TestWindowInfo> _windows = [];
    private readonly List<WndProcDelegate> _wndProcs = []; // 防止 GC 回收
    private readonly Dictionary<IntPtr, uint> _hwndColors = [];
    private bool _disposed;
    private int _classCounter;

    /// <summary>获取所有测试窗口信息</summary>
    public IReadOnlyList<TestWindowInfo> Windows => _windows;

    /// <summary>运行所有实验并返回结果</summary>
    public string RunAllExperiments()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=" + new string('=', 60));
        sb.AppendLine("任务栏嵌入方案实验");
        sb.AppendLine("=" + new string('=', 60));
        sb.AppendLine();

        // 获取任务栏信息
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            sb.AppendLine("[错误] 找不到任务栏 Shell_TrayWnd");
            return sb.ToString();
        }

        GetWindowRect(taskbar, out var taskbarRect);
        sb.AppendLine($"任务栏: Shell_TrayWnd = 0x{taskbar:X}");
        sb.AppendLine($"  位置: ({taskbarRect.Left}, {taskbarRect.Top}) - ({taskbarRect.Right}, {taskbarRect.Bottom})");
        sb.AppendLine($"  尺寸: {taskbarRect.Width} x {taskbarRect.Height}");
        sb.AppendLine();

        // 枚举任务栏子窗口
        sb.AppendLine("任务栏子窗口结构:");
        EnumerateChildren(taskbar, sb, 1);
        sb.AppendLine();

        // 查找可能的父窗口
        var trayNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var reBar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        var xamlBridge = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);

        sb.AppendLine("候选父窗口:");
        sb.AppendLine($"  Shell_TrayWnd:     0x{taskbar:X}");
        sb.AppendLine($"  TrayNotifyWnd:     0x{trayNotify:X}");
        sb.AppendLine($"  ReBarWindow32:     0x{reBar:X}");
        sb.AppendLine($"  XAML Bridge:       0x{xamlBridge:X}");
        sb.AppendLine();

        // 确定定位基准（通知区左侧）
        var notifyX = 0;
        if (trayNotify != IntPtr.Zero && GetWindowRect(trayNotify, out var tr))
        {
            notifyX = tr.Left;
        }
        else
        {
            notifyX = taskbarRect.Right - 150;
        }

        // 计算测试窗口的 Y 位置（任务栏中间）
        var testHeight = 30;
        var testWidth = 60;
        var gap = 10;
        var totalWidth = (testWidth + gap) * 7;
        var startX = notifyX - totalWidth - 20; // 留出一点空隙
        var baseY = taskbarRect.Top + (taskbarRect.Height - testHeight) / 2;

        sb.AppendLine($"定位基准: 通区左侧 X={notifyX}, 起始 X={startX}");

        // 定义实验方案
        var experiments = new List<(string Name, string Strategy, Func<int, IntPtr, (IntPtr hwnd, IntPtr parent, string parentClass)> Create, uint Color)>
        {
            // 方案 1: 现有方案 - TOPMOST Popup + Owner
            ("A-TOPMOST", "WS_POPUP + TOPMOST + Owner=Taskbar", (x, _) =>
            {
                var hwnd = CreateTestWindow($"Test_A_{_classCounter++}", WS_POPUP | WS_VISIBLE,
                    WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                    x, baseY, testWidth, testHeight, IntPtr.Zero, 0xFF0000); // 红色
                // 设置 owner
                SetWindowLongPtrSafe(hwnd, GWL_HWNDPARENT, taskbar);
                return (hwnd, taskbar, "Shell_TrayWnd(owner)");
            }, 0xFF0000),

            // 方案 2: 子窗口 - Shell_TrayWnd
            ("B-Child-Tray", "WS_CHILD of Shell_TrayWnd", (x, _) =>
            {
                // 转换屏幕坐标到客户区坐标
                var pt = new POINT { X = x, Y = baseY };
                ScreenToClient(taskbar, ref pt);
                var hwnd = CreateTestWindow($"Test_B_{_classCounter++}", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    WS_EX_NOACTIVATE,
                    pt.X, pt.Y, testWidth, testHeight, taskbar, 0x00FF00); // 绿色
                return (hwnd, taskbar, "Shell_TrayWnd");
            }, 0x00FF00),

            // 方案 3: 子窗口 - TrayNotifyWnd
            ("C-Child-Notify", "WS_CHILD of TrayNotifyWnd", (x, _) =>
            {
                if (trayNotify == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero, "N/A");
                GetWindowRect(trayNotify, out var nr);
                var pt = new POINT { X = x, Y = baseY };
                ScreenToClient(trayNotify, ref pt);
                var hwnd = CreateTestWindow($"Test_C_{_classCounter++}", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    WS_EX_NOACTIVATE,
                    pt.X, pt.Y, testWidth, testHeight, trayNotify, 0x0000FF); // 蓝色
                return (hwnd, trayNotify, "TrayNotifyWnd");
            }, 0x0000FF),

            // 方案 4: 子窗口 - ReBarWindow32 (Win10)
            ("D-Child-ReBar", "WS_CHILD of ReBarWindow32", (x, _) =>
            {
                if (reBar == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero, "N/A");
                var pt = new POINT { X = x, Y = baseY };
                ScreenToClient(reBar, ref pt);
                var hwnd = CreateTestWindow($"Test_D_{_classCounter++}", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    WS_EX_NOACTIVATE,
                    pt.X, pt.Y, testWidth, testHeight, reBar, 0xFFFF00); // 黄色
                return (hwnd, reBar, "ReBarWindow32");
            }, 0xFFFF00),

            // 方案 5: 子窗口 - XAML Bridge (Win11)
            ("E-Child-XAML", "WS_CHILD of XAML Bridge", (x, _) =>
            {
                if (xamlBridge == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero, "N/A");
                var pt = new POINT { X = x, Y = baseY };
                ScreenToClient(xamlBridge, ref pt);
                var hwnd = CreateTestWindow($"Test_E_{_classCounter++}", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    WS_EX_NOACTIVATE,
                    pt.X, pt.Y, testWidth, testHeight, xamlBridge, 0xFF00FF); // 紫色
                return (hwnd, xamlBridge, "XAML Bridge");
            }, 0xFF00FF),

            // 方案 6: Popup + 后转 Child (SetParent)
            ("F-SetParent", "WS_POPUP -> SetParent(Taskbar)", (x, _) =>
            {
                var hwnd = CreateTestWindow($"Test_F_{_classCounter++}", WS_POPUP | WS_VISIBLE,
                    WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    x, baseY, testWidth, testHeight, IntPtr.Zero, 0x00FFFF); // 青色
                // 修改样式为 CHILD
                var style = GetWindowLongPtrSafe(hwnd, GWL_STYLE);
                style = (IntPtr)(((long)style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS);
                SetWindowLongPtrSafe(hwnd, GWL_STYLE, style);
                // SetParent
                SetParent(hwnd, taskbar);
                // 转换坐标
                var pt = new POINT { X = x, Y = baseY };
                ScreenToClient(taskbar, ref pt);
                SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, testWidth, testHeight, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return (hwnd, taskbar, "Shell_TrayWnd(SetParent)");
            }, 0x00FFFF),

            // 方案 7: 非 TOPMOST Popup + 定时提升
            ("G-Popup-Top", "WS_POPUP + HWND_TOP (no TOPMOST)", (x, _) =>
            {
                var hwnd = CreateTestWindow($"Test_G_{_classCounter++}", WS_POPUP | WS_VISIBLE,
                    WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    x, baseY, testWidth, testHeight, IntPtr.Zero, 0xFFA500); // 橙色
                SetWindowLongPtrSafe(hwnd, GWL_HWNDPARENT, taskbar);
                // 非 TOPMOST，仅 TOP
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return (hwnd, taskbar, "Shell_TrayWnd(owner,no-topmost)");
            }, 0xFFA500),
        };

        // 执行实验
        sb.AppendLine("实验结果:");
        sb.AppendLine("-" + new string('-', 60));

        foreach (var (i, exp) in experiments.Select((e, i) => (i, e)))
        {
            var x = startX + i * (testWidth + gap);
            try
            {
                var (hwnd, parent, parentClass) = exp.Create(x, taskbar);
                var success = hwnd != IntPtr.Zero && IsWindow(hwnd);
                var visible = success && IsWindowVisible(hwnd);

                var info = new TestWindowInfo(
                    exp.Name, exp.Strategy, hwnd, parent, parentClass, exp.Color,
                    x, baseY, testWidth, testHeight);
                _windows.Add(info);

                sb.AppendLine($"[{exp.Name}] {(success ? "OK" : "FAIL")} {(visible ? "可见" : "不可见")}");
                sb.AppendLine($"  策略: {exp.Strategy}");
                sb.AppendLine($"  HWND: 0x{hwnd:X}, 父窗口: {parentClass} (0x{parent:X})");
                if (!success && hwnd == IntPtr.Zero)
                {
                    sb.AppendLine($"  错误码: {GetLastError()}");
                }
                if (success)
                {
                    GetWindowRect(hwnd, out var wr);
                    sb.AppendLine($"  实际位置: ({wr.Left}, {wr.Top}) {wr.Width}x{wr.Height}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[{exp.Name}] 异常: {ex.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("-" + new string('-', 60));
        sb.AppendLine("颜色图例:");
        sb.AppendLine("  红色=A-TOPMOST  绿色=B-Child-Tray  蓝色=C-Child-Notify");
        sb.AppendLine("  黄色=D-Child-ReBar  紫色=E-Child-XAML  青色=F-SetParent");
        sb.AppendLine("  橙色=G-Popup-Top");
        sb.AppendLine();
        sb.AppendLine("请观察任务栏上哪些颜色方块可见，然后：");
        sb.AppendLine("  1. 悬停任务栏图标触发预览缩略图");
        sb.AppendLine("  2. 右键通知区图标弹出菜单");
        sb.AppendLine("  3. 观察哪些方块消失/被遮挡/位置错误");
        sb.AppendLine();
        sb.AppendLine("调用 Dispose() 或 DestroyAllWindows() 清理测试窗口");

        return sb.ToString();
    }

    /// <summary>刷新所有窗口状态</summary>
    public string GetStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("当前状态:");
        foreach (var w in _windows)
        {
            var exists = IsWindow(w.Hwnd);
            var visible = exists && IsWindowVisible(w.Hwnd);
            var actualParent = exists ? GetParent(w.Hwnd) : IntPtr.Zero;
            GetWindowRect(w.Hwnd, out var rect);

            sb.AppendLine($"[{w.Name}] {(exists ? "存在" : "已销毁")} {(visible ? "可见" : "不可见")}");
            if (exists)
            {
                sb.AppendLine($"  当前父窗口: 0x{actualParent:X}");
                sb.AppendLine($"  当前位置: ({rect.Left}, {rect.Top}) {rect.Width}x{rect.Height}");
            }
        }
        return sb.ToString();
    }

    /// <summary>提升所有 TOPMOST/TOP 窗口</summary>
    public void BringAllToTop()
    {
        foreach (var w in _windows)
        {
            if (!IsWindow(w.Hwnd)) continue;
            if (w.Strategy.Contains("TOPMOST"))
            {
                SetWindowPos(w.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else if (w.Strategy.Contains("Popup"))
            {
                SetWindowPos(w.Hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
    }

    /// <summary>销毁所有测试窗口</summary>
    public void DestroyAllWindows()
    {
        foreach (var w in _windows)
        {
            if (IsWindow(w.Hwnd))
            {
                DestroyWindow(w.Hwnd);
            }
        }
        _windows.Clear();
        _hwndColors.Clear();
    }

    private static bool _classRegistered;
    private static WndProcDelegate? _sharedWndProc;
    private static Dictionary<IntPtr, uint>? _sharedColors;

    private IntPtr CreateTestWindow(string className, uint style, uint exStyle, int x, int y, int w, int h, IntPtr parent, uint color)
    {
        // 使用共享的窗口类
        const string sharedClassName = "WeatherWidgetExperiment";

        if (!_classRegistered)
        {
            _sharedColors = _hwndColors;
            _sharedWndProc = (hwnd, msg, wParam, lParam) =>
            {
                if (msg == WM_PAINT)
                {
                    var hdc = BeginPaint(hwnd, out var ps);
                    if (_sharedColors != null && _sharedColors.TryGetValue(hwnd, out var c))
                    {
                        var brush = CreateSolidBrush(c);
                        FillRect(hdc, ref ps.rcPaint, brush);
                        DeleteObject(brush);
                    }
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                }
                if (msg == WM_NCHITTEST)
                {
                    return (IntPtr)(-1); // HTTRANSPARENT
                }
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            };
            _wndProcs.Add(_sharedWndProc); // 防止 GC 回收

            var wndClass = new WNDCLASSW
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_sharedWndProc),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = sharedClassName
            };

            var atom = RegisterClassW(ref wndClass);
            if (atom != 0)
            {
                _classRegistered = true;
            }
            else
            {
                var regError = GetLastError();
                // 错误码 1410 = 类已存在，也算成功
                if (regError == 1410)
                {
                    _classRegistered = true;
                }
                System.Diagnostics.Debug.WriteLine($"RegisterClassW failed: error={regError}");
            }
        }

        if (!_classRegistered)
        {
            return IntPtr.Zero;
        }

        var hwnd = CreateWindowExW(exStyle, sharedClassName, className, style, x, y, w, h, parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        var lastError = GetLastError();
        if (hwnd != IntPtr.Zero)
        {
            _hwndColors[hwnd] = color;
            InvalidateRect(hwnd, IntPtr.Zero, true);
        }
        System.Diagnostics.Debug.WriteLine($"CreateTestWindow: class={sharedClassName}, style=0x{style:X}, exStyle=0x{exStyle:X}, pos=({x},{y}), size=({w},{h}), parent=0x{parent:X}, hwnd=0x{hwnd:X}, error={lastError}");
        return hwnd;
    }

    private static void EnumerateChildren(IntPtr parent, StringBuilder sb, int depth)
    {
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var className = new StringBuilder(256);
            GetClassName(child, className, 256);
            GetWindowRect(child, out var rect);
            var visible = IsWindowVisible(child) ? "" : " [隐藏]";

            sb.AppendLine($"{new string(' ', depth * 2)}- {className} (0x{child:X}) {rect.Width}x{rect.Height}{visible}");

            // 递归子窗口（限制深度）
            if (depth < 3)
            {
                EnumerateChildren(child, sb, depth + 1);
            }

            child = FindWindowEx(parent, child, null, null);
        }
    }

    private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);
    }

    private static void SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyAllWindows();
    }
}
