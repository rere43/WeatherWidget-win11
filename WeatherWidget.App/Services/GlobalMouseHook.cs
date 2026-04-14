using System.Runtime.InteropServices;

namespace WeatherWidget.App.Services;

/// <summary>
/// 全局鼠标钩子：用于检测"点击面板外部"并自动隐藏面板。
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    private IntPtr _hookHandle;
    private HookProc? _hookProc;

    /// <summary>鼠标按钮类型。</summary>
    public enum MouseButton
    {
        Left,
        Right,
        Middle,
        XButton
    }

    /// <summary>鼠标按下事件参数。</summary>
    public readonly record struct MouseDownEventArgs(int X, int Y, MouseButton Button);

    public event EventHandler<MouseDownEventArgs>? MouseDown;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                MouseButton? button = msg switch
                {
                    WM_LBUTTONDOWN => MouseButton.Left,
                    WM_RBUTTONDOWN => MouseButton.Right,
                    WM_MBUTTONDOWN => MouseButton.Middle,
                    WM_XBUTTONDOWN => MouseButton.XButton,
                    _ => null
                };

                if (button.HasValue)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    MouseDown?.Invoke(this, new MouseDownEventArgs(info.pt.X, info.pt.Y, button.Value));
                }
            }
        }
        catch
        {
            // 钩子回调里不抛异常，避免影响系统输入链路
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

