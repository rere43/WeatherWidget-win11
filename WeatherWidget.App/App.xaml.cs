using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using WeatherWidget.App.UI;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

public partial class App : Application
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    private SecondaryIconWindow? _secondaryIconWindow;
    private TaskbarEmbedWindow? _embedWindow;
    private NativeTaskbarWindow? _nativeTaskbarWindow;
    private DllTaskbarEmbed? _dllTaskbarEmbed;

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

        // 初始化主题
        var themeManager = new ThemeManager();
        themeManager.ApplyTheme(settings.ThemeMode);

        // 监听主题模式变化
        panelViewModel.ThemeModeChanged += (_, _) => themeManager.ApplyTheme(panelViewModel.ThemeMode);

        var iconRenderer = new IconRenderer(new WeatherArtProvider());

        void CreateEmbeddedModeWindows()
        {
            // 方案1：NativeTaskbarWindow (纯 C# 实现，已修复 Win11 遮挡问题)
            _nativeTaskbarWindow = new NativeTaskbarWindow(panelViewModel, iconRenderer);
            if (_nativeTaskbarWindow.TryCreate())
            {
                AppLogger.Info("NativeTaskbarWindow created for embedded mode");
                return;
            }

            _nativeTaskbarWindow.Dispose();
            _nativeTaskbarWindow = null;

            // 方案2：DLL 分层窗口实现（透明/鼠标穿透）
            _dllTaskbarEmbed = new DllTaskbarEmbed(panelViewModel, iconRenderer);
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
}
