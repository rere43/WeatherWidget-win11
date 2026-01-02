namespace WeatherWidget.App.Services;

public enum WeatherCondition
{
    Unknown = 0,
    Clear = 1,
    Cloudy = 2,
    Rain = 3,
    Snow = 4,
    Thunder = 5,
}

public static class WeatherCodeMapper
{
    public static WeatherCondition ToCondition(int code)
    {
        return code switch
        {
            0 => WeatherCondition.Clear,
            1 or 2 or 3 => WeatherCondition.Cloudy,
            45 or 48 => WeatherCondition.Cloudy,
            51 or 53 or 55 or 56 or 57 => WeatherCondition.Rain,
            61 or 63 or 65 or 66 or 67 => WeatherCondition.Rain,
            71 or 73 or 75 or 77 => WeatherCondition.Snow,
            80 or 81 or 82 => WeatherCondition.Rain,
            85 or 86 => WeatherCondition.Snow,
            95 or 96 or 99 => WeatherCondition.Thunder,
            _ => WeatherCondition.Unknown,
        };
    }
}

