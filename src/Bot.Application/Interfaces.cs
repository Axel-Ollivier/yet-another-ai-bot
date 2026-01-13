using Bot.Domain;

namespace Bot.Application;

public interface IGptClient
{
    Task<GptResponse> GenerateAsync(GptRequest request, CancellationToken ct);
}

public interface IRateLimiter
{
    bool TryAcquire(string key, TimeSpan interval);
}

public interface IWeatherClient
{
    Task<WeatherInfo?> GetCurrentAsync(string location, CancellationToken ct = default);
}
