using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class MessageRelayTests
{
    private static (MessageRelay Relay, Room Room, Participant Sender, Participant Peer, FakeWebSocket SenderSocket, FakeWebSocket PeerSocket, TestLogger<MessageRelay> Logger) Setup()
    {
        var codec = new ProtocolCodec();
        var logger = new TestLogger<MessageRelay>();
        var relay = new MessageRelay(codec, logger);
        var room = TestData.Room();
        var senderSocket = new FakeWebSocket();
        var peerSocket = new FakeWebSocket();
        var sender = TestData.Participant(RoomRole.Host, senderSocket);
        var peer = TestData.Participant(RoomRole.Client, peerSocket);
        room.Assign(sender);
        room.Assign(peer);
        sender.Start(new TestLogger<Participant>());
        peer.Start(new TestLogger<Participant>());
        return (relay, room, sender, peer, senderSocket, peerSocket, logger);
    }

    [Fact]
    public async Task Relay_WrongVersion_SendsVersionMismatchAndClose1002()
    {
        var (relay, room, sender, _, senderSocket, _, _) = Setup();
        var message = ProtocolMessage.Hello();
        message.V = 2;
        Assert.False(relay.Relay(room, sender, message));
        await Task.Delay(150);
        Assert.Contains(senderSocket.TextFrames, frame => frame.Contains("VERSION_MISMATCH", StringComparison.Ordinal));
        Assert.Contains(senderSocket.Closes, close => (int)close.Status == 1002);
    }

    [Fact]
    public void Relay_WrongVersion_ReturnsFalseAndLogsWarning()
    {
        var (relay, room, sender, _, _, _, logger) = Setup();
        var message = ProtocolMessage.Hello();
        message.V = 2;
        Assert.False(relay.Relay(room, sender, message));
        Assert.Contains(logger.Messages, entry => entry.Contains("reason=version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Relay_NoPeer_SendsConflictAndReturnsTrue()
    {
        var codec = new ProtocolCodec();
        var logger = new TestLogger<MessageRelay>();
        var relay = new MessageRelay(codec, logger);
        var room = TestData.Room();
        var socket = new FakeWebSocket();
        var sender = TestData.Participant(RoomRole.Host, socket);
        room.Assign(sender);
        sender.Start(new TestLogger<Participant>());
        Assert.True(relay.Relay(room, sender, ProtocolMessage.Hello()));
        await Task.Delay(150);
        Assert.Contains(socket.TextFrames, frame => frame.Contains("CONFLICT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Relay_PeerPresent_ForwardsReserializedMessage()
    {
        var (relay, room, sender, _, _, peerSocket, _) = Setup();
        Assert.True(relay.Relay(room, sender, ProtocolMessage.Shot(5, "C4")));
        await Task.Delay(100);
        var frame = Assert.Single(peerSocket.TextFrames);
        Assert.True(new ProtocolCodec().TryDeserialize(frame, out var parsed, out _));
        Assert.Equal(MessageType.SHOT, parsed!.Type);
        Assert.Equal(5, parsed.Seq);
        Assert.Equal("C4", parsed.Coord);
    }

    [Fact]
    public void Relay_PeerPresent_TouchesRoom()
    {
        var (relay, room, sender, _, _, _, _) = Setup();
        var before = room.LastActivity;
        Thread.Sleep(5);
        relay.Relay(room, sender, ProtocolMessage.Chat("hi"));
        Assert.True(room.LastActivity > before);
    }

    [Fact]
    public void Relay_PeerPresent_LogsRelayedWithTypeAndSeq()
    {
        var (relay, room, sender, _, _, _, logger) = Setup();
        relay.Relay(room, sender, ProtocolMessage.Shot(9, "D5"));
        Assert.Contains(logger.Messages, entry => entry.Contains("message_relayed", StringComparison.Ordinal) && entry.Contains("SHOT", StringComparison.Ordinal) && entry.Contains("9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Relay_PeerUnavailable_SendsFatalAndReturnsFalse()
    {
        var (relay, room, sender, peer, senderSocket, _, _) = Setup();
        await peer.CompleteAsync(TimeSpan.FromSeconds(1));
        Assert.False(relay.Relay(room, sender, ProtocolMessage.Hello()));
        await Task.Delay(100);
        Assert.Contains(senderSocket.TextFrames, frame => frame.Contains("FATAL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Relay_NeverEchoesToSender()
    {
        var (relay, room, sender, _, senderSocket, _, _) = Setup();
        relay.Relay(room, sender, ProtocolMessage.Chat("hello"));
        await Task.Delay(100);
        Assert.Empty(senderSocket.TextFrames);
    }

    [Fact]
    public void Relay_LogsNeverContainChatTextOrCoordinates()
    {
        var (relay, room, sender, _, _, _, logger) = Setup();
        relay.Relay(room, sender, ProtocolMessage.Chat("topsecret"));
        relay.Relay(room, sender, ProtocolMessage.Shot(1, "A1"));
        Assert.DoesNotContain(logger.Messages, entry => entry.Contains("topsecret", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, entry => entry.Contains("A1", StringComparison.Ordinal));
    }
}
