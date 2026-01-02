namespace WeatherWidget.App.Models;

public sealed record WeatherNow(
    double TemperatureC,
    int WeatherCode,
    int? RelativeHumidityPercent,
    double? UvIndex);

public sealed record WeatherDay(
    DateOnly Date,
    int WeatherCode,
    double TemperatureMaxC,
    double TemperatureMinC,
    double? UvIndexMax,
    int? PrecipitationProbabilityMaxPercent);

public sealed record WeatherHour(
    DateTimeOffset Time,
    int? RelativeHumidityPercent,
    int? WeatherCode,
    double? TemperatureC = null);

public sealed record WeatherSnapshot(
    string LocationName,
    double Latitude,
    double Longitude,
    DateTimeOffset FetchedAt,
    WeatherNow Now,
    IReadOnlyList<WeatherDay> Days,
    IReadOnlyList<WeatherHour>? Hours = null);
