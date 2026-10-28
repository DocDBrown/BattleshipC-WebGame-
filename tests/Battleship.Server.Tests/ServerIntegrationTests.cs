using System.Net.WebSockets;
using System.Reflection;
using Battleship.Server.Api;
using Battleship.Server.Background;
using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class ServerCompositionTests
{
    [Fact]
    public void DependencyInjection_AllSingletonsResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<BattleshipOptions>();
        services.AddSingleton<OptionsProvider>();
        services.AddSingleton<ProtocolCodec>();
        services.AddSingleton<RoomRegistry>();
        services.AddSingleton<RoomCodeGenerator>();
        services.AddSingleton<IpRateLimiter>();
        services.AddSingleton<MessageRelay>();
        services.AddSingleton<RoomSocketHandler>();
        services.AddSingleton<RoomApi>();
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<OptionsProvider>());
        Assert.NotNull(provider.GetRequiredService<ProtocolCodec>());
        Assert.NotNull(provider.GetRequiredService<RoomRegistry>());
        Assert.NotNull(provider.GetRequiredService<RoomCodeGenerator>());
        Assert.NotNull(provider.GetRequiredService<IpRateLimiter>());
        Assert.NotNull(provider.GetRequiredService<MessageRelay>());
        Assert.NotNull(provider.GetRequiredService<RoomSocketHandler>());
        Assert.NotNull(provider.GetRequiredService<RoomApi>());
    }

    [Fact]
    public void Options_BoundFromConfigurationAndNormalized()
    {
        var values = new Dictionary<string, string?>
        {
            ["Battleship:MaxRooms"] = "0",
            ["Battleship:MessageBurst"] = "3",
            ["Battleship:RoomIdleTimeoutSeconds"] = "-1"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<BattleshipOptions>().Bind(configuration.GetSection(BattleshipOptions.SectionName));
        services.AddSingleton<OptionsProvider>();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OptionsProvider>().Get();
        Assert.Equal(1, options.MaxRooms);
        Assert.Equal(3, options.MessageBurst);
        Assert.Equal(1, options.RoomIdleTimeoutSeconds);
    }
}

public sealed class RelayIntegrationTests
{
    [Fact]
    public async Task HostToClient_ThroughRealParticipantQueues()
    {
        var (relay, room, host, client, hostSocket, clientSocket) = Setup();
        Assert.True(relay.Relay(room, host, ProtocolMessage.Shot(1, "D4")));
        await RealServerFactory.WaitForAsync(() => clientSocket.TextFrames.Count == 1);
        Assert.Contains("\"SHOT\"", clientSocket.TextFrames[0], StringComparison.Ordinal);
        Assert.Empty(hostSocket.TextFrames);
        await host.CompleteAsync(TimeSpan.FromSeconds(1));
        await client.CompleteAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ClientToHost_ThroughRealParticipantQueues()
    {
        var (relay, room, host, client, hostSocket, _) = Setup();
        Assert.True(relay.Relay(room, client, ProtocolMessage.Result(1, ShotOutcome.HIT)));
        await RealServerFactory.WaitForAsync(() => hostSocket.TextFrames.Count == 1);
        Assert.Contains("\"RESULT\"", hostSocket.TextFrames[0], StringComparison.Ordinal);
        await host.CompleteAsync(TimeSpan.FromSeconds(1));
        await client.CompleteAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task QuitThenDisconnect_PeerReceivesQuitBefore4001()
    {
        var (relay, room, host, client, _, clientSocket) = Setup();
        Assert.True(relay.Relay(room, host, ProtocolMessage.Quit()));
        client.EnqueueClose(4001, "peer disconnected");
        await RealServerFactory.WaitForAsync(() => clientSocket.Closes.Count == 1);
        Assert.Contains(clientSocket.TextFrames, frame => frame.Contains("\"QUIT\"", StringComparison.Ordinal));
        Assert.Equal(4001, (int)clientSocket.Closes[0].Status);
        await host.CompleteAsync(TimeSpan.FromSeconds(1));
        await client.CompleteAsync(TimeSpan.FromSeconds(1));
    }

    private static (MessageRelay Relay, Room Room, Participant Host, Participant Client, FakeWebSocket HostSocket, FakeWebSocket ClientSocket) Setup()
    {
        var codec = new ProtocolCodec();
        var relay = new MessageRelay(codec, new TestLogger<MessageRelay>());
        var room = TestData.Room();
        var hostSocket = new FakeWebSocket();
        var clientSocket = new FakeWebSocket();
        var host = TestData.Participant(RoomRole.Host, hostSocket);
        var client = TestData.Participant(RoomRole.Client, clientSocket);
        room.Assign(host);
        room.Assign(client);
        host.Start(new TestLogger<Participant>());
        client.Start(new TestLogger<Participant>());
        return (relay, room, host, client, hostSocket, clientSocket);
    }
}

public sealed class SocketHandlerIntegrationTests
{
    [Fact]
    public async Task RealRegistryAndRelay_HandshakeAndRelay()
    {
        var registry = new RoomRegistry();
        var limiter = new IpRateLimiter();
        var logger = new TestLogger<RoomSocketHandler>();
        var codec = new ProtocolCodec();
        var relay = new MessageRelay(codec, new TestLogger<MessageRelay>());
        var handler = new RoomSocketHandler(registry, TestData.Options(), limiter, relay, codec, logger);
        var room = new Room("ABC234", DateTimeOffset.UtcNow);
        registry.TryAdd(room);
        var peerSocket = new FakeWebSocket();
        var peer = TestData.Participant(RoomRole.Host, peerSocket);
        room.Assign(peer);
        peer.Start(new TestLogger<Participant>());
        var socket = new FakeWebSocket();
        socket.QueueText(codec.Serialize(ProtocolMessage.Hello()));
        socket.QueueClose();
        var (context, _) = TestData.WebSocketContext(socket, "ABC234", "client");
        await handler.HandleAsync(context, "ABC234");
        await RealServerFactory.WaitForAsync(() => peerSocket.TextFrames.Count >= 1);
        Assert.Contains("\"HELLO\"", peerSocket.TextFrames[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothDisconnectSimultaneously_RoomRemovedExactlyOnce()
    {
        var registry = new RoomRegistry();
        var logger = new TestLogger<RoomSocketHandler>();
        var codec = new ProtocolCodec();
        var relay = new MessageRelay(codec, new TestLogger<MessageRelay>());
        var handler = new RoomSocketHandler(registry, TestData.Options(), new IpRateLimiter(), relay, codec, logger);
        var room = new Room("ABC234", DateTimeOffset.UtcNow);
        registry.TryAdd(room);
        var hostSocket = new FakeWebSocket();
        var hostContext = TestData.WebSocketContext(hostSocket, "ABC234", "host").Context;
        var hostTask = handler.HandleAsync(hostContext, "ABC234");
        await RealServerFactory.WaitForAsync(() => room.Host is not null);
        var clientSocket = new FakeWebSocket();
        var clientContext = TestData.WebSocketContext(clientSocket, "ABC234", "client").Context;
        var clientTask = handler.HandleAsync(clientContext, "ABC234");
        await RealServerFactory.WaitForAsync(() => room.Client is not null);
        hostSocket.QueueClose();
        clientSocket.QueueClose();
        await Task.WhenAll(hostTask, clientTask);
        Assert.Equal(0, registry.Count);
        Assert.Single(logger.Messages, entry => entry.Contains("room_disposed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParticipantCompleteDuringWrite_NoUnhandledException()
    {
        var socket = new FakeWebSocket
        {
            SendHandler = async cancellationToken => await Task.Delay(5, cancellationToken)
        };
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        for (var i = 0; i < 100; i++)
        {
            participant.EnqueueText($"message-{i}");
        }

        var exception = await Record.ExceptionAsync(async () => await participant.CompleteAsync(TimeSpan.FromSeconds(3)));
        Assert.Null(exception);
        Assert.True(socket.Disposed);
    }
}

public sealed class ReaperIntegrationTests
{
    [Fact]
    public async Task IdleRoom_ExpiredAndRemovedFromRegistry()
    {
        var registry = new RoomRegistry();
        var reaper = new RoomReaper(registry, TestData.Options(new BattleshipOptions { RoomIdleTimeoutSeconds = 1 }), new IpRateLimiter(), new TestLogger<RoomReaper>());
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await SweepAsync(reaper);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task NeverConnectedRoom_ExpiredAfterIdleTimeout()
    {
        var registry = new RoomRegistry();
        var reaper = new RoomReaper(registry, TestData.Options(new BattleshipOptions { RoomIdleTimeoutSeconds = 1 }), new IpRateLimiter(), new TestLogger<RoomReaper>());
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow.AddSeconds(-10)));
        await SweepAsync(reaper);
        Assert.Null(registry.Get("ABC234"));
    }

    [Fact]
    public async Task ReaperAndDisconnectRace_NoDoubleDisposeOrLeak()
    {
        var registry = new RoomRegistry();
        var reaper = new RoomReaper(registry, TestData.Options(new BattleshipOptions { RoomMaxLifetimeMinutes = 1 }), new IpRateLimiter(), new TestLogger<RoomReaper>());
        var room = new Room("ABC234", DateTimeOffset.UtcNow.AddMinutes(-5));
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(RoomRole.Host, socket);
        room.Assign(participant);
        participant.Start(new TestLogger<Participant>());
        registry.TryAdd(room);
        await Task.WhenAll(SweepAsync(reaper), Task.Run(() => room.TryCloseIfEmpty()));
        Assert.Equal(0, registry.Count);
        await participant.CompleteAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, socket.DisposeCount);
    }

    private static async Task SweepAsync(RoomReaper reaper)
    {
        var method = typeof(RoomReaper).GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(reaper, null)!;
    }
}

public sealed class RateLimitIntegrationTests
{
    [Fact]
    public void ClientJoinWithMaxRetries_DoesNotExhaustConnectPermits()
    {
        var limiter = new IpRateLimiter();
        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("connect", "127.0.0.1", 20));
        }

        Assert.False(limiter.TryAcquire("connect", "127.0.0.1", 20));
    }

    [Fact]
    public void CreateAndConnectScopes_DoNotConsumeEachOther()
    {
        var limiter = new IpRateLimiter();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire("create", "127.0.0.1", 5));
        }

        Assert.False(limiter.TryAcquire("create", "127.0.0.1", 5));
        Assert.True(limiter.TryAcquire("connect", "127.0.0.1", 5));
    }
}
