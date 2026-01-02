using System.Runtime.InteropServices;

namespace WeatherWidget.App.Services;

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private readonly IntPtr _hWnd;
    private readonly int _id;

    public event EventHandler? Pressed;

    public GlobalHotkey(IntPtr hWnd, int id, uint modifiers, uint virtualKey)
    {
        _hWnd = hWnd;
        _id = id;

        if (!RegisterHotKey(_hWnd, _id, modifiers, virtualKey))
        {
            throw new InvalidOperationException($"RegisterHotKey failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public bool TryHandleMessage(int msg, IntPtr wParam)
    {
        if (msg != WM_HOTKEY)
        {
            return false;
        }

        if (wParam.ToInt32() != _id)
        {
            return false;
        }

        Pressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        UnregisterHotKey(_hWnd, _id);
    }

    public static (uint Modifiers, uint VirtualKey) CtrlAlt(char key)
    {
        var vk = (uint)char.ToUpperInvariant(key);
        return (MOD_CONTROL | MOD_ALT, vk);
    }

    public static (uint Modifiers, uint VirtualKey) CtrlAltShift(char key)
    {
        var vk = (uint)char.ToUpperInvariant(key);
        return (MOD_CONTROL | MOD_ALT | MOD_SHIFT, vk);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
