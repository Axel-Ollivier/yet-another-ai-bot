using Bot.Application;

namespace Bot.Adapters.Discord;

public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly Dictionary<string, DateTimeOffset> _last = new();
    private readonly object _lock = new();

    public bool TryAcquire(string key, TimeSpan interval)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            if (_last.TryGetValue(key, out var last))
            {
                if (now - last < interval) return false;
            }
            _last[key] = now;
            return true;
        }
    }
}
