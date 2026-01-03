using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using WeatherWidget.App.Services;
using WeatherWidget.App.ViewModels;

namespace WeatherWidget.App;

/// <summary>
/// 第二个任务栏图标窗口，用于双图标模式下显示温度数字
/// </summary>
public partial class SecondaryIconWindow : Window
{
    private const string AppUserModelId = "MyWeatherWidget.TempIcon.v1";

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
                vt = 31,
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

    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchDisplayNameResource = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 4
    };

    private readonly PanelViewModel _panelViewModel;
    private readonly IconRenderer _iconRenderer;
    private readonly WindowIconManager _iconManager = new();

    public SecondaryIconWindow(PanelViewModel panelViewModel, IconRenderer iconRenderer)
    {
        InitializeComponent();

        _panelViewModel = panelViewModel;
        _iconRenderer = iconRenderer;

        TaskbarItemInfo = new TaskbarItemInfo();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;

        _panelViewModel.WeatherUpdated += (_, _) => UpdateIcon();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowAppUserModelId(hwnd);
        _iconManager.Attach(hwnd);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = -10_000;
        Top = -10_000;
        WindowState = WindowState.Minimized;

        _ = Dispatcher.BeginInvoke(() =>
        {
            UpdateIcon();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
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

    public void UpdateIcon()
    {
        var snapshot = _panelViewModel.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        try
        {
            var settings = _panelViewModel.Settings;
            var big = _iconRenderer.RenderTemperatureOnlyIcon(snapshot.Now, settings, size: 64);
            var small = _iconRenderer.RenderTemperatureOnlyIcon(snapshot.Now, settings, size: 32);

            if (big is System.Windows.Media.Imaging.BitmapSource bigBmp &&
                small is System.Windows.Media.Imaging.BitmapSource smallBmp &&
                !IconRenderer.IsAllTransparent(bigBmp) && !IconRenderer.IsAllTransparent(smallBmp))
            {
                _iconManager.Update(big, small);
            }

            TaskbarItemInfo!.Description = $"温度: {Math.Round(snapshot.Now.TemperatureC):0}°C";
        }
        catch (Exception ex)
        {
            AppLogger.Info($"SecondaryIconWindow UpdateIcon failed: {ex.Message}");
        }
    }

    private void SetWindowAppUserModelId(IntPtr hwnd)
    {
        try
        {
            var guid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            var hr = SHGetPropertyStoreForWindow(hwnd, ref guid, out var propStore);
            if (hr != 0 || propStore == null)
            {
                return;
            }

            try
            {
                var appIdKey = PKEY_AppUserModel_ID;
                var appIdValue = PropVariant.FromString(AppUserModelId);
                propStore.SetValue(ref appIdKey, ref appIdValue);
                appIdValue.Clear();

                var displayNameKey = PKEY_AppUserModel_RelaunchDisplayNameResource;
                var displayNameValue = PropVariant.FromString("温度");
                propStore.SetValue(ref displayNameKey, ref displayNameValue);
                displayNameValue.Clear();

                propStore.Commit();
            }
            finally
            {
                Marshal.ReleaseComObject(propStore);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Info($"SecondaryIconWindow SetWindowAppUserModelId failed: {ex.Message}");
        }
    }

    public new void Close()
    {
        _iconManager.Dispose();
        base.Close();
    }
}
