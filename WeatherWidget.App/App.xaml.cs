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

    private TaskbarEmbedWindow? _embedWindow;
    private NativeTaskbarWindow? _nativeTaskbarWindow;
    private DllTaskbarEmbed? _dllTaskbarEmbed;
    private ChildTaskbarWindow? _childTaskbarWindow;
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
    private SettingsStore? _settingsStore;
    private PanelViewModel? _panelViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 命令行参数：--experiment 启动嵌入方案实验窗口
        if (e.Args.Contains("--experiment", StringComparer.OrdinalIgnoreCase))
        {
            var experimentWindow = new UI.EmbedExperimentWindow();
            experimentWindow.Show();
            return;
        }

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
        _settingsStore = settingsStore;
        var settings = settingsStore.LoadOrCreateDefault();

        var weatherRepository = new WeatherRepository(
            new OpenMeteoClient(),
            new WeatherCacheStore(Path.Combine(appDataRoot, "cache.json")));

        var clothingAdvisor = new ClothingAdvisor();
        var geocodingClient = new GeocodingClient();

        var panelViewModel = new PanelViewModel(settingsStore, settings, weatherRepository, clothingAdvisor, geocodingClient);
        _panelViewModel = panelViewModel;
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

            embeddedPinDurationMs = Math.Clamp((panelViewModel.Settings.Embedded ?? Settings.Default.Embedded).HoverPinMs, 0, 5000);
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

            var shouldRun = panelWindow.IsVisible || _embeddedTriggerHwnd != IntPtr.Zero;
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
            if (_embeddedTriggerHwnd != IntPtr.Zero &&
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
                    var dpi = VisualTreeHelper.GetDpi(panelWindow);
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

                    var delayMs = Math.Clamp((panelViewModel.Settings.Embedded ?? Settings.Default.Embedded).HoverDelayMs, 0, 5000);
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

            // 方案：ChildTaskbarWindow (SetParent 子窗口方案)
            // 优势：无需 TopMost 保活，不遮挡右键菜单，自然跟随任务栏
            if (_childTaskbarWindow != null) return;

            _childTaskbarWindow = new ChildTaskbarWindow(panelViewModel, iconRenderer, OnHoverChanged);
            if (_childTaskbarWindow.TryCreate())
            {
                _embeddedTriggerHwnd = _childTaskbarWindow.Handle;
                AppLogger.Info("ChildTaskbarWindow created for embedded mode");
                return;
            }

            _childTaskbarWindow.Dispose();
            _childTaskbarWindow = null;

            AppLogger.Info("[ERROR] Failed to create ChildTaskbarWindow");
        }

        // 仅保留嵌入任务栏模式
        CreateEmbeddedModeWindows();

        // 异步初始化数据加载与刷新
        _ = panelWindow.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await panelViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Info($"PanelViewModel InitializeAsync failed: {ex.Message}");
            }
        });

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
            try
            {
                if (_settingsStore is not null && _panelViewModel is not null)
                {
                    _settingsStore.Save(_panelViewModel.Settings);
                }
            }
            catch { }

            try
            {
                _childTaskbarWindow?.Dispose();
                _childTaskbarWindow = null;
            }
            catch { }

            try
            {
                _embedWindow?.Close();
                _embedWindow = null;
            }
            catch { }

            try
            {
                _nativeTaskbarWindow?.Dispose();
                _nativeTaskbarWindow = null;
            }
            catch { }

            try
            {
                _dllTaskbarEmbed?.Dispose();
                _dllTaskbarEmbed = null;
            }
            catch { }

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
