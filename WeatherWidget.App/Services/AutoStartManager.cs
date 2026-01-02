using Microsoft.Win32;
using System.IO;

namespace WeatherWidget.App.Services;

public static class AutoStartManager
{
    private const string AppName = "WeatherWidget";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                // 替换 .dll 为 .exe 如果需要 (但在 .NET 6+ Environment.ProcessPath 通常正确指向 exe)
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    exePath = exePath[..^4] + ".exe";
                }

                // 添加 --autostart 参数，以便程序知道它是自动启动的
                key.SetValue(AppName, $"\"{exePath}\" --autostart");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"Failed to set auto start: {ex.Message}");
        }
    }
}
