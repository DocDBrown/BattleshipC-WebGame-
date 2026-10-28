using System.Net.WebSockets;
using System.Text;
using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomSocketHandlerTests
{
    private const string Code = "ABC234";

    private sealed class Harness
    {
        public required RoomSocketHandler Handler { get; init; }
        public required RoomRegistry Registry { get; init; }
        public required IpRateLimiter Limiter { get; init; }
        public required TestLogger<RoomSocketHandler> Logger { get; init; }
        public required FakeWebSocket Socket { get; init; }
        public required DefaultHttpContext Context { get; init; }
        public required FakeWebSocketFeature Feature { get; init; }
        public required string RoomCode { get; init; }

        public Task RunAsync() => Handler.HandleAsync(Context, RoomCode);
    }

    private static Harness CreateHarness(string? role = "host", BattleshipOptions? options = null, string roomCode = Code, FakeWebSocket? socket = null, bool isWebSocketRequest = true, bool acceptThrows = false)
    {
        var registry = new RoomRegistry();
        var limiter = new IpRateLimiter();
        var logger = new TestLogger<RoomSocketHandler>();
        var codec = new ProtocolCodec();
        var relay = new MessageRelay(codec, new TestLogger<MessageRelay>());
        var handler = new RoomSocketHandler(registry, TestData.Options(options), limiter, relay, codec, logger);
        socket ??= new FakeWebSocket();
        var (context, feature) = TestData.WebSocketContext(socket, roomCode, role);
        feature.IsWebSocketRequest = isWebSocketRequest;
        feature.AcceptThrows = acceptThrows;
        return new Harness
        {
            Handler = handler,
            Registry = registry,
            Limiter = limiter,
            Logger = logger,
            Socket = socket,
            Context = context,
            Feature = feature,
            RoomCode = roomCode
        };
    }

    private static Room AddRoom(Harness harness, string code = Code, bool closed = false)
    {
        var room = new Room(code, DateTimeOffset.UtcNow);
        if (closed)
        {
            room.TryMarkClosed();
        }

        harness.Registry.TryAdd(room);
        return room;
    }

    private static Participant AddPeer(Harness harness, Room room, RoomRole role, out FakeWebSocket peerSocket)
    {
        peerSocket = new FakeWebSocket();
        var peer = TestData.Participant(role, peerSocket);
        room.Assign(peer);
        peer.Start(new TestLogger<Participant>());
        return peer;
    }

    [Fact]
    public async Task Handle_NonWebSocketRequest_Returns400Problem()
    {
        var harness = CreateHarness(isWebSocketRequest: false);
        await harness.RunAsync();
        Assert.Equal(400, harness.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", harness.Context.Response.ContentType);
    }

    [Fact]
    public async Task Handle_UpgradeFails_LogsWarning()
    {
        var harness = CreateHarness(acceptThrows: true);
        await harness.RunAsync();
        Assert.Contains(harness.Logger.Messages, entry => entry.Contains("websocket_upgrade_failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ConnectRateExceeded_Closes4029()
    {
        var harness = CreateHarness(options: new BattleshipOptions { ConnectPermitsPerMinute = 1 });
        Assert.True(harness.Limiter.TryAcquire("connect", "127.0.0.1", 1));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4029);
    }

    [Fact]
    public async Task Handle_RateLimitCheckedBeforeRoomLookup()
    {
        var harness = CreateHarness(options: new BattleshipOptions { ConnectPermitsPerMinute = 1 });
        Assert.True(harness.Limiter.TryAcquire("connect", "127.0.0.1", 1));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4029);
        Assert.DoesNotContain(harness.Socket.Closes, close => (int)close.Status == 4004);
    }

    [Fact]
    public async Task Handle_InvalidRoomCode_Closes4400()
    {
        var harness = CreateHarness(roomCode: "bad1");
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4400);
    }

    [Fact]
    public async Task Handle_MissingOrInvalidRole_Closes4400()
    {
        var harness = CreateHarness(role: null);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4400);
    }

    [Fact]
    public async Task Handle_RoleQueryCaseInsensitive()
    {
        var harness = CreateHarness(role: "HoSt");
        AddRoom(harness);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Logger.Messages, entry => entry.Contains("room_joined", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_UnknownRoom_Closes4004()
    {
        var harness = CreateHarness();
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4004);
    }

    [Fact]
    public async Task Handle_ClientBeforeHost_Closes4004()
    {
        var harness = CreateHarness(role: "client");
        AddRoom(harness);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4004);
    }

    [Fact]
    public async Task Handle_ClosedRoom_Closes4004()
    {
        var harness = CreateHarness();
        AddRoom(harness, closed: true);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4004);
    }

    [Fact]
    public async Task Handle_SlotTaken_Closes4009AndLogs()
    {
        var harness = CreateHarness();
        var room = AddRoom(harness);
        room.Assign(TestData.Participant(RoomRole.Host));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 4009);
        Assert.Contains(harness.Logger.Messages, entry => entry.Contains("room_full_rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_SlotTaken_RoomUnchanged()
    {
        var harness = CreateHarness();
        var room = AddRoom(harness);
        var existing = TestData.Participant(RoomRole.Host);
        room.Assign(existing);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Equal(1, room.ParticipantCount);
        Assert.Same(existing, room.Host);
    }

    [Fact]
    public async Task Handle_Joined_LogsRoomJoined()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Logger.Messages, entry => entry.Contains("room_joined", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_BinaryFrame_Closes1003()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueBytes(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 1003);
    }

    [Fact]
    public async Task Receive_OversizedMessage_Closes1009()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueBytes(new byte[ProtocolInfo.MaxMessageBytes + 1]);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 1009);
    }

    [Fact]
    public async Task Receive_OversizedOrBinary_NotRelayed()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        harness.Socket.QueueBytes(new byte[ProtocolInfo.MaxMessageBytes + 1]);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Empty(peerSocket.TextFrames);
    }

    [Fact]
    public async Task Receive_Exactly4096Bytes_Relayed()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        var codec = new ProtocolCodec();
        var json = codec.Serialize(ProtocolMessage.Hello());
        var padded = json + new string(' ', ProtocolInfo.MaxMessageBytes - Encoding.UTF8.GetByteCount(json));
        harness.Socket.QueueText(padded);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(peerSocket.TextFrames, frame => frame.Contains("\"HELLO\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_FragmentedMessage_Reassembled()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        var json = new ProtocolCodec().Serialize(ProtocolMessage.Hello());
        var half = json.Length / 2;
        harness.Socket.QueueBytes(Encoding.UTF8.GetBytes(json[..half]), endOfMessage: false);
        harness.Socket.QueueBytes(Encoding.UTF8.GetBytes(json[half..]));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(peerSocket.TextFrames, frame => frame.Contains("\"HELLO\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_RateLimited_SendsRateLimitedAndContinues()
    {
        var harness = CreateHarness(role: "client", options: new BattleshipOptions { MessageBurst = 1, MessagesPerSecond = 1 });
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out _);
        var json = new ProtocolCodec().Serialize(ProtocolMessage.Hello());
        harness.Socket.QueueText(json);
        harness.Socket.QueueText(json);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(harness.Socket.TextFrames, frame => frame.Contains("RATE_LIMITED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_RateLimitCheckedBeforeSchemaValidation()
    {
        var harness = CreateHarness(role: "client", options: new BattleshipOptions { MessageBurst = 1, MessagesPerSecond = 1 });
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out _);
        harness.Socket.QueueText("{bad");
        harness.Socket.QueueText("{bad");
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(harness.Socket.TextFrames, frame => frame.Contains("RATE_LIMITED", StringComparison.Ordinal));
        Assert.Single(harness.Socket.TextFrames, frame => frame.Contains("VALIDATION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_SchemaInvalid_SendsValidationAndContinues()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        harness.Socket.QueueText("{bad");
        harness.Socket.QueueText(new ProtocolCodec().Serialize(ProtocolMessage.Hello()));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(harness.Socket.TextFrames, frame => frame.Contains("VALIDATION", StringComparison.Ordinal));
        Assert.Contains(peerSocket.TextFrames, frame => frame.Contains("\"HELLO\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_SchemaInvalid_LogContainsNoPayload()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueText("{\"secret\":\"payload-secret\"");
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.DoesNotContain(harness.Logger.Messages, entry => entry.Contains("payload-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_InvalidUtf8_SendsValidationAndContinues()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out _);
        harness.Socket.QueueBytes(new byte[] { 0xFF, 0xFE });
        harness.Socket.QueueText(new ProtocolCodec().Serialize(ProtocolMessage.Hello()));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(harness.Socket.TextFrames, frame => frame.Contains("VALIDATION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Receive_RelayReturnsFalse_StopsLoop()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        var message = ProtocolMessage.Hello();
        message.V = 2;
        harness.Socket.QueueText(new ProtocolCodec().Serialize(message));
        harness.Socket.QueueText(new ProtocolCodec().Serialize(ProtocolMessage.Chat("after")));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(harness.Socket.TextFrames, frame => frame.Contains("VERSION_MISMATCH", StringComparison.Ordinal));
        Assert.Empty(peerSocket.TextFrames);
    }

    [Fact]
    public async Task Receive_UnexpectedException_Closes1011()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueException(new InvalidOperationException("boom"));
        await harness.RunAsync();
        Assert.Contains(harness.Socket.Closes, close => (int)close.Status == 1011);
    }

    [Fact]
    public async Task Disconnect_NotifiesPeerWith4001()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(50);
        Assert.Contains(peerSocket.Closes, close => (int)close.Status == 4001);
    }

    [Fact]
    public async Task Disconnect_PendingQuitDeliveredBefore4001()
    {
        var harness = CreateHarness(role: "client");
        var room = AddRoom(harness);
        AddPeer(harness, room, RoomRole.Host, out var peerSocket);
        harness.Socket.QueueText(new ProtocolCodec().Serialize(ProtocolMessage.Quit()));
        harness.Socket.QueueClose();
        await harness.RunAsync();
        await Task.Delay(100);
        Assert.Contains(peerSocket.TextFrames, frame => frame.Contains("\"QUIT\"", StringComparison.Ordinal));
        Assert.Contains(peerSocket.Closes, close => (int)close.Status == 4001);
    }

    [Fact]
    public async Task Disconnect_LastParticipant_DisposesRoom()
    {
        var harness = CreateHarness();
        AddRoom(harness);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        Assert.Equal(0, harness.Registry.Count);
    }

    [Fact]
    public async Task Disconnect_AlreadyRemoved_NoOp()
    {
        var harness = CreateHarness();
        var room = AddRoom(harness);
        harness.Socket.QueueClose();
        await harness.RunAsync();
        var participant = TestData.Participant(RoomRole.Host, harness.Socket);
        var method = typeof(RoomSocketHandler).GetMethod("DisconnectAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(harness.Handler, new object[] { room, participant, new BattleshipOptions() })!;
        Assert.Equal(0, harness.Registry.Count);
    }
}
