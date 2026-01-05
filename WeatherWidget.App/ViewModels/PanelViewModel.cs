using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WeatherWidget.App.Models;
using AppSettings = WeatherWidget.App.Models.Settings;
using WeatherWidget.App.Services;
using WeatherWidget.App.Utils;

namespace WeatherWidget.App.ViewModels;

public sealed class PanelViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly WeatherRepository _weatherRepository;
    private readonly ClothingAdvisor _clothingAdvisor;
    private readonly GeocodingClient _geocodingClient;
    private readonly ClothingImageProvider _clothingImageProvider = new();
    private readonly DispatcherTimer _timer;

    private bool _isBusy;
    private string _statusText = "正在初始化…";
    private WeatherSnapshot? _snapshot;
    private string _clothingSuggestion = "—";
    private System.Windows.Media.ImageSource? _clothingImage;
    private string _city;
    private readonly DispatcherTimer _citySearchTimer;
    private CancellationTokenSource? _citySearchCts;
    private bool _suppressCitySearch;
    private GeoSuggestion? _selectedCitySuggestion;
    private ResolvedLocation? _pendingCityResolved;
    private double _tempBadgeOffsetX;
    private double _tempBadgeOffsetY;
    private double _tempBadgeFontScale;
    private string _tempBadgeFormat;
    private double _cornerBadgeOffsetX;
    private double _cornerBadgeOffsetY;
    private double _cornerBadgeFontScale;
    private string _cornerUvFormat;
    private string _cornerHumidityFormat;
    private bool _extraBadgeEnabled;
    private double _extraBadgeOffsetX;
    private double _extraBadgeOffsetY;
    private double _extraBadgeFontScale;
    private string _extraBadgeFormat;
    private int _refreshMinutes;
    private IconCornerMetric _iconCornerMetric;
    private bool _badgeBackgroundEnabled;
    private double _badgeStrokeWidth;
    private bool _iconBackgroundEnabled;
    private double _iconOffsetX;
    private double _iconOffsetY;
    private BadgePosition _tempBadgePosition;
    private BadgePosition _cornerBadgePosition;
    private BadgePosition _extraBadgePosition;
    private string _badgeFontFamily;
    private string _tempBadgeColor;
    private string _cornerBadgeColor;
    private string _extraBadgeColor;
    private ThemeMode _themeMode;
    private IconDisplayMode _iconDisplayMode;
    private double _embeddedIconScale;
    private double _embeddedOffsetX;
    private double _embeddedUvToWeatherGap;
    private int _embeddedHoverDelayMs;
    private EmbeddedTextLayout _embeddedTextLayout;
    private EmbeddedTextAlignment _embeddedTextAlignment;
    private readonly DispatcherTimer _settingsSaveTimer;
    private bool _settingsSavePending;

    private ForecastDayViewModel? _selectedForecastDay;
    private bool _isDayDetailVisible;
    private Geometry _temperatureChartGeometry = Geometry.Empty;
    private Geometry _humidityChartGeometry = Geometry.Empty;
    private string _selectedDayTempSummary = "—";
    private string _selectedDayHumiditySummary = "—";
    private bool _isSettingsPanelVisible;

    public event EventHandler? WeatherUpdated;

    public static IReadOnlyList<string> SystemFontFamilies { get; } = GetSystemFonts();

    private static IReadOnlyList<string> GetSystemFonts()
    {
        var fonts = new List<string>();
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            fonts.Add(family.Source);
        }
        return fonts;
    }

    public PanelViewModel(
        SettingsStore settingsStore,
        Settings settings,
        WeatherRepository weatherRepository,
        ClothingAdvisor clothingAdvisor,
        GeocodingClient geocodingClient)
    {
        _settingsStore = settingsStore;
        Settings = settings;
        _weatherRepository = weatherRepository;
        _clothingAdvisor = clothingAdvisor;
        _geocodingClient = geocodingClient;

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(forceRefresh: true));
        SaveSettingsCommand = new RelayCommand(() => _ = SaveSettingsAsync());
        ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
        ToggleDayDetailCommand = new RelayCommand<ForecastDayViewModel>(ToggleDayDetail);
        ToggleSettingsPanelCommand = new RelayCommand(ToggleSettingsPanel);
        ToggleLogPanelCommand = new RelayCommand(ToggleLogPanel);

        _city = Settings.City;
        _tempBadgeOffsetX = Settings.TempBadgeOffsetX;
        _tempBadgeOffsetY = Settings.TempBadgeOffsetY;
        _tempBadgeFontScale = Settings.TempBadgeFontScale;
        _tempBadgeFormat = Settings.TempBadgeFormat;
        _cornerBadgeOffsetX = Settings.CornerBadgeOffsetX;
        _cornerBadgeOffsetY = Settings.CornerBadgeOffsetY;
        _cornerBadgeFontScale = Settings.CornerBadgeFontScale;
        _cornerUvFormat = Settings.CornerUvFormat;
        _cornerHumidityFormat = Settings.CornerHumidityFormat;
        _extraBadgeEnabled = Settings.ExtraBadgeEnabled;
        _extraBadgeOffsetX = Settings.ExtraBadgeOffsetX;
        _extraBadgeOffsetY = Settings.ExtraBadgeOffsetY;
        _extraBadgeFontScale = Settings.ExtraBadgeFontScale;
        _extraBadgeFormat = Settings.ExtraBadgeFormat;
        _refreshMinutes = Math.Max(1, (int)Math.Round(Settings.RefreshInterval.TotalMinutes));
        _iconCornerMetric = Settings.IconCornerMetric;
        _badgeBackgroundEnabled = Settings.BadgeBackgroundEnabled;
        _badgeStrokeWidth = Settings.BadgeStrokeWidth;
        _iconBackgroundEnabled = Settings.IconBackgroundEnabled;
        _iconOffsetX = Settings.IconOffsetX;
        _iconOffsetY = Settings.IconOffsetY;
        _tempBadgePosition = Settings.TempBadgePosition;
        _cornerBadgePosition = Settings.CornerBadgePosition;
        _extraBadgePosition = Settings.ExtraBadgePosition;
        _badgeFontFamily = Settings.BadgeFontFamily;
        _tempBadgeColor = Settings.TempBadgeColor;
        _cornerBadgeColor = Settings.CornerBadgeColor;
        _extraBadgeColor = Settings.ExtraBadgeColor;
        _themeMode = Settings.ThemeMode;
        _iconDisplayMode = Settings.IconDisplayMode;
        _embeddedIconScale = Settings.EmbeddedIconScale;
        _embeddedOffsetX = Settings.EmbeddedOffsetX;
        _embeddedUvToWeatherGap = Settings.EmbeddedUvToWeatherGap;
        _embeddedHoverDelayMs = Settings.EmbeddedHoverDelayMs;
        _embeddedTextLayout = Settings.EmbeddedTextLayout;
        _embeddedTextAlignment = Settings.EmbeddedTextAlignment;

        _settingsSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _settingsSaveTimer.Tick += (_, __) => FlushSettingsSave();

        CitySuggestions = new ObservableCollection<GeoSuggestion>();
        _citySearchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260),
        };
        _citySearchTimer.Tick += (_, __) =>
        {
            _citySearchTimer.Stop();
            _ = UpdateCitySuggestionsAsync();
        };

        _timer = new DispatcherTimer
        {
            Interval = Settings.RefreshInterval,
        };
        _timer.Tick += async (_, __) => await RefreshAsync(forceRefresh: false);

        AutoStartManager.SetAutoStart(Settings.AutoStart);
    }

    public Settings Settings { get; private set; }

    public bool AutoStart
    {
        get => Settings.AutoStart;
        set
        {
            if (Settings.AutoStart == value) return;
            Settings = Settings with { AutoStart = value };
            AutoStartManager.SetAutoStart(value);
            ScheduleSettingsSave();
            RaisePropertyChanged();
        }
    }

    public bool StartHidden
    {
        get => Settings.StartHidden;
        set
        {
            if (Settings.StartHidden == value) return;
            Settings = Settings with { StartHidden = value };
            ScheduleSettingsSave();
            RaisePropertyChanged();
        }
    }

    public ObservableCollection<GeoSuggestion> CitySuggestions { get; }

    public ObservableCollection<string> UpdateLogs { get; } = new();

    public string UpdateLogHeaderLine =>
        UpdateLogs.Count == 0 ? "更新日志：—" : $"更新日志：{UpdateLogs[^1]}";

    public GeoSuggestion? SelectedCitySuggestion
    {
        get => _selectedCitySuggestion;
        set
        {
            if (!SetProperty(ref _selectedCitySuggestion, value))
            {
                return;
            }

            if (value is null)
            {
                return;
            }

            _pendingCityResolved = new ResolvedLocation(value.Latitude, value.Longitude);
            _suppressCitySearch = true;
            City = value.DisplayName;
            _suppressCitySearch = false;

            CitySuggestions.Clear();
        }
    }

    public string City
    {
        get => _city;
        set
        {
            if (!SetProperty(ref _city, value))
            {
                return;
            }

            if (_suppressCitySearch)
            {
                return;
            }

            _pendingCityResolved = null;
            if (SelectedCitySuggestion is not null)
            {
                SelectedCitySuggestion = null;
            }

            ScheduleCitySearch();
        }
    }

    public double TempBadgeOffsetX
    {
        get => _tempBadgeOffsetX;
        set
        {
            if (!SetProperty(ref _tempBadgeOffsetX, value))
            {
                return;
            }

            Settings = Settings with { TempBadgeOffsetX = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TempBadgeOffsetY
    {
        get => _tempBadgeOffsetY;
        set
        {
            if (!SetProperty(ref _tempBadgeOffsetY, value))
            {
                return;
            }

            Settings = Settings with { TempBadgeOffsetY = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TempBadgeFontScale
    {
        get => _tempBadgeFontScale;
        set
        {
            if (!SetProperty(ref _tempBadgeFontScale, value))
            {
                return;
            }

            Settings = Settings with { TempBadgeFontScale = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string TempBadgeFormat
    {
        get => _tempBadgeFormat;
        set
        {
            if (!SetProperty(ref _tempBadgeFormat, value))
            {
                return;
            }

            Settings = Settings with { TempBadgeFormat = string.IsNullOrWhiteSpace(value) ? AppSettings.Default.TempBadgeFormat : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double CornerBadgeOffsetX
    {
        get => _cornerBadgeOffsetX;
        set
        {
            if (!SetProperty(ref _cornerBadgeOffsetX, value))
            {
                return;
            }

            Settings = Settings with { CornerBadgeOffsetX = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double CornerBadgeOffsetY
    {
        get => _cornerBadgeOffsetY;
        set
        {
            if (!SetProperty(ref _cornerBadgeOffsetY, value))
            {
                return;
            }

            Settings = Settings with { CornerBadgeOffsetY = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double CornerBadgeFontScale
    {
        get => _cornerBadgeFontScale;
        set
        {
            if (!SetProperty(ref _cornerBadgeFontScale, value))
            {
                return;
            }

            Settings = Settings with { CornerBadgeFontScale = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CornerUvFormat
    {
        get => _cornerUvFormat;
        set
        {
            if (!SetProperty(ref _cornerUvFormat, value))
            {
                return;
            }

            Settings = Settings with { CornerUvFormat = string.IsNullOrWhiteSpace(value) ? AppSettings.Default.CornerUvFormat : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CornerHumidityFormat
    {
        get => _cornerHumidityFormat;
        set
        {
            if (!SetProperty(ref _cornerHumidityFormat, value))
            {
                return;
            }

            Settings = Settings with { CornerHumidityFormat = string.IsNullOrWhiteSpace(value) ? AppSettings.Default.CornerHumidityFormat : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ExtraBadgeEnabled
    {
        get => _extraBadgeEnabled;
        set
        {
            if (!SetProperty(ref _extraBadgeEnabled, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeEnabled = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ExtraBadgeOffsetX
    {
        get => _extraBadgeOffsetX;
        set
        {
            if (!SetProperty(ref _extraBadgeOffsetX, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeOffsetX = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ExtraBadgeOffsetY
    {
        get => _extraBadgeOffsetY;
        set
        {
            if (!SetProperty(ref _extraBadgeOffsetY, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeOffsetY = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double ExtraBadgeFontScale
    {
        get => _extraBadgeFontScale;
        set
        {
            if (!SetProperty(ref _extraBadgeFontScale, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeFontScale = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ExtraBadgeFormat
    {
        get => _extraBadgeFormat;
        set
        {
            if (!SetProperty(ref _extraBadgeFormat, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeFormat = string.IsNullOrWhiteSpace(value) ? AppSettings.Default.ExtraBadgeFormat : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public int RefreshMinutes
    {
        get => _refreshMinutes;
        set => SetProperty(ref _refreshMinutes, Math.Max(1, value));
    }

    public IconCornerMetric IconCornerMetric
    {
        get => _iconCornerMetric;
        set
        {
            if (!SetProperty(ref _iconCornerMetric, value))
            {
                return;
            }

            // 角标设置即时生效并持久化（不要求额外点击"保存"）
            Settings = Settings with { IconCornerMetric = value };
            _settingsStore.Save(Settings);
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool BadgeBackgroundEnabled
    {
        get => _badgeBackgroundEnabled;
        set
        {
            if (!SetProperty(ref _badgeBackgroundEnabled, value))
            {
                return;
            }

            Settings = Settings with { BadgeBackgroundEnabled = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double BadgeStrokeWidth
    {
        get => _badgeStrokeWidth;
        set
        {
            value = Math.Clamp(value, 0.5, 6.0);
            if (!SetProperty(ref _badgeStrokeWidth, value))
            {
                return;
            }

            Settings = Settings with { BadgeStrokeWidth = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IconBackgroundEnabled
    {
        get => _iconBackgroundEnabled;
        set
        {
            if (!SetProperty(ref _iconBackgroundEnabled, value))
            {
                return;
            }

            Settings = Settings with { IconBackgroundEnabled = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double IconOffsetX
    {
        get => _iconOffsetX;
        set
        {
            if (!SetProperty(ref _iconOffsetX, value))
            {
                return;
            }

            Settings = Settings with { IconOffsetX = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double IconOffsetY
    {
        get => _iconOffsetY;
        set
        {
            if (!SetProperty(ref _iconOffsetY, value))
            {
                return;
            }

            Settings = Settings with { IconOffsetY = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public BadgePosition TempBadgePosition
    {
        get => _tempBadgePosition;
        set
        {
            if (!SetProperty(ref _tempBadgePosition, value))
            {
                return;
            }

            Settings = Settings with { TempBadgePosition = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public BadgePosition CornerBadgePosition
    {
        get => _cornerBadgePosition;
        set
        {
            if (!SetProperty(ref _cornerBadgePosition, value))
            {
                return;
            }

            Settings = Settings with { CornerBadgePosition = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public BadgePosition ExtraBadgePosition
    {
        get => _extraBadgePosition;
        set
        {
            if (!SetProperty(ref _extraBadgePosition, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgePosition = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string BadgeFontFamily
    {
        get => _badgeFontFamily;
        set
        {
            if (!SetProperty(ref _badgeFontFamily, value))
            {
                return;
            }

            Settings = Settings with { BadgeFontFamily = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string TempBadgeColor
    {
        get => _tempBadgeColor;
        set
        {
            if (!SetProperty(ref _tempBadgeColor, value))
            {
                return;
            }

            Settings = Settings with { TempBadgeColor = string.IsNullOrWhiteSpace(value) ? "#FFFFFFFF" : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CornerBadgeColor
    {
        get => _cornerBadgeColor;
        set
        {
            if (!SetProperty(ref _cornerBadgeColor, value))
            {
                return;
            }

            Settings = Settings with { CornerBadgeColor = string.IsNullOrWhiteSpace(value) ? "#FFFFFFFF" : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ExtraBadgeColor
    {
        get => _extraBadgeColor;
        set
        {
            if (!SetProperty(ref _extraBadgeColor, value))
            {
                return;
            }

            Settings = Settings with { ExtraBadgeColor = string.IsNullOrWhiteSpace(value) ? "#FFFFFFFF" : value.Trim() };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public ThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (!SetProperty(ref _themeMode, value))
            {
                return;
            }

            Settings = Settings with { ThemeMode = value };
            ScheduleSettingsSave();
            ThemeModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ThemeModeChanged;

    public IconDisplayMode IconDisplayMode
    {
        get => _iconDisplayMode;
        set
        {
            if (!SetProperty(ref _iconDisplayMode, value))
            {
                return;
            }

            Settings = Settings with { IconDisplayMode = value };
            ScheduleSettingsSave();
            IconDisplayModeChanged?.Invoke(this, EventArgs.Empty);
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? IconDisplayModeChanged;

    public double EmbeddedIconScale
    {
        get => _embeddedIconScale;
        set
        {
            value = Math.Clamp(value, 0.5, 1.6);
            if (!SetProperty(ref _embeddedIconScale, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedIconScale = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double EmbeddedOffsetX
    {
        get => _embeddedOffsetX;
        set
        {
            value = Math.Clamp(value, -300, 300);
            if (!SetProperty(ref _embeddedOffsetX, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedOffsetX = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public double EmbeddedUvToWeatherGap
    {
        get => _embeddedUvToWeatherGap;
        set
        {
            value = Math.Clamp(value, 2, 40);
            if (!SetProperty(ref _embeddedUvToWeatherGap, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedUvToWeatherGap = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public int EmbeddedHoverDelayMs
    {
        get => _embeddedHoverDelayMs;
        set
        {
            value = Math.Clamp(value, 0, 5000);
            if (!SetProperty(ref _embeddedHoverDelayMs, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedHoverDelayMs = value };
            ScheduleSettingsSave();
        }
    }

    public EmbeddedTextLayout EmbeddedTextLayout
    {
        get => _embeddedTextLayout;
        set
        {
            if (!SetProperty(ref _embeddedTextLayout, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedTextLayout = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public EmbeddedTextAlignment EmbeddedTextAlignment
    {
        get => _embeddedTextAlignment;
        set
        {
            if (!SetProperty(ref _embeddedTextAlignment, value))
            {
                return;
            }

            Settings = Settings with { EmbeddedTextAlignment = value };
            ScheduleSettingsSave();
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public WeatherSnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            if (SetProperty(ref _snapshot, value))
            {
                RaisePropertyChanged(nameof(CurrentTempText));
                RaisePropertyChanged(nameof(CurrentHumidityText));
                RaisePropertyChanged(nameof(CurrentUvText));
                RaisePropertyChanged(nameof(LocationText));
                RaisePropertyChanged(nameof(LastUpdatedText));
                RaisePropertyChanged(nameof(TaskbarDescription));
            }
        }
    }

    public string ClothingSuggestion
    {
        get => _clothingSuggestion;
        private set => SetProperty(ref _clothingSuggestion, value);
    }

    public System.Windows.Media.ImageSource? ClothingImage
    {
        get => _clothingImage;
        private set => SetProperty(ref _clothingImage, value);
    }

    public ObservableCollection<ForecastDayViewModel> ForecastDays { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand<ForecastDayViewModel> ToggleDayDetailCommand { get; }
    public RelayCommand ToggleSettingsPanelCommand { get; }
    public RelayCommand ToggleLogPanelCommand { get; }

    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        private set => SetProperty(ref _isSettingsPanelVisible, value);
    }

    private bool _isLogPanelVisible;
    public bool IsLogPanelVisible
    {
        get => _isLogPanelVisible;
        private set => SetProperty(ref _isLogPanelVisible, value);
    }

    public string LocationText => Snapshot?.LocationName ?? Settings.City;

    public string CurrentTempText => Snapshot is null ? "—" : $"{Math.Round(Snapshot.Now.TemperatureC):0}°C";

    public string CurrentHumidityText => Snapshot?.Now.RelativeHumidityPercent is null ? "—" : $"{Snapshot.Now.RelativeHumidityPercent.Value}%";

    public string CurrentUvText => Snapshot?.Now.UvIndex is null ? "—" : $"{Snapshot.Now.UvIndex.Value:0.0}";

    public string LastUpdatedText => Snapshot is null ? "—" : Snapshot.FetchedAt.ToLocalTime().ToString("HH:mm");

    public ForecastDayViewModel? SelectedForecastDay
    {
        get => _selectedForecastDay;
        private set
        {
            if (SetProperty(ref _selectedForecastDay, value))
            {
                RaisePropertyChanged(nameof(SelectedDayDetailTitle));
                if (_selectedForecastDay is null)
                {
                    IsDayDetailVisible = false;
                }
                UpdateSelectedDayDetail();
            }
        }
    }

    public bool IsDayDetailVisible
    {
        get => _isDayDetailVisible;
        private set => SetProperty(ref _isDayDetailVisible, value);
    }

    public string SelectedDayDetailTitle =>
        SelectedForecastDay is null ? string.Empty : $"{SelectedForecastDay.DayLabel} · 温度/湿度 24 小时";

    public Geometry TemperatureChartGeometry
    {
        get => _temperatureChartGeometry;
        private set => SetProperty(ref _temperatureChartGeometry, value);
    }

    public Geometry HumidityChartGeometry
    {
        get => _humidityChartGeometry;
        private set => SetProperty(ref _humidityChartGeometry, value);
    }

    public ObservableCollection<HourIcon> TemperatureHourIcons { get; } = new();
    public ObservableCollection<ChartMarker> ChartMarkers { get; } = new();

    private double _currentTimeLineX = -1;
    public double CurrentTimeLineX
    {
        get => _currentTimeLineX;
        private set => SetProperty(ref _currentTimeLineX, value);
    }

    private bool _showCurrentTimeLine;
    public bool ShowCurrentTimeLine
    {
        get => _showCurrentTimeLine;
        private set => SetProperty(ref _showCurrentTimeLine, value);
    }

    public string SelectedDayTempSummary
    {
        get => _selectedDayTempSummary;
        private set => SetProperty(ref _selectedDayTempSummary, value);
    }

    public string SelectedDayHumiditySummary
    {
        get => _selectedDayHumiditySummary;
        private set => SetProperty(ref _selectedDayHumiditySummary, value);
    }

    public string TaskbarDescription
    {
        get
        {
            if (Snapshot is null)
            {
                return "天气：未加载";
            }

            var now = Snapshot.Now;
            var uv = now.UvIndex is null ? "UV—" : $"UV{Math.Round(now.UvIndex.Value):0}";
            var rh = now.RelativeHumidityPercent is null ? "湿度—" : $"湿度{now.RelativeHumidityPercent.Value}%";
            return $"{Snapshot.LocationName} {Math.Round(now.TemperatureC):0}°C  {uv}  {rh}";
        }
    }

    public async Task InitializeAsync()
    {
        var cached = _weatherRepository.TryGetCached();
        if (cached is not null)
        {
            ApplySnapshot(cached);
            SetStatus("已加载缓存");
        }

        await EnsureCityResolvedAsync();
        await RefreshAsync(forceRefresh: false);
        _timer.Interval = Settings.RefreshInterval;
        _timer.Start();
    }

    private async Task EnsureCityResolvedAsync()
    {
        var city = Settings.City;
        if (string.IsNullOrWhiteSpace(city))
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var resolved = await _geocodingClient.ResolveAsync(city.Trim(), cts.Token);
            if (resolved is null)
            {
                return;
            }

            var delta = Math.Abs(resolved.Latitude - Settings.Latitude) + Math.Abs(resolved.Longitude - Settings.Longitude);
            if (delta < 0.01)
            {
                return;
            }

            Settings = Settings with
            {
                Latitude = resolved.Latitude,
                Longitude = resolved.Longitude,
            };
            _settingsStore.Save(Settings);
            AppendLog("已根据城市定位更新坐标");
        }
        catch
        {
            // 初始化阶段定位失败不阻塞后续刷新
        }
    }

    private async Task RefreshAsync(bool forceRefresh)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStatus("正在更新天气…");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await _weatherRepository.GetAsync(
                locationName: Settings.City,
                latitude: Settings.Latitude,
                longitude: Settings.Longitude,
                refreshInterval: Settings.RefreshInterval,
                forceRefresh: forceRefresh,
                cancellationToken: cts.Token);

            ApplySnapshot(snapshot);
            SetStatus("更新成功");
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus($"更新失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(WeatherSnapshot snapshot)
    {
        Snapshot = snapshot;
        ClothingSuggestion = _clothingAdvisor.GetSuggestion(snapshot);
        var clothingImage = _clothingImageProvider.Get(_clothingAdvisor.GetHumidityLevel(snapshot));
        ClothingImage = clothingImage;

        ForecastDays.Clear();
        foreach (var day in snapshot.Days)
        {
            ForecastDays.Add(new ForecastDayViewModel(day));
        }

        if (SelectedForecastDay is not null)
        {
            var date = SelectedForecastDay.Date;
            ForecastDayViewModel? found = null;
            foreach (var d in ForecastDays)
            {
                if (d.Date == date)
                {
                    found = d;
                    break;
                }
            }

            SelectedForecastDay = found;
        }
    }

    private async Task SaveSettingsAsync()
    {
        var newCity = string.IsNullOrWhiteSpace(City) ? Settings.City : City.Trim();
        var refresh = TimeSpan.FromMinutes(Math.Max(1, RefreshMinutes));
        var def = AppSettings.Default;

        var tempOffsetX = TempBadgeOffsetX;
        var tempOffsetY = TempBadgeOffsetY;
        var tempScale = TempBadgeFontScale;
        var cornerOffsetX = CornerBadgeOffsetX;
        var cornerOffsetY = CornerBadgeOffsetY;
        var cornerScale = CornerBadgeFontScale;
        var extraEnabled = ExtraBadgeEnabled;
        var extraOffsetX = ExtraBadgeOffsetX;
        var extraOffsetY = ExtraBadgeOffsetY;
        var extraScale = ExtraBadgeFontScale;

        var tempFormat = string.IsNullOrWhiteSpace(TempBadgeFormat) ? def.TempBadgeFormat : TempBadgeFormat.Trim();
        var uvFormat = string.IsNullOrWhiteSpace(CornerUvFormat) ? def.CornerUvFormat : CornerUvFormat.Trim();
        var rhFormat = string.IsNullOrWhiteSpace(CornerHumidityFormat) ? def.CornerHumidityFormat : CornerHumidityFormat.Trim();
        var extraFormat = string.IsNullOrWhiteSpace(ExtraBadgeFormat) ? def.ExtraBadgeFormat : ExtraBadgeFormat.Trim();

        if (string.Equals(newCity, Settings.City, StringComparison.OrdinalIgnoreCase))
        {
            Settings = Settings with
            {
                City = newCity,
                RefreshInterval = refresh,
                IconCornerMetric = IconCornerMetric,
                TempBadgeOffsetX = tempOffsetX,
                TempBadgeOffsetY = tempOffsetY,
                TempBadgeFontScale = tempScale,
                TempBadgeFormat = tempFormat,
                CornerBadgeOffsetX = cornerOffsetX,
                CornerBadgeOffsetY = cornerOffsetY,
                CornerBadgeFontScale = cornerScale,
                CornerUvFormat = uvFormat,
                CornerHumidityFormat = rhFormat,
                ExtraBadgeEnabled = extraEnabled,
                ExtraBadgeOffsetX = extraOffsetX,
                ExtraBadgeOffsetY = extraOffsetY,
                ExtraBadgeFontScale = extraScale,
                ExtraBadgeFormat = extraFormat,
                AutoStart = AutoStart,
                StartHidden = StartHidden,
                ThemeMode = ThemeMode,
                IconDisplayMode = IconDisplayMode,
            };

            _settingsStore.Save(Settings);
            SetStatus("设置已保存");
            _timer.Interval = Settings.RefreshInterval;
            RaisePropertyChanged(nameof(LocationText));
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
            _ = RefreshAsync(forceRefresh: true);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resolved = _pendingCityResolved ?? await _geocodingClient.ResolveAsync(newCity, cts.Token);
            if (resolved is null)
            {
                SetStatus($"未找到城市：{newCity}");
                return;
            }

            Settings = Settings with
            {
                City = newCity,
                Latitude = resolved.Latitude,
                Longitude = resolved.Longitude,
                RefreshInterval = refresh,
                IconCornerMetric = IconCornerMetric,
                TempBadgeOffsetX = tempOffsetX,
                TempBadgeOffsetY = tempOffsetY,
                TempBadgeFontScale = tempScale,
                TempBadgeFormat = tempFormat,
                CornerBadgeOffsetX = cornerOffsetX,
                CornerBadgeOffsetY = cornerOffsetY,
                CornerBadgeFontScale = cornerScale,
                CornerUvFormat = uvFormat,
                CornerHumidityFormat = rhFormat,
                ExtraBadgeEnabled = extraEnabled,
                ExtraBadgeOffsetX = extraOffsetX,
                ExtraBadgeOffsetY = extraOffsetY,
                ExtraBadgeFontScale = extraScale,
                ExtraBadgeFormat = extraFormat,
                AutoStart = AutoStart,
                StartHidden = StartHidden,
                ThemeMode = ThemeMode,
                IconDisplayMode = IconDisplayMode,
            };

            _settingsStore.Save(Settings);
            SetStatus("设置已保存，正在刷新…");
            _timer.Interval = Settings.RefreshInterval;
            RaisePropertyChanged(nameof(LocationText));
            WeatherUpdated?.Invoke(this, EventArgs.Empty);
            await RefreshAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            SetStatus($"城市定位失败：{ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        AppendLog(text);
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm} {text.Trim()}";
        UpdateLogs.Add(line);
        while (UpdateLogs.Count > 12)
        {
            UpdateLogs.RemoveAt(0);
        }

        RaisePropertyChanged(nameof(UpdateLogHeaderLine));
    }

    private void ToggleDayDetail(ForecastDayViewModel? day)
    {
        if (day is null)
        {
            if (SelectedForecastDay is null)
            {
                IsDayDetailVisible = false;
                return;
            }

            IsDayDetailVisible = !IsDayDetailVisible;
            if (IsDayDetailVisible)
            {
                UpdateSelectedDayDetail();
            }
            return;
        }

        if (SelectedForecastDay?.Date == day.Date)
        {
            IsDayDetailVisible = !IsDayDetailVisible;
            if (IsDayDetailVisible)
            {
                UpdateSelectedDayDetail();
            }
            return;
        }

        SelectedForecastDay = day;
        IsDayDetailVisible = true;
    }

    private void ToggleSettingsPanel()
    {
        IsSettingsPanelVisible = !IsSettingsPanelVisible;
    }

    private void ToggleLogPanel()
    {
        IsLogPanelVisible = !IsLogPanelVisible;
    }

    private void UpdateSelectedDayDetail()
    {
        TemperatureHourIcons.Clear();
        ChartMarkers.Clear();
        TemperatureChartGeometry = Geometry.Empty;
        HumidityChartGeometry = Geometry.Empty;

        if (SelectedForecastDay is null || Snapshot is null)
        {
            SelectedDayTempSummary = "—";
            SelectedDayHumiditySummary = "";
            return;
        }

        var hours = Snapshot.Hours;
        if (hours is null || hours.Count == 0)
        {
            SelectedDayTempSummary = "暂无 24 小时数据（请联网刷新一次）";
            SelectedDayHumiditySummary = "";
            return;
        }

        var date = SelectedForecastDay.Date;
        var dayHours = new List<WeatherHour>(24);
        foreach (var hour in hours)
        {
            if (DateOnly.FromDateTime(hour.Time.DateTime) != date)
            {
                continue;
            }

            dayHours.Add(hour);
            if (dayHours.Count >= 24)
            {
                break;
            }
        }

        if (dayHours.Count == 0)
        {
            var now = DateTimeOffset.Now;
            foreach (var hour in hours)
            {
                if (hour.Time < now)
                {
                    continue;
                }

                dayHours.Add(hour);
                if (dayHours.Count >= 24)
                {
                    break;
                }
            }
        }

        if (dayHours.Count == 0)
        {
            SelectedDayTempSummary = "暂无 24 小时数据（请联网刷新一次）";
            SelectedDayHumiditySummary = "";
            return;
        }

        const double w = 200.0;
        const double h = 42.0;
        var tempPoints = new PointCollection(dayHours.Count);
        var humidityPoints = new PointCollection(dayHours.Count);

        var fallbackTemp = Snapshot.Now.TemperatureC;
        foreach (var d in Snapshot.Days)
        {
            if (d.Date == date)
            {
                fallbackTemp = (d.TemperatureMinC + d.TemperatureMaxC) / 2.0;
                break;
            }
        }

        double? lastFilledTemp = null;
        int? lastFilledHumidity = null;
        var filledTemps = new double[dayHours.Count];
        var filledHumidities = new double[dayHours.Count];
        var minFilledTemp = double.PositiveInfinity;
        var maxFilledTemp = double.NegativeInfinity;
        var minFilledHumidity = double.PositiveInfinity;
        var maxFilledHumidity = double.NegativeInfinity;

        var minTemp = double.PositiveInfinity;
        var maxTemp = double.NegativeInfinity;
        var sumTemp = 0.0;
        var countTemp = 0;

        var minHumidity = double.PositiveInfinity;
        var maxHumidity = double.NegativeInfinity;
        var sumHumidity = 0.0;
        var countHumidity = 0;

        for (var i = 0; i < dayHours.Count; i++)
        {
            var rawTemp = dayHours[i].TemperatureC;
            var rawHumidity = dayHours[i].RelativeHumidityPercent;

            var filledTemp = rawTemp ?? lastFilledTemp ?? fallbackTemp;
            lastFilledTemp = filledTemp;
            filledTemps[i] = filledTemp;
            minFilledTemp = Math.Min(minFilledTemp, filledTemp);
            maxFilledTemp = Math.Max(maxFilledTemp, filledTemp);

            var filledHumidity = (double)(rawHumidity ?? lastFilledHumidity ?? 50);
            lastFilledHumidity = (int)filledHumidity;
            filledHumidities[i] = filledHumidity;
            minFilledHumidity = Math.Min(minFilledHumidity, filledHumidity);
            maxFilledHumidity = Math.Max(maxFilledHumidity, filledHumidity);

            if (rawTemp is not null)
            {
                minTemp = Math.Min(minTemp, rawTemp.Value);
                maxTemp = Math.Max(maxTemp, rawTemp.Value);
                sumTemp += rawTemp.Value;
                countTemp++;
            }

            if (rawHumidity is not null)
            {
                minHumidity = Math.Min(minHumidity, rawHumidity.Value);
                maxHumidity = Math.Max(maxHumidity, rawHumidity.Value);
                sumHumidity += rawHumidity.Value;
                countHumidity++;
            }
        }

        if (!double.IsFinite(minFilledTemp) || !double.IsFinite(maxFilledTemp))
        {
            SelectedDayTempSummary = "暂无 24 小时数据（请联网刷新一次）";
            SelectedDayHumiditySummary = "";
            return;
        }

        // 温度曲线范围（确保最小范围为10度，减少震幅）
        var actualTempRange = maxFilledTemp - minFilledTemp;
        const double minTempRange = 10.0;
        double paddedMinTemp, paddedMaxTemp;
        if (actualTempRange < minTempRange)
        {
            var center = (minFilledTemp + maxFilledTemp) / 2;
            paddedMinTemp = center - minTempRange / 2;
            paddedMaxTemp = center + minTempRange / 2;
        }
        else
        {
            var pad = actualTempRange * 0.08;
            paddedMinTemp = minFilledTemp - pad;
            paddedMaxTemp = maxFilledTemp + pad;
        }
        var rangeTemp = Math.Max(0.1, paddedMaxTemp - paddedMinTemp);

        // 湿度曲线范围（确保最小范围为50%，减少震幅）
        var actualHumidityRange = maxFilledHumidity - minFilledHumidity;
        const double minHumidityRange = 50.0;
        double paddedMinHumidity, paddedMaxHumidity;
        if (actualHumidityRange < minHumidityRange)
        {
            var center = (minFilledHumidity + maxFilledHumidity) / 2;
            paddedMinHumidity = center - minHumidityRange / 2;
            paddedMaxHumidity = center + minHumidityRange / 2;
        }
        else
        {
            var pad = actualHumidityRange * 0.08;
            paddedMinHumidity = minFilledHumidity - pad;
            paddedMaxHumidity = maxFilledHumidity + pad;
        }
        // 确保湿度范围在 0-100 之内
        if (paddedMinHumidity < 0)
        {
            paddedMaxHumidity -= paddedMinHumidity;
            paddedMinHumidity = 0;
        }
        if (paddedMaxHumidity > 100)
        {
            paddedMinHumidity -= (paddedMaxHumidity - 100);
            if (paddedMinHumidity < 0) paddedMinHumidity = 0;
            paddedMaxHumidity = 100;
        }
        var rangeHumidity = Math.Max(1.0, paddedMaxHumidity - paddedMinHumidity);

        // 记录最高最低点索引
        var minTempIndex = 0;
        var maxTempIndex = 0;
        var minHumidityIndex = 0;
        var maxHumidityIndex = 0;
        for (var i = 0; i < dayHours.Count; i++)
        {
            if (filledTemps[i] <= filledTemps[minTempIndex]) minTempIndex = i;
            if (filledTemps[i] >= filledTemps[maxTempIndex]) maxTempIndex = i;
            if (filledHumidities[i] <= filledHumidities[minHumidityIndex]) minHumidityIndex = i;
            if (filledHumidities[i] >= filledHumidities[maxHumidityIndex]) maxHumidityIndex = i;
        }

        for (var i = 0; i < dayHours.Count; i++)
        {
            var x = dayHours.Count <= 1 ? 0 : i * (w / (dayHours.Count - 1));

            var tempY = (paddedMaxTemp - filledTemps[i]) / rangeTemp * h;
            tempPoints.Add(new Point(x, tempY));

            var humidityY = (paddedMaxHumidity - filledHumidities[i]) / rangeHumidity * h;
            humidityPoints.Add(new Point(x, humidityY));
        }

        TemperatureChartGeometry = CreateSmoothGeometry(tempPoints);
        HumidityChartGeometry = CreateSmoothGeometry(humidityPoints);

        // 构建摘要
        if (countTemp > 0 && double.IsFinite(minTemp) && double.IsFinite(maxTemp))
        {
            var avgTemp = sumTemp / countTemp;
            SelectedDayTempSummary = $"温度 {minTemp:0.#}°C – {maxTemp:0.#}°C（平均 {avgTemp:0.#}°C）";
        }
        else
        {
            SelectedDayTempSummary = "暂无 24 小时数据";
        }

        if (countHumidity > 0 && double.IsFinite(minHumidity) && double.IsFinite(maxHumidity))
        {
            var avgHumidity = sumHumidity / countHumidity;
            // 湿度舒适度评价
            var humidityLevel = avgHumidity switch
            {
                < 30 => "偏干",
                < 40 => "略干",
                < 60 => "舒适",
                < 70 => "略湿",
                _ => "潮湿"
            };
            SelectedDayHumiditySummary = $"湿度 {minHumidity:0}% – {maxHumidity:0}%（平均 {avgHumidity:0}%，{humidityLevel}，参考40-60%）";
        }
        else
        {
            SelectedDayHumiditySummary = "";
        }

        // 计算当前时间在图表上的位置
        var currentTime = DateTimeOffset.Now;
        var selectedDate = SelectedForecastDay.Date;
        var isToday = selectedDate == DateOnly.FromDateTime(currentTime.DateTime);
        ShowCurrentTimeLine = isToday && dayHours.Count > 1;
        CurrentTimeLineX = -1;

        if (isToday && dayHours.Count > 1)
        {
            // 计算当前时间对应的 X 位置
            var currentHour = currentTime.Hour + currentTime.Minute / 60.0;
            var firstHour = dayHours[0].Time.Hour;
            var lastHour = dayHours[^1].Time.Hour;
            if (lastHour < firstHour) lastHour += 24;
            var hourSpan = lastHour - firstHour;
            if (hourSpan > 0)
            {
                var relativeHour = currentHour - firstHour;
                if (relativeHour >= 0 && relativeHour <= hourSpan)
                {
                    CurrentTimeLineX = relativeHour / hourSpan * w;
                }
            }
        }

        // 生成24小时天气图标行（每3小时一个）
        for (var i = 0; i < dayHours.Count; i += 3)
        {
            var hour = dayHours[i];
            var code = hour.WeatherCode ?? SelectedForecastDay.WeatherCode;
            var x = dayHours.Count <= 1 ? 0 : i * (w / (dayHours.Count - 1));
            var timeLabel = hour.Time.ToString("HH:mm");
            var tempLabel = hour.TemperatureC.HasValue ? $"{hour.TemperatureC.Value:0}°" : "—";
            var isCurrentHour = isToday && hour.Time.Hour == currentTime.Hour;
            TemperatureHourIcons.Add(new HourIcon(X: x, Y: 0, WeatherCode: code, TimeLabel: timeLabel, TempLabel: tempLabel, IsCurrentHour: isCurrentHour));
        }

        // 添加最高最低点标记（调整位置更靠近曲线）
        ChartMarkers.Clear();
        if (dayHours.Count > 1)
        {
            // 温度最高点（橙色）- 在曲线上方
            var maxTempX = maxTempIndex * (w / (dayHours.Count - 1));
            var maxTempY = (paddedMaxTemp - filledTemps[maxTempIndex]) / rangeTemp * h;
            ChartMarkers.Add(new ChartMarker(maxTempX, maxTempY - 1, $"{filledTemps[maxTempIndex]:0}°", "#FFFF8C00", true));

            // 温度最低点（橙色）- 在曲线下方
            var minTempX = minTempIndex * (w / (dayHours.Count - 1));
            var minTempY = (paddedMaxTemp - filledTemps[minTempIndex]) / rangeTemp * h;
            ChartMarkers.Add(new ChartMarker(minTempX, minTempY + 5, $"{filledTemps[minTempIndex]:0}°", "#FFFF8C00", false));

            // 湿度最高点（蓝色）- 在曲线上方
            var maxHumidityX = maxHumidityIndex * (w / (dayHours.Count - 1));
            var maxHumidityY = (paddedMaxHumidity - filledHumidities[maxHumidityIndex]) / rangeHumidity * h;
            ChartMarkers.Add(new ChartMarker(maxHumidityX, maxHumidityY - 1, $"{filledHumidities[maxHumidityIndex]:0}%", "#FF4382FF", true));

            // 湿度最低点（蓝色）- 在曲线下方
            var minHumidityX = minHumidityIndex * (w / (dayHours.Count - 1));
            var minHumidityY = (paddedMaxHumidity - filledHumidities[minHumidityIndex]) / rangeHumidity * h;
            ChartMarkers.Add(new ChartMarker(minHumidityX, minHumidityY + 5, $"{filledHumidities[minHumidityIndex]:0}%", "#FF4382FF", false));
        }
    }

    private static Geometry CreateSmoothGeometry(IList<Point> points)
    {
        if (points.Count < 2) return Geometry.Empty;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (int i = 0; i < points.Count - 1; i++)
            {
                var curr = points[i];
                var next = points[i + 1];
                // 使用水平切向的三次贝塞尔曲线
                var cp1 = new Point(curr.X + (next.X - curr.X) / 3, curr.Y);
                var cp2 = new Point(curr.X + 2 * (next.X - curr.X) / 3, next.Y);
                context.BezierTo(cp1, cp2, next, isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public sealed record HourIcon(double X, double Y, int WeatherCode, string TimeLabel, string TempLabel, bool IsCurrentHour);
    public sealed record ChartMarker(double X, double Y, string Label, string Color, bool IsMax);

    private void ScheduleCitySearch()
    {
        var q = (City ?? string.Empty).Trim();
        if (q.Length < 1)
        {
            CitySuggestions.Clear();
            return;
        }

        _citySearchTimer.Stop();
        _citySearchTimer.Start();
    }

    private async Task UpdateCitySuggestionsAsync()
    {
        var q = (City ?? string.Empty).Trim();
        if (q.Length < 1)
        {
            CitySuggestions.Clear();
            return;
        }

        _citySearchCts?.Cancel();
        _citySearchCts?.Dispose();
        _citySearchCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

        try
        {
            var list = await _geocodingClient.SearchAsync(q, count: 8, _citySearchCts.Token);
            if (_citySearchCts.IsCancellationRequested)
            {
                return;
            }

            CitySuggestions.Clear();
            foreach (var item in list)
            {
                CitySuggestions.Add(item);
            }
        }
        catch
        {
            // 搜索失败不打断用户输入
        }
    }

    private void ScheduleSettingsSave()
    {
        _settingsSavePending = true;
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void FlushSettingsSave()
    {
        _settingsSaveTimer.Stop();
        if (!_settingsSavePending)
        {
            return;
        }

        _settingsSavePending = false;
        _settingsStore.Save(Settings);
    }
}
