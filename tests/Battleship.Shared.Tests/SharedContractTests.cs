using System.Reflection;
using Xunit;

namespace Battleship.Shared.Tests;

public sealed class SharedContractTests
{
    [Fact]
    public void ServerAndClient_ReferenceSameProtocolVersion()
    {
        Assert.Equal(1, ProtocolInfo.Version);
        Assert.Equal(ProtocolInfo.Version, typeof(ProtocolInfo).GetField(nameof(ProtocolInfo.Version))!.GetRawConstantValue());
    }

    [Fact]
    public void ServerAndClient_ReferenceSameMaxMessageBytes()
    {
        Assert.Equal(4096, ProtocolInfo.MaxMessageBytes);
        Assert.Equal(ProtocolInfo.MaxMessageBytes, typeof(ProtocolInfo).GetField(nameof(ProtocolInfo.MaxMessageBytes))!.GetRawConstantValue());
    }

    [Fact]
    public void ServerRelayedFrame_DeserializesOnClient_ForEveryType()
    {
        var codec = new ProtocolCodec();
        var messages = new[]
        {
            ProtocolMessage.Hello(), ProtocolMessage.Ready(), ProtocolMessage.Shot(1, "C3"),
            ProtocolMessage.Result(1, ShotOutcome.MISS), ProtocolMessage.Chat("hi"),
            ProtocolMessage.Quit(), ProtocolMessage.Error(ProtocolErrorCode.FATAL, "x")
        };
        foreach (var message in messages)
        {
            var frame = codec.Serialize(message);
            Assert.True(codec.TryDeserialize(frame, out var parsed, out var error), error);
            Assert.Equal(message.Type, parsed!.Type);
        }
    }

    [Fact]
    public void ServerIssuedRoomCode_PassesSharedValidator()
    {
        var alphabet = ProtocolInfo.RoomCodeAlphabet;
        var code = new string(new[] { alphabet[0], alphabet[1], alphabet[2], alphabet[3], alphabet[4], alphabet[5] });
        Assert.True(ProtocolInfo.IsValidRoomCode(code));
        Assert.Equal(ProtocolInfo.RoomCodeLength, code.Length);
    }
}
