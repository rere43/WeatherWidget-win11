using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WeatherWidget.App.Services;
using WeatherWidget.App.UI;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

public partial class MainWindow : Window
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_RESTORE = 0xF120;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;

    // AppUserModelID 必须与 App.xaml.cs 中设置的一致
    private const string AppUserModelId = "MyWeatherWidget.App.v1";

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PropVariant pv);
        int SetValue(ref PROPERTYKEY key, ref PropVariant pv);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                vt = 31, // VT_LPWSTR
                pwszVal = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Clear()
        {
            if (vt == 31 && pwszVal != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pwszVal);
                pwszVal = IntPtr.Zero;
            }
            vt = 0;
        }
    }

    // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    // PKEY_AppUserModel_RelaunchCommand = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 2
    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchCommand = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 2
    };

    // PKEY_AppUserModel_RelaunchDisplayNameResource = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 4
    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchDisplayNameResource = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 4
    };

    private readonly PanelWindow _panelWindow;
    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private readonly SettingsStore _settingsStore;
    private GlobalHotkey? _hotkey;
    private readonly WindowIconManager _iconManager = new();
    private DateTimeOffset _lastToggleAt = DateTimeOffset.MinValue;
    private readonly DateTimeOffset _startupAt = DateTimeOffset.Now;
    private int _blankIconRetryCount;
    private bool _blankIconRetryScheduled;
    private bool _suppressFirstActivation;

    public MainWindow(
        PanelWindow panelWindow,
        PanelViewModel panelViewModel,
        IconRenderer iconRenderer,
        SettingsStore settingsStore)
    {
        InitializeComponent();

        _panelWindow = panelWindow;
        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;
        _settingsStore = settingsStore;

        var args = Environment.GetCommandLineArgs();
        var isAutoStart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        if (isAutoStart && _panelViewModel.Settings.StartHidden)
        {
            _suppressFirstActivation = true;
            AppLogger.Info("Startup: AutoStart detected, suppressing first activation");
        }

        // 嵌入模式下隐藏主程序任务栏图标和窗口
        if (_panelViewModel.Settings.IconDisplayMode == Models.IconDisplayMode.Embedded)
        {
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            Width = 0;
            Height = 0;
        }

        TaskbarItemInfo = new TaskbarItemInfo();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Activated += OnActivated;
        StateChanged += OnStateChanged;
        Closed += OnClosed;

        _panelViewModel.WeatherUpdated += (_, __) => UpdateTaskbarVisuals();

        // 监听图标模式变化，动态切换任务栏图标显示
        _panelViewModel.IconDisplayModeChanged += (_, __) =>
        {
            var isEmbedded = _panelViewModel.IconDisplayMode == Models.IconDisplayMode.Embedded;
            ShowInTaskbar = !isEmbedded;
            Visibility = isEmbedded ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // 嵌入模式下不响应激活事件
        if (_panelViewModel.Settings.IconDisplayMode == Models.IconDisplayMode.Embedded)
            return;

        if (_suppressFirstActivation)
        {
            _suppressFirstActivation = false;
            return;
        }

        // 启动瞬间会有 Activated 抖动，避免误触发打开/关闭面板。
        if (DateTimeOffset.Now - _startupAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        TryTogglePanel(source: "Activated");
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // 保持宿主窗口永远在“最小化且离屏”的状态，避免用户看到一个空白窗口。
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            Left = -10_000;
            Top = -10_000;
            WindowState = WindowState.Minimized;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // 设置窗口级 AppUserModelID，确保固定到任务栏后图标能正确更新
            SetWindowAppUserModelId(hwnd);
            var source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);
            _iconManager.Attach(hwnd);

            var candidates = new (string Label, uint Mods, uint Vk)[]
            {
                ("Ctrl+Alt+Shift+W", GlobalHotkey.CtrlAltShift('W').Modifiers, GlobalHotkey.CtrlAltShift('W').VirtualKey),
                ("Ctrl+Alt+Shift+T", GlobalHotkey.CtrlAltShift('T').Modifiers, GlobalHotkey.CtrlAltShift('T').VirtualKey),
                ("Ctrl+Alt+Shift+Y", GlobalHotkey.CtrlAltShift('Y').Modifiers, GlobalHotkey.CtrlAltShift('Y').VirtualKey),
            };

            foreach (var c in candidates)
            {
                try
                {
                    _hotkey = new GlobalHotkey(hwnd, id: 0x5744, modifiers: c.Mods, virtualKey: c.Vk);
                    _hotkey.Pressed += (_, __) =>
                    {
                        AppLogger.Info($"Hotkey pressed: {c.Label}");
                        TryTogglePanel(source: c.Label);
                    };

                    AppLogger.Info($"Hotkey registered: {c.Label}");
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Info($"Hotkey register failed ({c.Label}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"Hotkey register failed: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkey?.TryHandleMessage(msg, wParam) == true)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_SYSCOMMAND)
        {
            var cmd = wParam.ToInt32() & 0xFFF0;
            if (cmd is SC_RESTORE or SC_MINIMIZE or SC_MAXIMIZE)
            {
                AppLogger.Info($"WM_SYSCOMMAND cmd=0x{cmd:X}");
                TryTogglePanel(source: $"WM_SYSCOMMAND 0x{cmd:X}");
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("MainWindow Loaded");
        Left = -10_000;
        Top = -10_000;
        WindowState = WindowState.Minimized;

        // 嵌入模式下：确保窗口不在任务栏显示（Show 之后再次设置）
        if (_panelViewModel.Settings.IconDisplayMode == Models.IconDisplayMode.Embedded)
        {
            ShowInTaskbar = false;
            Hide(); // 完全隐藏窗口，不仅仅是最小化
            AppLogger.Info("MainWindow hidden for embedded mode");
        }

        // 注意：WPF 在 Application.OnStartup 期间 Show() 窗口时，Loaded 可能早于消息循环稳定期。
        // RenderTargetBitmap.Render 在此阶段可能渲染出全透明位图（表现为任务栏黑图标）。
        // 这里延后到 Dispatcher 空闲期再初始化与渲染图标。
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await _panelViewModel.InitializeAsync();
            UpdateTaskbarVisuals();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void TryTogglePanel(string source)
    {
        // 避免一次点击触发多个消息导致“刚打开又立刻关上”
        var now = DateTimeOffset.Now;
        if (now - _lastToggleAt < TimeSpan.FromMilliseconds(300))
        {
            AppLogger.Info($"Toggle suppressed: {source}");
            return;
        }

        _lastToggleAt = now;
        Dispatcher.BeginInvoke(() => TogglePanel(), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TogglePanel()
    {
        if (_panelWindow.IsVisible)
        {
            AppLogger.Info("PanelWindow Hide");
            _panelWindow.Hide();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var anchor = TaskbarAnchor.GetTaskbarAnchor(_panelWindow.Width, _panelWindow.Height, dpi.DpiScaleX, dpi.DpiScaleY);
        AppLogger.Info($"PanelWindow Show anchor=({anchor.Left:0.##},{anchor.Top:0.##}) size=({_panelWindow.Width:0.##},{_panelWindow.Height:0.##}) dpi=({dpi.DpiScaleX:0.##},{dpi.DpiScaleY:0.##})");
        _panelWindow.Left = anchor.Left;
        _panelWindow.Top = anchor.Top;

        _panelWindow.Show();
        _panelWindow.Activate();
    }

    private void UpdateTaskbarVisuals()
    {
        var snapshot = _panelViewModel.Snapshot;
        if (snapshot is null)
        {
            AppLogger.Info("UpdateTaskbarVisuals skipped: Snapshot is null");
            return;
        }

        try
        {
            var settings = _panelViewModel.Settings;

            // 根据图标模式选择渲染方式
            ImageSource big, small;
            if (settings.IconDisplayMode == Models.IconDisplayMode.Separate)
            {
                // 双图标模式：主窗口只显示天气图标
                big = _iconRenderer.RenderWeatherOnlyIcon(snapshot.Now, settings, size: 64);
                small = _iconRenderer.RenderWeatherOnlyIcon(snapshot.Now, settings, size: 32);
            }
            else
            {
                // 单图标模式：显示完整图标（天气+角标）
                big = _iconRenderer.RenderTaskbarIcon(snapshot.Now, settings, size: 64);
                small = _iconRenderer.RenderTaskbarIcon(snapshot.Now, settings, size: 32);
            }

            if (big is System.Windows.Media.Imaging.BitmapSource bigBmp &&
                small is System.Windows.Media.Imaging.BitmapSource smallBmp &&
                (IconRenderer.IsAllTransparent(bigBmp) || IconRenderer.IsAllTransparent(smallBmp)))
            {
                // 避免把“全透明”图标设置到任务栏（表现为黑/空）
                if (_blankIconRetryCount >= 5)
                {
                    AppLogger.Info("UpdateTaskbarVisuals skipped: icon is blank (no more retries)");
                }
                else
                {
                    _blankIconRetryCount++;
                    AppLogger.Info($"UpdateTaskbarVisuals skipped: icon is blank (retry {_blankIconRetryCount}/5)");
                    ScheduleBlankIconRetry();
                }

                TaskbarItemInfo!.Description = _panelViewModel.TaskbarDescription;
                return;
            }

            _blankIconRetryCount = 0;
            _iconManager.Update(big, small);
            AppLogger.Info("UpdateTaskbarVisuals icon updated");
        }
        catch (Exception ex)
        {
            AppLogger.Info($"UpdateTaskbarVisuals icon failed: {ex.Message}");
        }
        TaskbarItemInfo!.Description = _panelViewModel.TaskbarDescription;
    }

    private void ScheduleBlankIconRetry()
    {
        if (_blankIconRetryScheduled)
        {
            return;
        }

        _blankIconRetryScheduled = true;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Task.Delay(500);
            }
            finally
            {
                _blankIconRetryScheduled = false;
            }

            UpdateTaskbarVisuals();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AppLogger.Info("MainWindow Closed");
        _hotkey?.Dispose();
        _iconManager.Dispose();
        _panelWindow.Close();
        _settingsStore.Save(_panelViewModel.Settings);
        Application.Current.Shutdown();
    }

    /// <summary>
    /// 设置窗口级 AppUserModelID，确保任务栏固定后图标能正确关联并动态更新。
    /// Windows 任务栏会缓存固定应用的图标，通过设置 RelaunchCommand 和 RelaunchDisplayName，
    /// 可以让 Windows 在重新启动时正确关联到同一个应用实例。
    /// </summary>
    private void SetWindowAppUserModelId(IntPtr hwnd)
    {
        try
        {
            var guid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var hr = SHGetPropertyStoreForWindow(hwnd, ref guid, out var propStore);
            if (hr != 0 || propStore == null)
            {
                AppLogger.Info($"SetWindowAppUserModelId: SHGetPropertyStoreForWindow failed, hr=0x{hr:X}");
                return;
            }

            try
            {
                // 设置 AppUserModelID
                var appIdKey = PKEY_AppUserModel_ID;
                var appIdValue = PropVariant.FromString(AppUserModelId);
                propStore.SetValue(ref appIdKey, ref appIdValue);
                appIdValue.Clear();

                // 设置 RelaunchCommand（可执行文件路径）
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                var relaunchKey = PKEY_AppUserModel_RelaunchCommand;
                var relaunchValue = PropVariant.FromString($"\"{exePath}\"");
                propStore.SetValue(ref relaunchKey, ref relaunchValue);
                relaunchValue.Clear();

                // 设置 RelaunchDisplayName
                var displayNameKey = PKEY_AppUserModel_RelaunchDisplayNameResource;
                var displayNameValue = PropVariant.FromString("天气小组件");
                propStore.SetValue(ref displayNameKey, ref displayNameValue);
                displayNameValue.Clear();

                propStore.Commit();
                AppLogger.Info($"SetWindowAppUserModelId: Set to '{AppUserModelId}'");
            }
            finally
            {
                Marshal.ReleaseComObject(propStore);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"SetWindowAppUserModelId failed: {ex.Message}");
        }
    }
}
