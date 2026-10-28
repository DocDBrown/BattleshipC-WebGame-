using Xunit;

namespace Battleship.Shared.Tests;

public sealed class ProtocolMessageTests
{
    private static readonly ProtocolMessage[] Messages =
    {
        ProtocolMessage.Hello(),
        ProtocolMessage.Ready(),
        ProtocolMessage.Shot(1, "A1"),
        ProtocolMessage.Result(1, ShotOutcome.HIT),
        ProtocolMessage.Result(2, ShotOutcome.SUNK, ShipKind.CARRIER),
        ProtocolMessage.Chat("hello"),
        ProtocolMessage.Quit(),
        ProtocolMessage.Error(ProtocolErrorCode.VALIDATION, "bad")
    };

    [Fact]
    public void EveryFactory_SetsCurrentVersion() =>
        Assert.All(Messages, message => Assert.Equal(ProtocolInfo.Version, message.V));

    [Fact]
    public void EveryFactory_ProducesMessagePassingValidate() =>
        Assert.All(Messages, message => Assert.True(ProtocolCodec.Validate(message, out _)));

    [Fact]
    public void Result_SunkWithoutShip_FailsValidate()
    {
        var message = ProtocolMessage.Result(1, ShotOutcome.SUNK);
        Assert.False(ProtocolCodec.Validate(message, out var error));
        Assert.Contains("ship is required", error);
    }

    [Fact]
    public void Error_LongDetail_IsTruncated()
    {
        var message = ProtocolMessage.Error(ProtocolErrorCode.FATAL, new string('x', 500));
        Assert.Equal(ProtocolInfo.ErrorDetailMaxLength, message.Detail!.Length);
    }
}
