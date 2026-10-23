namespace Battleship.Server.RateLimiting;

public sealed class TokenBucket
{
    private readonly object _sync = new();
    private readonly double _capacity;
    private readonly double _ratePerSecond;
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucket(int burst, double ratePerSecond)
    {
        _capacity = Math.Max(1, burst);
        _ratePerSecond = Math.Max(0.01, ratePerSecond);
        _tokens = _capacity;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    public bool TryTake()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _lastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + (elapsed * _ratePerSecond));
                _lastRefill = now;
            }

            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }
}
