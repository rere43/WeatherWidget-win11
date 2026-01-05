using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WeatherWidget.App.Services;
using WeatherWidget.App.Models;
using WeatherWidget.App.UI;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

public partial class App : Application
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    private SecondaryIconWindow? _secondaryIconWindow;
    private TaskbarEmbedWindow? _embedWindow;
    private NativeTaskbarWindow? _nativeTaskbarWindow;
    private DllTaskbarEmbed? _dllTaskbarEmbed;
    private GlobalMouseHook? _globalMouseHook;
    private IntPtr _foregroundEventHook;
    private WinEventDelegate? _foregroundEventProc;
    private IntPtr _panelShownForegroundHwnd;
    private IntPtr _panelHwnd;
    private readonly uint _processId = (uint)Environment.ProcessId;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 设置固定的 AppUserModelID，确保任务栏固定后图标能正确关联并动态更新
        _ = SetCurrentProcessExplicitAppUserModelID("MyWeatherWidget.App.v1");

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeatherWidget");

        Directory.CreateDirectory(appDataRoot);
        AppLogger.Init(appDataRoot);
        AppLogger.Info("App Startup");
        AppLogger.Info($"BaseDir: {AppContext.BaseDirectory}");
        AppLogger.Info($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");

        var settingsStore = new SettingsStore(Path.Combine(appDataRoot, "settings.json"));
        var settings = settingsStore.LoadOrCreateDefault();

        var weatherRepository = new WeatherRepository(
            new OpenMeteoClient(),
            new WeatherCacheStore(Path.Combine(appDataRoot, "cache.json")));

        var clothingAdvisor = new ClothingAdvisor();
        var geocodingClient = new GeocodingClient();

        var panelViewModel = new PanelViewModel(settingsStore, settings, weatherRepository, clothingAdvisor, geocodingClient);
        var panelWindow = new PanelWindow { DataContext = panelViewModel };

        _globalMouseHook = new GlobalMouseHook();
        _foregroundEventProc = (_, eventType, hwnd, _, _, _, _) =>
        {
            if (eventType != EVENT_SYSTEM_FOREGROUND)
            {
                return;
            }

            if (!panelWindow.IsVisible || hwnd == IntPtr.Zero)
            {
                return;
            }

            // 前台切到面板自身不处理
            if (_panelHwnd != IntPtr.Zero && hwnd == _panelHwnd)
            {
                return;
            }

            // 仍停留在面板弹出时的前台窗口也不处理
            if (_panelShownForegroundHwnd != IntPtr.Zero && hwnd == _panelShownForegroundHwnd)
            {
                return;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == _processId)
            {
                return;
            }

            panelWindow.Dispatcher.BeginInvoke(() =>
            {
                if (panelWindow.IsVisible)
                {
                    panelWindow.Hide();
                }
            });
        };

        // 点击面板外部时自动隐藏（解决悬停触发后未激活导致 Deactivated 不触发的问题）
        _globalMouseHook.MouseDown += (_, pt) =>
        {
            if (!panelWindow.IsVisible)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(panelWindow).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc))
            {
                return;
            }

            var isInside = pt.X >= rc.Left && pt.X <= rc.Right && pt.Y >= rc.Top && pt.Y <= rc.Bottom;
            if (isInside)
            {
                return;
            }

            panelWindow.Dispatcher.BeginInvoke(() =>
            {
                if (panelWindow.IsVisible)
                {
                    panelWindow.Hide();
                }
            });
        };

        panelWindow.IsVisibleChanged += (_, __) =>
        {
            if (_globalMouseHook is null)
            {
                return;
            }

            try
            {
                if (panelWindow.IsVisible)
                {
                    _globalMouseHook.Start();

                    _panelHwnd = new WindowInteropHelper(panelWindow).Handle;
                    _panelShownForegroundHwnd = GetForegroundWindow();

                    if (_foregroundEventHook == IntPtr.Zero && _foregroundEventProc is not null)
                    {
                        _foregroundEventHook = SetWinEventHook(
                            EVENT_SYSTEM_FOREGROUND,
                            EVENT_SYSTEM_FOREGROUND,
                            IntPtr.Zero,
                            _foregroundEventProc,
                            0,
                            0,
                            WINEVENT_OUTOFCONTEXT);
                        if (_foregroundEventHook == IntPtr.Zero)
                        {
                            AppLogger.Info("SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed");
                        }
                    }
                }
                else
                {
                    _globalMouseHook.Stop();

                    if (_foregroundEventHook != IntPtr.Zero)
                    {
                        _ = UnhookWinEvent(_foregroundEventHook);
                        _foregroundEventHook = IntPtr.Zero;
                    }

                    _panelShownForegroundHwnd = IntPtr.Zero;
                    _panelHwnd = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Info($"GlobalMouseHook failed: {ex.Message}");
            }
        };

        // 初始化主题
        var themeManager = new ThemeManager();
        themeManager.ApplyTheme(settings.ThemeMode);

        // 监听主题模式变化
        panelViewModel.ThemeModeChanged += (_, _) => themeManager.ApplyTheme(panelViewModel.ThemeMode);

        var iconRenderer = new IconRenderer(new WeatherArtProvider());

        void CreateEmbeddedModeWindows()
        {
            // 悬停回调：显示面板并确保获得焦点
            void OnHoverChanged(bool isHovering)
            {
                if (isHovering && !panelWindow.IsVisible)
                {
                    try
                    {
                        if (GetCursorPos(out var pt))
                        {
                            var dpi = VisualTreeHelper.GetDpi(MainWindow ?? panelWindow);
                            var anchor = TaskbarAnchor.GetTaskbarAnchorNearPointPx(
                                panelWindow.Width,
                                panelWindow.Height,
                                dpi.DpiScaleX,
                                dpi.DpiScaleY,
                                pt.X,
                                pt.Y);
                            panelWindow.Left = anchor.Left;
                            panelWindow.Top = anchor.Top;
                        }
                    }
                    catch { }

                    panelWindow.Show();
                    panelWindow.Activate();

                    // 强制将窗口设置为前台窗口
                    var hwnd = new WindowInteropHelper(panelWindow).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ForceForegroundWindow(hwnd);
                    }
                }
            }

            // 方案1：NativeTaskbarWindow (纯 C# 实现，已修复 Win11 遮挡问题)
            _nativeTaskbarWindow = new NativeTaskbarWindow(panelViewModel, iconRenderer, OnHoverChanged);
            if (_nativeTaskbarWindow.TryCreate())
            {
                AppLogger.Info("NativeTaskbarWindow created for embedded mode");
                return;
            }

            _nativeTaskbarWindow.Dispose();
            _nativeTaskbarWindow = null;

            // 方案2：DLL 分层窗口实现（透明/鼠标穿透）
            _dllTaskbarEmbed = new DllTaskbarEmbed(panelViewModel, iconRenderer, OnHoverChanged);
            if (_dllTaskbarEmbed.TryCreate())
            {
                AppLogger.Info("DllTaskbarEmbed created for embedded mode");
                return;
            }

            _dllTaskbarEmbed.Dispose();
            _dllTaskbarEmbed = null;

            // 回退方案3：WPF 悬浮窗口
            _embedWindow = new TaskbarEmbedWindow(panelViewModel, iconRenderer);
            _embedWindow.Show();
            AppLogger.Info("TaskbarEmbedWindow created for embedded mode (fallback 2)");
        }

        // 先创建并显示主窗口（天气图标），确保图标在左边
        var hostWindow = new MainWindow(panelWindow, panelViewModel, iconRenderer, settingsStore);
        MainWindow = hostWindow;

        // 必须调用 Show() 让 WPF 消息循环正常运行
        // 嵌入模式下 MainWindow 构造函数已设置 ShowInTaskbar=false 和 Visibility=Collapsed
        hostWindow.Show();

        // 如果启用双图标模式，创建第二个图标窗口（文字在右边）
        if (settings.IconDisplayMode == IconDisplayMode.Separate)
        {
            _secondaryIconWindow = new SecondaryIconWindow(panelViewModel, iconRenderer);
            _secondaryIconWindow.Show();
            AppLogger.Info("SecondaryIconWindow created for dual icon mode");
        }
        else if (settings.IconDisplayMode == IconDisplayMode.Embedded)
        {
            CreateEmbeddedModeWindows();
        }

        // 监听图标模式变化
        panelViewModel.IconDisplayModeChanged += (_, _) =>
        {
            // 先关闭现有的副窗口
            if (_secondaryIconWindow != null)
            {
                _secondaryIconWindow.Close();
                _secondaryIconWindow = null;
                AppLogger.Info("SecondaryIconWindow closed");
            }
            if (_embedWindow != null)
            {
                _embedWindow.Close();
                _embedWindow = null;
                AppLogger.Info("TaskbarEmbedWindow closed");
            }
            if (_nativeTaskbarWindow != null)
            {
                _nativeTaskbarWindow.Dispose();
                _nativeTaskbarWindow = null;
                AppLogger.Info("NativeTaskbarWindow closed");
            }
            if (_dllTaskbarEmbed != null)
            {
                _dllTaskbarEmbed.Dispose();
                _dllTaskbarEmbed = null;
                AppLogger.Info("DllTaskbarEmbed closed");
            }

            // 根据新模式创建对应窗口
            if (panelViewModel.IconDisplayMode == IconDisplayMode.Separate)
            {
                _secondaryIconWindow = new SecondaryIconWindow(panelViewModel, iconRenderer);
                _secondaryIconWindow.Show();
                AppLogger.Info("SecondaryIconWindow created dynamically");
            }
            else if (panelViewModel.IconDisplayMode == IconDisplayMode.Embedded)
            {
                CreateEmbeddedModeWindows();
            }
        };
    }

    /// <summary>
    /// 强制将窗口设置为前台窗口（绕过 Windows 的前台窗口限制）
    /// </summary>
    private static void ForceForegroundWindow(IntPtr hwnd)
    {
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundHwnd, out _);
        var currentThreadId = GetCurrentThreadId();

        if (foregroundThreadId != currentThreadId)
        {
            // 附加到前台窗口的线程，这样我们就可以设置前台窗口
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _globalMouseHook?.Dispose();
            if (_foregroundEventHook != IntPtr.Zero)
            {
                _ = UnhookWinEvent(_foregroundEventHook);
                _foregroundEventHook = IntPtr.Zero;
            }
        }
        catch { }

        base.OnExit(e);
    }
}
