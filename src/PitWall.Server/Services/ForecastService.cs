using System.Globalization;
using System.Text.Json.Nodes;

namespace PitWall.Server.Services;

/// <summary>
/// Hourly forecast for the circuit from Open-Meteo (free, no key). The live feed only reports
/// what the track weather is now; the forecast answers "will it rain for the race?".
/// </summary>
public sealed class ForecastService(HttpClient http, ScheduleService schedule)
{
    private readonly Dictionary<string, (DateTimeOffset At, JsonNode Body)> _cache = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<JsonNode?> GetForCurrentWeekendAsync(CancellationToken cancellation)
    {
        var weekend = await schedule.GetCurrentWeekendAsync(cancellation);
        if (weekend is null || (weekend.Latitude == 0 && weekend.Longitude == 0))
        {
            return null;
        }

        var key = $"{weekend.Season}-{weekend.Round}";
        await _gate.WaitAsync(cancellation);
        try
        {
            if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.At < TimeSpan.FromMinutes(30))
            {
                return hit.Body.DeepClone();
            }

            var lat = weekend.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            var lon = weekend.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={lat}&longitude={lon}" +
                      "&hourly=temperature_2m,precipitation_probability,precipitation,wind_speed_10m,weather_code" +
                      "&current=temperature_2m,precipitation,wind_speed_10m,weather_code" +
                      "&forecast_days=5&timezone=UTC&timeformat=unixtime";
            try
            {
                using var response = await http.GetAsync(url, cancellation);
                response.EnsureSuccessStatusCode();
                var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))!;
                body["weekend"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
                    new { weekend.Name, weekend.Locality, weekend.Country }, System.Text.Json.JsonSerializerOptions.Web));
                _cache[key] = (DateTimeOffset.UtcNow, body);
                return body.DeepClone();
            }
            catch (HttpRequestException) when (_cache.TryGetValue(key, out var stale))
            {
                return stale.Body.DeepClone();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
