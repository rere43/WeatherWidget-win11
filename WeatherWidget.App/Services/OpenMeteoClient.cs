using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class OpenMeteoClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<WeatherSnapshot> GetForecastAsync(string locationName, double latitude, double longitude, CancellationToken cancellationToken)
    {
        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,relative_humidity_2m,uv_index,weather_code" +
            "&hourly=temperature_2m,relative_humidity_2m,weather_code" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,uv_index_max,precipitation_probability_max" +
            "&timezone=auto";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"OpenMeteo GetForecastAsync failed for {locationName} ({latitude}, {longitude})", ex);
            throw;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;

        var fetchedAt = DateTimeOffset.UtcNow;
        var offsetSeconds = root.TryGetProperty("utc_offset_seconds", out var offsetEl) ? offsetEl.GetInt32() : 0;
        var offset = TimeSpan.FromSeconds(offsetSeconds);

        var current = root.GetProperty("current");
        var now = new WeatherNow(
            TemperatureC: current.GetProperty("temperature_2m").GetDouble(),
            WeatherCode: current.GetProperty("weather_code").GetInt32(),
            RelativeHumidityPercent: current.TryGetProperty("relative_humidity_2m", out var rh) ? rh.GetInt32() : null,
            UvIndex: current.TryGetProperty("uv_index", out var uv) ? uv.GetDouble() : null);

        var daily = root.GetProperty("daily");
        var dates = daily.GetProperty("time").EnumerateArray().Select(x => DateOnly.Parse(x.GetString()!)).ToArray();
        var codes = daily.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var maxs = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var mins = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var uvMax = daily.TryGetProperty("uv_index_max", out var uvIndexMax)
            ? uvIndexMax.EnumerateArray().Select(x => (double?)x.GetDouble()).ToArray()
            : Array.Empty<double?>();
        var popMax = daily.TryGetProperty("precipitation_probability_max", out var pop)
            ? pop.EnumerateArray().Select(x => (int?)x.GetInt32()).ToArray()
            : Array.Empty<int?>();

        var days = new List<WeatherDay>(Math.Min(5, dates.Length));
        for (var i = 0; i < dates.Length && i < 5; i++)
        {
            days.Add(
                new WeatherDay(
                    Date: dates[i],
                    WeatherCode: codes[i],
                    TemperatureMaxC: maxs[i],
                    TemperatureMinC: mins[i],
                     UvIndexMax: uvMax.Length > i ? uvMax[i] : null,
                     PrecipitationProbabilityMaxPercent: popMax.Length > i ? popMax[i] : null));
        }

        var hours = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var hourly) && hourly.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetString()).ToArray();
                var tempHourly = hourly.TryGetProperty("temperature_2m", out var tempEl)
                    ? tempEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? (double?)x.GetDouble() : null).ToArray()
                    : Array.Empty<double?>();
                var rhHourly = hourly.TryGetProperty("relative_humidity_2m", out var rhEl)
                    ? rhEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? (int?)x.GetInt32() : null).ToArray()
                    : Array.Empty<int?>();
                var codesHourly = hourly.TryGetProperty("weather_code", out var wcEl)
                    ? wcEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? (int?)x.GetInt32() : null).ToArray()
                    : Array.Empty<int?>();

                var n = times.Length;
                for (var i = 0; i < n; i++)
                {
                    var ts = times[i];
                    if (string.IsNullOrWhiteSpace(ts))
                    {
                        continue;
                    }

                    if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                    {
                        continue;
                    }

                    var t = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
                    hours.Add(
                        new WeatherHour(
                            Time: t,
                            RelativeHumidityPercent: rhHourly.Length > i ? rhHourly[i] : null,
                            WeatherCode: codesHourly.Length > i ? codesHourly[i] : null,
                            TemperatureC: tempHourly.Length > i ? tempHourly[i] : null));
                }
            }
            catch
            {
                // hourly 解析失败不影响主体展示
            }
        }

        return new WeatherSnapshot(locationName, latitude, longitude, fetchedAt, now, days, hours);
    }
}
