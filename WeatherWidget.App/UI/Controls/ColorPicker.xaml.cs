using System.Windows;
using System.Windows.Controls;

namespace WeatherWidget.App.UI.Controls;

public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(string),
            typeof(ColorPicker),
            new FrameworkPropertyMetadata("#FFFFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public ColorPicker()
    {
        InitializeComponent();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string color)
        {
            SelectedColor = color;
            ToggleBtn.IsChecked = false;
        }
    }
}
