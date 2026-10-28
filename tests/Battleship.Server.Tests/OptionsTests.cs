using Battleship.Server.Options;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class BattleshipOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new BattleshipOptions();
        Assert.Equal(500, options.MaxRooms);
        Assert.Equal(64, options.RoomCodeAttempts);
        Assert.Equal(30, options.CreateRoomPermitsPerMinute);
        Assert.Equal(120, options.ConnectPermitsPerMinute);
        Assert.Equal(30, options.KeepAliveSeconds);
        Assert.Equal(5, options.SendTimeoutSeconds);
        Assert.Equal(10, options.MessagesPerSecond);
        Assert.Equal(20, options.MessageBurst);
        Assert.Equal(30, options.ReaperIntervalSeconds);
        Assert.Equal(120, options.RoomIdleTimeoutSeconds);
        Assert.Equal(60, options.RoomMaxLifetimeMinutes);
    }

    [Fact]
    public void Normalized_ZeroOrNegative_ClampedToOne()
    {
        var options = new BattleshipOptions
        {
            MaxRooms = 0,
            RoomCodeAttempts = -2,
            CreateRoomPermitsPerMinute = 0,
            ConnectPermitsPerMinute = -1,
            KeepAliveSeconds = 0,
            SendTimeoutSeconds = -5,
            MessagesPerSecond = 0,
            MessageBurst = -1,
            ReaperIntervalSeconds = 0,
            RoomIdleTimeoutSeconds = -3,
            RoomMaxLifetimeMinutes = 0
        }.Normalized();
        Assert.All(new[]
        {
            options.MaxRooms,
            options.RoomCodeAttempts,
            options.CreateRoomPermitsPerMinute,
            options.ConnectPermitsPerMinute,
            options.KeepAliveSeconds,
            options.SendTimeoutSeconds,
            options.MessagesPerSecond,
            options.MessageBurst,
            options.ReaperIntervalSeconds,
            options.RoomIdleTimeoutSeconds,
            options.RoomMaxLifetimeMinutes
        }, value => Assert.Equal(1, value));
    }

    [Fact]
    public void Normalized_PositiveValues_Preserved()
    {
        var source = new BattleshipOptions { MaxRooms = 7, RoomCodeAttempts = 8, MessageBurst = 9 };
        var normalized = source.Normalized();
        Assert.Equal(7, normalized.MaxRooms);
        Assert.Equal(8, normalized.RoomCodeAttempts);
        Assert.Equal(9, normalized.MessageBurst);
    }

    [Fact]
    public void Normalized_ReturnsNewInstance()
    {
        var source = new BattleshipOptions();
        Assert.NotSame(source, source.Normalized());
    }

    [Fact]
    public void SectionName_IsBattleship() => Assert.Equal("Battleship", BattleshipOptions.SectionName);
}

public sealed class OptionsProviderTests
{
    [Fact]
    public void Get_ReturnsNormalizedCurrentValue()
    {
        var monitor = new TestOptionsMonitor<BattleshipOptions>(new BattleshipOptions { MaxRooms = 0 });
        var provider = new OptionsProvider(monitor);
        Assert.Equal(1, provider.Get().MaxRooms);
    }

    [Fact]
    public void Get_ReflectsConfigurationReload()
    {
        var monitor = new TestOptionsMonitor<BattleshipOptions>(new BattleshipOptions { MaxRooms = 5 });
        var provider = new OptionsProvider(monitor);
        Assert.Equal(5, provider.Get().MaxRooms);
        monitor.CurrentValue = new BattleshipOptions { MaxRooms = 6 };
        Assert.Equal(6, provider.Get().MaxRooms);
    }
}
