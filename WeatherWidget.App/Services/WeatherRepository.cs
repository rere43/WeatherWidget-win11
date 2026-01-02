using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class WeatherRepository
{
    private readonly OpenMeteoClient _client;
    private readonly WeatherCacheStore _cacheStore;

    public WeatherRepository(OpenMeteoClient client, WeatherCacheStore cacheStore)
    {
        _client = client;
        _cacheStore = cacheStore;
    }

    public WeatherSnapshot? TryGetCached() => _cacheStore.TryLoad();

    public async Task<WeatherSnapshot> GetAsync(
        string locationName,
        double latitude,
        double longitude,
        TimeSpan refreshInterval,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cached = _cacheStore.TryLoad();
        if (!forceRefresh && cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < refreshInterval)
        {
            return cached;
        }

        var fresh = await _client.GetForecastAsync(locationName, latitude, longitude, cancellationToken);
        _cacheStore.Save(fresh);
        return fresh;
    }
}

