using WeatherWidget.App.Models;
using WeatherWidget.App.Services;
using System.Windows.Media;

namespace WeatherWidget.App.ViewModels;

public sealed class ForecastDayViewModel
{
    private static readonly WeatherIconMapper IconMapper = new();

    public ForecastDayViewModel(WeatherDay day)
    {
        Date = day.Date;
        WeatherCode = day.WeatherCode;
        WeatherText = IconMapper.GetWeatherText(day.WeatherCode);
        TempRange = $"{Math.Round(day.TemperatureMinC):0}° / {Math.Round(day.TemperatureMaxC):0}°";
        PrecipitationProbability = day.PrecipitationProbabilityMaxPercent is null ? "—" : $"{day.PrecipitationProbabilityMaxPercent.Value}%";
        UvMax = day.UvIndexMax is null ? "—" : $"{Math.Round(day.UvIndexMax.Value):0}";
    }

    public DateOnly Date { get; }
    public int WeatherCode { get; }
    public string WeatherText { get; }
    public string TempRange { get; }
    public string PrecipitationProbability { get; }
    public string UvMax { get; }

    public string DayLabel
    {
        get
        {
            var dt = Date.ToDateTime(TimeOnly.MinValue);
            return dt.DayOfWeek switch
            {
                DayOfWeek.Monday => "周一",
                DayOfWeek.Tuesday => "周二",
                DayOfWeek.Wednesday => "周三",
                DayOfWeek.Thursday => "周四",
                DayOfWeek.Friday => "周五",
                DayOfWeek.Saturday => "周六",
                DayOfWeek.Sunday => "周日",
                _ => dt.DayOfWeek.ToString(),
            };
        }
    }
}
