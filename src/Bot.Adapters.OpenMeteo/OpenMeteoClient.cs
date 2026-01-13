using System.Text.Json;
using Bot.Application;
using Bot.Domain;
using Microsoft.Extensions.Logging;

namespace Bot.Adapters.OpenMeteo;

public sealed class OpenMeteoClient : IWeatherClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenMeteoClient> _logger;

    public OpenMeteoClient(HttpClient http, ILogger<OpenMeteoClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<WeatherInfo?> GetCurrentAsync(string location, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;

        var q = Uri.EscapeDataString(location);
        var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={q}&count=1&language=fr&format=json";

        using var geo = await _http.GetAsync(geoUrl, ct);
        if (!geo.IsSuccessStatusCode)
        {
            _logger.LogWarning("Open-Meteo geocoding failed: {Status}", (int)geo.StatusCode);
            return null;
        }

        await using var geoStream = await geo.Content.ReadAsStreamAsync(ct);
        using var geoDoc = await JsonDocument.ParseAsync(geoStream, cancellationToken: ct);
        if (!geoDoc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;

        var first = results[0];
        var lat = first.GetProperty("latitude").GetDouble();
        var lon = first.GetProperty("longitude").GetDouble();
        var city = first.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : location;
        var country = first.TryGetProperty("country", out var countryEl) ? countryEl.GetString() : null;
        var admin1 = first.TryGetProperty("admin1", out var adminEl) ? adminEl.GetString() : null;
        var place = string.Join(", ", new[] { city, admin1, country }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var meteoUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m&wind_speed_unit=kmh&timezone=auto";
        using var meteo = await _http.GetAsync(meteoUrl, ct);
        if (!meteo.IsSuccessStatusCode)
        {
            _logger.LogWarning("Open-Meteo forecast failed: {Status}", (int)meteo.StatusCode);
            return null;
        }

        await using var meteoStream = await meteo.Content.ReadAsStreamAsync(ct);
        using var meteoDoc = await JsonDocument.ParseAsync(meteoStream, cancellationToken: ct);
        var root = meteoDoc.RootElement;
        var current = root.GetProperty("current");
        var units = root.TryGetProperty("current_units", out var unitsEl) ? unitsEl : default;

        var temp = current.TryGetProperty("temperature_2m", out var tEl) ? tEl.GetDouble() : double.NaN;
        var wind = current.TryGetProperty("wind_speed_10m", out var wEl) ? wEl.GetDouble() : double.NaN;
        var code = current.TryGetProperty("weather_code", out var cEl) ? cEl.GetInt32() : -1;
        var tempUnit = units.ValueKind != JsonValueKind.Undefined && units.TryGetProperty("temperature_2m", out var tu) ? (tu.GetString() ?? "°C") : "°C";
        var windUnit = units.ValueKind != JsonValueKind.Undefined && units.TryGetProperty("wind_speed_10m", out var wu) ? (wu.GetString() ?? "km/h") : "km/h";

        return new WeatherInfo(place, lat, lon, temp, wind, code, tempUnit, windUnit);
    }
}
