using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Rooms;

namespace Battleship.Server.Background;

public sealed class RoomReaper : BackgroundService
{
    private readonly RoomRegistry _registry;
    private readonly OptionsProvider _options;
    private readonly IpRateLimiter _ipLimiter;
    private readonly ILogger<RoomReaper> _logger;

    public RoomReaper(
        RoomRegistry registry,
        OptionsProvider options,
        IpRateLimiter ipLimiter,
        ILogger<RoomReaper> logger)
    {
        _registry = registry;
        _options = options;
        _ipLimiter = ipLimiter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Get();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ReaperIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "room_reaper_failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
    }

    private async Task SweepAsync()
    {
        var options = _options.Get();
        var now = DateTimeOffset.UtcNow;
        var idleTimeout = TimeSpan.FromSeconds(options.RoomIdleTimeoutSeconds);
        var maxLifetime = TimeSpan.FromMinutes(options.RoomMaxLifetimeMinutes);

        foreach (var room in _registry.Snapshot())
        {
            if (!room.IsExpired(now, idleTimeout, maxLifetime))
            {
                continue;
            }

            if (!room.TryMarkClosed())
            {
                continue;
            }

            var participants = room.SnapshotParticipants();
            foreach (var participant in participants)
            {
                participant.EnqueueClose(1001, "room expired");
            }

            if (_registry.TryRemove(room))
            {
                _logger.LogInformation("room_expired room={RoomCode}", room.Code);
                _logger.LogInformation("room_disposed room={RoomCode}", room.Code);
            }

            foreach (var participant in participants)
            {
                await participant.CompleteAsync(TimeSpan.FromSeconds(options.SendTimeoutSeconds));
            }
        }

        _ipLimiter.Cleanup(TimeSpan.FromMinutes(10));
    }
}
