using System.Reflection;
using Battleship.Server.Background;
using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Rooms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomReaperTests
{
    private static (RoomReaper Reaper, RoomRegistry Registry, IpRateLimiter Limiter, TestLogger<RoomReaper> Logger) Create(BattleshipOptions? options = null)
    {
        var registry = new RoomRegistry();
        var limiter = new IpRateLimiter();
        var logger = new TestLogger<RoomReaper>();
        var reaper = new RoomReaper(registry, TestData.Options(options), limiter, logger);
        return (reaper, registry, limiter, logger);
    }

    private static async Task SweepAsync(RoomReaper reaper)
    {
        var method = typeof(RoomReaper).GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(reaper, null)!;
    }

    [Fact]
    public async Task Sweep_NonExpiredRoom_Untouched()
    {
        var (reaper, registry, _, _) = Create();
        var room = new Room("ABC234", DateTimeOffset.UtcNow);
        registry.TryAdd(room);
        await SweepAsync(reaper);
        Assert.Equal(1, registry.Count);
        Assert.False(room.IsClosed);
    }

    [Fact]
    public async Task Sweep_ExpiredRoom_SendsClose1001ToParticipants()
    {
        var (reaper, registry, _, _) = Create(new BattleshipOptions { RoomMaxLifetimeMinutes = 1 });
        var room = new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5));
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(RoomRole.Host, socket);
        room.Assign(participant);
        participant.Start(new TestLogger<Participant>());
        registry.TryAdd(room);
        await SweepAsync(reaper);
        Assert.Contains(socket.Closes, close => (int)close.Status == 1001);
    }

    [Fact]
    public async Task Sweep_ExpiredRoom_Close1001QueuedBeforeRemoval()
    {
        var (reaper, registry, _, _) = Create(new BattleshipOptions { RoomMaxLifetimeMinutes = 1 });
        var room = new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5));
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(RoomRole.Host, socket);
        room.Assign(participant);
        participant.Start(new TestLogger<Participant>());
        registry.TryAdd(room);
        await SweepAsync(reaper);
        Assert.Contains(socket.Closes, close => (int)close.Status == 1001);
        Assert.Null(registry.Get("ABC234"));
    }

    [Fact]
    public async Task Sweep_ExpiredRoom_RemovedAndLogged()
    {
        var (reaper, registry, _, logger) = Create(new BattleshipOptions { RoomIdleTimeoutSeconds = 1 });
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await SweepAsync(reaper);
        Assert.Equal(0, registry.Count);
        Assert.Contains(logger.Messages, entry => entry.Contains("room_expired", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, entry => entry.Contains("room_disposed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sweep_AlreadyClosedRoom_Skipped()
    {
        var (reaper, registry, _, _) = Create(new BattleshipOptions { RoomMaxLifetimeMinutes = 1 });
        var room = new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5));
        room.TryMarkClosed();
        registry.TryAdd(room);
        await SweepAsync(reaper);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task Sweep_CompletesParticipantsWithSendTimeout()
    {
        var (reaper, registry, _, _) = Create(new BattleshipOptions { RoomMaxLifetimeMinutes = 1, SendTimeoutSeconds = 1 });
        var room = new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5));
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(RoomRole.Host, socket);
        room.Assign(participant);
        participant.Start(new TestLogger<Participant>());
        registry.TryAdd(room);
        await SweepAsync(reaper);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task Sweep_InvokesIpLimiterCleanup()
    {
        var (reaper, registry, limiter, _) = Create();
        Assert.True(limiter.TryAcquire("create", "ip", 1));
        Assert.False(limiter.TryAcquire("create", "ip", 1));
        SetLastSeen(limiter, "create:ip", DateTimeOffset.UtcNow.AddMinutes(-11));
        await SweepAsync(reaper);
        Assert.True(limiter.TryAcquire("create", "ip", 1));
    }

    [Fact]
    public async Task Execute_SweepThrows_LogsErrorAndContinues()
    {
        var registry = new RoomRegistry();
        var limiter = new IpRateLimiter();
        var logger = new TestLogger<RoomReaper>();
        var monitor = new ThrowAfterFirstMonitor();
        var reaper = new RoomReaper(registry, new OptionsProvider(monitor), limiter, logger);
        using var cts = new CancellationTokenSource();
        await reaper.StartAsync(cts.Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!logger.Messages.Any(entry => entry.Contains("room_reaper_failed", StringComparison.Ordinal)))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Reaper did not log a sweep failure.");
            }

            await Task.Delay(20);
        }

        cts.Cancel();
        await reaper.StopAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, entry => entry.Contains("room_reaper_failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_Cancellation_StopsCleanly()
    {
        var (reaper, _, _, _) = Create(new BattleshipOptions { ReaperIntervalSeconds = 1 });
        using var cts = new CancellationTokenSource();
        await reaper.StartAsync(cts.Token);
        cts.Cancel();
        await reaper.StopAsync(CancellationToken.None);
        Assert.True(true);
    }

    private static void SetLastSeen(IpRateLimiter limiter, string key, DateTimeOffset lastSeen)
    {
        var field = typeof(IpRateLimiter).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var entry in (System.Collections.IEnumerable)field.GetValue(limiter)!)
        {
            var entryType = entry.GetType();
            if ((string)entryType.GetProperty("Key")!.GetValue(entry)! != key)
            {
                continue;
            }

            var window = entryType.GetProperty("Value")!.GetValue(entry)!;
            window.GetType().GetProperty("LastSeen")!.SetValue(window, lastSeen);
            return;
        }
    }

    private sealed class ThrowAfterFirstMonitor : IOptionsMonitor<BattleshipOptions>
    {
        private int _reads;
        public BattleshipOptions CurrentValue
        {
            get
            {
                if (Interlocked.Increment(ref _reads) > 1)
                {
                    throw new InvalidOperationException("configuration unavailable");
                }

                return new BattleshipOptions { ReaperIntervalSeconds = 1 };
            }
        }

        public BattleshipOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<BattleshipOptions, string?> listener) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
