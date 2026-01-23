using System.Windows;
using WeatherWidget.App.Services;

namespace WeatherWidget.App.UI;

public partial class EmbedExperimentWindow : Window
{
    private TaskbarEmbedExperiment? _experiment;

    public EmbedExperimentWindow()
    {
        InitializeComponent();
        AppendLog("任务栏嵌入方案实验工具");
        AppendLog("========================================");
        AppendLog("");
        AppendLog("点击「运行实验」创建多个测试窗口");
        AppendLog("");
        AppendLog("实验方案说明:");
        AppendLog("  A-TOPMOST:      现有方案 - WS_POPUP + TOPMOST + Owner");
        AppendLog("  B-Child-Tray:   子窗口挂载到 Shell_TrayWnd");
        AppendLog("  C-Child-Notify: 子窗口挂载到 TrayNotifyWnd");
        AppendLog("  D-Child-ReBar:  子窗口挂载到 ReBarWindow32 (Win10)");
        AppendLog("  E-Child-XAML:   子窗口挂载到 XAML Bridge (Win11)");
        AppendLog("  F-SetParent:    先创建 Popup 再 SetParent 转子窗口");
        AppendLog("  G-Popup-Top:    非 TOPMOST 的 Popup，仅 HWND_TOP");
        AppendLog("");
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _experiment?.Dispose();
            _experiment = new TaskbarEmbedExperiment();

            var result = _experiment.RunAllExperiments();
            AppendLog(result);

            StatusButton.IsEnabled = true;
            BringTopButton.IsEnabled = true;
            CleanupButton.IsEnabled = true;
            RunButton.Content = "重新运行";
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] {ex.Message}");
            AppendLog(ex.StackTrace ?? "");
        }
    }

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_experiment == null) return;

        AppendLog("");
        AppendLog("========================================");
        AppendLog(_experiment.GetStatus());
    }

    private void BringTopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_experiment == null) return;

        _experiment.BringAllToTop();
        AppendLog("[操作] 已尝试提升所有 Popup 窗口");
    }

    private void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_experiment == null) return;

        _experiment.DestroyAllWindows();
        AppendLog("[操作] 已销毁所有测试窗口");

        StatusButton.IsEnabled = false;
        BringTopButton.IsEnabled = false;
        CleanupButton.IsEnabled = false;
        RunButton.Content = "运行实验";
    }

    private void AppendLog(string text)
    {
        LogTextBox.AppendText(text + Environment.NewLine);
        LogScrollViewer.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        _experiment?.Dispose();
        base.OnClosed(e);
    }
}
