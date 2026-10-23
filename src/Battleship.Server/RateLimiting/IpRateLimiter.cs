using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Battleship.Server.RateLimiting;

public sealed class IpRateLimiter
{
    private sealed class Window
    {
        public DateTimeOffset StartedAt { get; set; }
        public int Count { get; set; }
        public DateTimeOffset LastSeen { get; set; }
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public bool TryAcquire(HttpContext context, string scope, int permitsPerMinute)
    {
        return TryAcquire(scope, ClientKey(context), permitsPerMinute);
    }

    public bool TryAcquire(string scope, string key, int permitsPerMinute)
    {
        if (permitsPerMinute <= 0)
        {
            return false;
        }

        var bucketKey = $"{scope}:{key}";
        var now = DateTimeOffset.UtcNow;
        var window = _windows.GetOrAdd(bucketKey, _ => new Window { StartedAt = now, LastSeen = now });
        lock (window)
        {
            if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
            {
                window.StartedAt = now;
                window.Count = 0;
            }

            window.LastSeen = now;
            if (window.Count >= permitsPerMinute)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    public void Cleanup(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        foreach (var pair in _windows)
        {
            DateTimeOffset lastSeen;
            lock (pair.Value)
            {
                lastSeen = pair.Value.LastSeen;
            }

            if (lastSeen < cutoff)
            {
                _windows.TryRemove(pair.Key, out _);
            }
        }
    }

    public static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
