using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App.UI;

public partial class PanelWindow : Window
{
    private const double BaseHeight = 540;
    private const double SettingsPanelWidth = 580;
    private const double LogPanelWidth = 300;
    private const double ClampMarginDip = 12;

    private bool _settingsPanelVisible;
    private bool _logPanelVisible;
    private INotifyPropertyChanged? _vmNotify;

    public bool AutoHideOnDeactivated { get; set; } = true;

    public PanelWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        DataContextChanged += OnDataContextChanged;
        Deactivated += OnDeactivated;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 点击窗口其他区域时关闭城市下拉
        if (DataContext is PanelViewModel vm && vm.IsCityDropdownOpen)
        {
            // 检查点击是否在 TextBox 或 Popup 内
            var hitTextBox = CityTextBox.IsMouseOver;
            var hitPopup = CitySuggestionPopup.IsMouseOver;
            if (!hitTextBox && !hitPopup)
            {
                vm.IsCityDropdownOpen = false;
            }
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // 窗口失去焦点时自动隐藏
        if (!AutoHideOnDeactivated)
        {
            return;
        }

        // 关闭下拉
        if (DataContext is PanelViewModel vm)
        {
            vm.IsCityDropdownOpen = false;
        }

        Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    // 关闭策略：仅通过点击任务栏图标再次切换、或按 ESC 关闭

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vmNotify is not null)
        {
            _vmNotify.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vmNotify = e.NewValue as INotifyPropertyChanged;
        if (_vmNotify is not null)
        {
            _vmNotify.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (e.NewValue is PanelViewModel vm)
        {
            _settingsPanelVisible = vm.IsSettingsPanelVisible;
            _logPanelVisible = vm.IsLogPanelVisible;
        }
        RecalculateSize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not PanelViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(PanelViewModel.IsDayDetailVisible))
        {
            // 展开/收起温湿图时，需要延迟等待布局更新后再计算高度
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, RecalculateSize);
        }
        else if (e.PropertyName == nameof(PanelViewModel.IsSettingsPanelVisible))
        {
            _settingsPanelVisible = vm.IsSettingsPanelVisible;
            Dispatcher.BeginInvoke(RecalculateSize);
        }
        else if (e.PropertyName == nameof(PanelViewModel.IsLogPanelVisible))
        {
            _logPanelVisible = vm.IsLogPanelVisible;
            Dispatcher.BeginInvoke(RecalculateSize);
        }
    }

    private void RecalculateSize()
    {
        var targetWidth = 940.0;

        if (_settingsPanelVisible)
        {
            targetWidth += SettingsPanelWidth;
        }

        if (_logPanelVisible)
        {
            targetWidth += LogPanelWidth;
        }

        var wa = SystemParameters.WorkArea;
        var maxHeight = Math.Max(240, wa.Height - ClampMarginDip * 2);
        var maxWidth = Math.Max(400, wa.Width - ClampMarginDip * 2);
        targetWidth = Math.Min(targetWidth, maxWidth);

        var targetHeight = GetDesiredHeight(targetWidth);
        targetHeight = Math.Min(targetHeight, maxHeight);

        ResizeToSize(targetWidth, targetHeight);
    }

    private double GetDesiredHeight(double targetWidth)
    {
        if (Content is not UIElement root)
        {
            return BaseHeight;
        }

        root.Measure(new Size(targetWidth, double.PositiveInfinity));
        var desiredHeight = root.DesiredSize.Height;
        if (double.IsNaN(desiredHeight) || double.IsInfinity(desiredHeight) || desiredHeight <= 0)
        {
            return BaseHeight;
        }

        return Math.Ceiling(desiredHeight);
    }

    private void ResizeToSize(double targetWidth, double targetHeight)
    {
        targetHeight = Math.Max(240, targetHeight);
        targetWidth = Math.Max(400, targetWidth);

        var oldHeight = Height;
        var oldWidth = Width;

        var widthChanged = Math.Abs(oldWidth - targetWidth) >= 0.5;
        var heightChanged = Math.Abs(oldHeight - targetHeight) >= 0.5;

        if (!widthChanged && !heightChanged)
        {
            return;
        }

        if (heightChanged)
        {
            var delta = targetHeight - oldHeight;
            Height = targetHeight;
            Top -= delta; // 保持底边不动
        }

        if (widthChanged)
        {
            var delta = targetWidth - oldWidth;
            Width = targetWidth;
            Left -= delta; // 保持右边不动
        }

        ClampToWorkArea();
    }

    private void ClampToWorkArea()
    {
        var wa = SystemParameters.WorkArea;

        var minLeft = wa.Left + ClampMarginDip;
        var maxLeft = wa.Right - Width - ClampMarginDip;
        var minTop = wa.Top + ClampMarginDip;
        var maxTop = wa.Bottom - Height - ClampMarginDip;

        if (maxLeft < minLeft)
        {
            maxLeft = minLeft;
        }

        if (maxTop < minTop)
        {
            maxTop = minTop;
        }

        Left = Math.Clamp(Left, minLeft, maxLeft);
        Top = Math.Clamp(Top, minTop, maxTop);
    }

    private void CityTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelViewModel vm)
        {
            vm.IsCityDropdownOpen = true;
        }
    }

    private void CityTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 延迟关闭，检查焦点是否转移到 Popup 内
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (DataContext is PanelViewModel vm)
            {
                // 如果焦点仍在输入框或 Popup 内，不关闭
                if (CityTextBox.IsKeyboardFocusWithin || CitySuggestionPopup.IsKeyboardFocusWithin)
                {
                    return;
                }
                // 如果鼠标在 Popup 上，也不关闭（用户可能正在点击）
                if (CitySuggestionPopup.IsMouseOver)
                {
                    return;
                }
                vm.IsCityDropdownOpen = false;
            }
        });
    }

    private void CitySuggestionItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is Services.GeoSuggestion suggestion)
        {
            if (DataContext is PanelViewModel vm)
            {
                vm.SelectedCitySuggestion = suggestion;
                vm.IsCityDropdownOpen = false;
            }
        }
    }

    private void CitySuggestionBorder_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is Services.GeoSuggestion suggestion)
        {
            if (DataContext is PanelViewModel vm)
            {
                vm.SelectedCitySuggestion = suggestion;
                vm.IsCityDropdownOpen = false;
            }
        }
    }

    private void CustomLocationItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is Models.CustomLocation loc)
        {
            if (DataContext is PanelViewModel vm)
            {
                vm.SelectCustomLocationCommand.Execute(loc);
            }
        }
    }
}
