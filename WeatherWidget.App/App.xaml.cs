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

        // 监听图标模式变化
        panelViewModel.IconDisplayModeChanged += (_, _) =>
        {
            if (panelViewModel.IconDisplayMode == IconDisplayMode.Separate)
            {
                if (_secondaryIconWindow == null)
                {
                    _secondaryIconWindow = new SecondaryIconWindow(panelViewModel, iconRenderer);
                    _secondaryIconWindow.Show();
                    AppLogger.Info("SecondaryIconWindow created dynamically");
                }
            }
            else
            {
                if (_secondaryIconWindow != null)
                {
                    _secondaryIconWindow.Close();
                    _secondaryIconWindow = null;
                    AppLogger.Info("SecondaryIconWindow closed");
                }
            }
        };
    }
}
