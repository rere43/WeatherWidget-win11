using System.IO;
using System.Text.Json;
using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class WeatherCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public WeatherCacheStore(string path)
    {
        _path = path;
    }

    public WeatherSnapshot? TryLoad()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<WeatherSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(WeatherSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
