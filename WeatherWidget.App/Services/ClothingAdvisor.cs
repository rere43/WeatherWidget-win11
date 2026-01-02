using WeatherWidget.App.Models;

namespace WeatherWidget.App.Services;

public sealed class ClothingAdvisor
{
    public HumidityLevel GetHumidityLevel(WeatherSnapshot snapshot)
    {
        var rh = snapshot.Now.RelativeHumidityPercent;
        if (rh is null)
        {
            return HumidityLevel.Unknown;
        }

        return rh.Value switch
        {
            < 35 => HumidityLevel.Dry,
            >= 70 => HumidityLevel.Humid,
            _ => HumidityLevel.Normal,
        };
    }

    public string GetSuggestion(WeatherSnapshot snapshot)
    {
        var now = snapshot.Now;
        var today = snapshot.Days.FirstOrDefault();

        var temp = now.TemperatureC;
        var pop = today?.PrecipitationProbabilityMaxPercent;
        var humidityLevel = GetHumidityLevel(snapshot);

        var layers = temp switch
        {
            < 0 => "很冷：羽绒服/厚外套 + 保暖内衣，注意手套围巾。",
            < 8 => "偏冷：厚外套/呢大衣 + 毛衣，注意保暖。",
            < 15 => "微凉：夹克/风衣 + 薄毛衣，早晚加一层。",
            < 22 => "舒适：长袖/薄外套即可。",
            < 28 => "偏热：短袖为主，注意防晒补水。",
            _ => "很热：清凉短袖/短裤，注意防晒和补水。",
        };

        var rain = pop is >= 50 ? " 可能有雨：带伞/雨衣更稳。" : "";
        var uv = now.UvIndex is >= 7 ? " 紫外线偏强：建议帽子/防晒霜。" : "";
        var humidity = humidityLevel switch
        {
            HumidityLevel.Dry => " 空气偏干：加一件薄外套防风，注意润唇/护手。",
            HumidityLevel.Humid => " 空气偏潮：优先透气速干，体感更闷热/更凉时可适当加层。",
            _ => "",
        };

        return layers + rain + uv + humidity;
    }
}
