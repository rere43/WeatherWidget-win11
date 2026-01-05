using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private IntPtr _embeddedTriggerHwnd;

    private enum PanelOpenSource
    {
        None = 0,
        Hover = 1,
        Click = 2,
    }

    private PanelOpenSource _panelOpenSource;
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

        var embeddedIsHoveringNow = false;
        var embeddedIsPinned = false;
        var embeddedPinDurationMs = 0;
        var embeddedPinStartedAt = DateTimeOffset.MinValue;

        var embeddedHoverDelayTimer = new DispatcherTimer();
        var embeddedPinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        void ResetEmbeddedPin()
        {
            embeddedIsPinned = false;
            embeddedPinDurationMs = 0;
            embeddedPinStartedAt = DateTimeOffset.MinValue;

            panelViewModel.IsHoverPinProgressVisible = false;
            panelViewModel.HoverPinProgressPercent = 0;

            embeddedPinTimer.Stop();
        }

        void StartEmbeddedPinCountdown()
        {
            ResetEmbeddedPin();

            embeddedPinDurationMs = Math.Clamp(panelViewModel.Settings.EmbeddedHoverPinMs, 0, 5000);
            if (embeddedPinDurationMs <= 0)
            {
                embeddedIsPinned = true;
                return;
            }

            embeddedPinStartedAt = DateTimeOffset.Now;
            panelViewModel.IsHoverPinProgressVisible = true;
            panelViewModel.HoverPinProgressPercent = 0;
            embeddedPinTimer.Start();
        }

        void ShowPanelByHover()
        {
            if (!embeddedIsHoveringNow || panelWindow.IsVisible)
            {
                return;
            }

            _panelOpenSource = PanelOpenSource.Hover;
            StartEmbeddedPinCountdown();

            TryPositionPanelToCursor();

            // 悬停触发：不抢焦点（避免打断当前应用输入）
            panelWindow.ShowActivated = false;
            panelWindow.Show();
        }

        embeddedHoverDelayTimer.Tick += (_, __) =>
        {
            embeddedHoverDelayTimer.Stop();
            ShowPanelByHover();
        };

        embeddedPinTimer.Tick += (_, __) =>
        {
            if (!panelWindow.IsVisible || _panelOpenSource != PanelOpenSource.Hover || embeddedPinDurationMs <= 0)
            {
                ResetEmbeddedPin();
                return;
            }

            var elapsedMs = (DateTimeOffset.Now - embeddedPinStartedAt).TotalMilliseconds;
            var progress = Math.Clamp(elapsedMs / embeddedPinDurationMs, 0, 1);
            panelViewModel.HoverPinProgressPercent = progress * 100;

            if (progress >= 1)
            {
                embeddedIsPinned = true;
                panelViewModel.HoverPinProgressPercent = 100;
                panelViewModel.IsHoverPinProgressVisible = false;
                embeddedPinTimer.Stop();
            }
        };

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

        void UpdateGlobalMouseHookState()
        {
            if (_globalMouseHook is null)
            {
                return;
            }

            var shouldRun = panelWindow.IsVisible || panelViewModel.IconDisplayMode == IconDisplayMode.Embedded;
            try
            {
                if (shouldRun)
                {
                    _globalMouseHook.Start();
                }
                else
                {
                    _globalMouseHook.Stop();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Info($"GlobalMouseHook failed: {ex.Message}");
            }
        }

        // 点击面板外部时自动隐藏（解决悬停触发后未激活导致 Deactivated 不触发的问题）
        _globalMouseHook.MouseDown += (_, pt) =>
        {
            // 1) 嵌入模式：单击触发区域切换/固定面板（不拦截系统点击，只做观察）
            if (panelViewModel.IconDisplayMode == IconDisplayMode.Embedded &&
                _embeddedTriggerHwnd != IntPtr.Zero &&
                GetWindowRect(_embeddedTriggerHwnd, out var triggerRc))
            {
                var isInTrigger = pt.X >= triggerRc.Left && pt.X <= triggerRc.Right && pt.Y >= triggerRc.Top && pt.Y <= triggerRc.Bottom;
                if (isInTrigger)
                {
                    panelWindow.Dispatcher.BeginInvoke(() =>
                    {
                        embeddedHoverDelayTimer.Stop();
                        ResetEmbeddedPin();

                        if (panelWindow.IsVisible)
                        {
                            panelWindow.Hide();
                            return;
                        }

                        _panelOpenSource = PanelOpenSource.Click;
                        panelWindow.AutoHideOnDeactivated = true;
                        panelWindow.ShowActivated = true;

                        TryPositionPanelToCursor();
                        panelWindow.Show();
                        panelWindow.Activate();

                        var hwnd2 = new WindowInteropHelper(panelWindow).Handle;
                        if (hwnd2 != IntPtr.Zero)
                        {
                            ForceForegroundWindow(hwnd2);
                        }
                    });
                    return;
                }
            }

            // 2) 点击面板外部自动隐藏（所有打开方式）
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
                UpdateGlobalMouseHookState();

                if (panelWindow.IsVisible)
                {
                    panelWindow.AutoHideOnDeactivated = true;

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
                    panelWindow.AutoHideOnDeactivated = true;
                    embeddedHoverDelayTimer.Stop();
                    ResetEmbeddedPin();
                    embeddedIsHoveringNow = false;

                    if (_foregroundEventHook != IntPtr.Zero)
                    {
                        _ = UnhookWinEvent(_foregroundEventHook);
                        _foregroundEventHook = IntPtr.Zero;
                    }

                    _panelShownForegroundHwnd = IntPtr.Zero;
                    _panelHwnd = IntPtr.Zero;
                    _panelOpenSource = PanelOpenSource.None;
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

        void TryPositionPanelToCursor()
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
        }

        void CreateEmbeddedModeWindows()
        {
            _embeddedTriggerHwnd = IntPtr.Zero;

            embeddedIsHoveringNow = false;
            embeddedHoverDelayTimer.Stop();
            ResetEmbeddedPin();

            // 悬停回调：延迟触发显示；移出触发区域立即隐藏（仅对悬停打开的面板）
            void OnHoverChanged(bool isHovering)
            {
                embeddedIsHoveringNow = isHovering;

                if (isHovering)
                {
                    if (panelWindow.IsVisible)
                    {
                        return;
                    }

                    var delayMs = Math.Clamp(panelViewModel.Settings.EmbeddedHoverDelayMs, 0, 5000);
                    embeddedHoverDelayTimer.Stop();
                    embeddedHoverDelayTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
                    if (delayMs <= 0)
                    {
                        ShowPanelByHover();
                    }
                    else
                    {
                        embeddedHoverDelayTimer.Start();
                    }

                    return;
                }

                embeddedHoverDelayTimer.Stop();

                if (panelWindow.IsVisible && _panelOpenSource == PanelOpenSource.Hover && !embeddedIsPinned)
                {
                    panelWindow.Hide();
                }
            }

            // 方案1：NativeTaskbarWindow (纯 C# 实现，已修复 Win11 遮挡问题)
            _nativeTaskbarWindow = new NativeTaskbarWindow(panelViewModel, iconRenderer, OnHoverChanged);
            if (_nativeTaskbarWindow.TryCreate())
            {
                _embeddedTriggerHwnd = _nativeTaskbarWindow.Handle;
                AppLogger.Info("NativeTaskbarWindow created for embedded mode");
                return;
            }

            _nativeTaskbarWindow.Dispose();
            _nativeTaskbarWindow = null;

            // 方案2：DLL 分层窗口实现（透明/鼠标穿透）
            _dllTaskbarEmbed = new DllTaskbarEmbed(panelViewModel, iconRenderer, OnHoverChanged);
            if (_dllTaskbarEmbed.TryCreate())
            {
                _embeddedTriggerHwnd = _dllTaskbarEmbed.Handle;
                AppLogger.Info("DllTaskbarEmbed created for embedded mode");
                return;
            }

            _dllTaskbarEmbed.Dispose();
            _dllTaskbarEmbed = null;

            // 回退方案3：WPF 悬浮窗口
            _embedWindow = new TaskbarEmbedWindow(panelViewModel, iconRenderer);
            _embedWindow.Show();
            _embeddedTriggerHwnd = new WindowInteropHelper(_embedWindow).Handle;
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
            _embeddedTriggerHwnd = IntPtr.Zero;
            embeddedHoverDelayTimer.Stop();
            ResetEmbeddedPin();
            embeddedIsHoveringNow = false;

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

            UpdateGlobalMouseHookState();
        };

        UpdateGlobalMouseHookState();
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
