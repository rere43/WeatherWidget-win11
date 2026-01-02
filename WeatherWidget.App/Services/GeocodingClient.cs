using System.Net.Http;
using System.Text.Json;

namespace WeatherWidget.App.Services;

public sealed record ResolvedLocation(double Latitude, double Longitude);
public sealed record GeoSuggestion(string DisplayName, double Latitude, double Longitude);

public sealed class GeocodingClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<ResolvedLocation?> ResolveAsync(string cityQuery, CancellationToken cancellationToken)
    {
        cityQuery = (cityQuery ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cityQuery))
        {
            return null;
        }

        var list = await SearchAsync(cityQuery, count: 1, cancellationToken);
        if (list.Count == 0)
        {
            return null;
        }

        var first = list[0];
        return new ResolvedLocation(first.Latitude, first.Longitude);
    }

    public async Task<IReadOnlyList<GeoSuggestion>> SearchAsync(string query, int count, CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GeoSuggestion>();
        }

        count = Math.Clamp(count, 1, 12);

        var url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(query)}" +
            $"&count={count}" +
            "&language=zh" +
            "&format=json";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GeoSuggestion>();
        }

        var list = new List<GeoSuggestion>(count);
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("latitude", out var lat) || !item.TryGetProperty("longitude", out var lon))
            {
                continue;
            }

            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var admin1 = item.TryGetProperty("admin1", out var a1) ? a1.GetString() : null;
            var country = item.TryGetProperty("country", out var c) ? c.GetString() : null;

            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(admin1))
            {
                parts.Add(admin1!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(country))
            {
                parts.Add(country!.Trim());
            }

            var display = parts.Count == 0 ? query : string.Join(" · ", parts);
            list.Add(new GeoSuggestion(display, lat.GetDouble(), lon.GetDouble()));

            if (list.Count >= count)
            {
                break;
            }
        }

        return list;
    }
}
