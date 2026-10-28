using System.Text;
using Xunit;

namespace Battleship.Shared.Tests;

public sealed class ProtocolContractTests
{
    private readonly ProtocolCodec _codec = new();

    private static IEnumerable<ProtocolMessage> Samples()
    {
        yield return ProtocolMessage.Hello();
        yield return ProtocolMessage.Ready();
        yield return ProtocolMessage.Shot(1, "A1");
        yield return ProtocolMessage.Result(1, ShotOutcome.HIT);
        yield return ProtocolMessage.Result(2, ShotOutcome.SUNK, ShipKind.CARRIER);
        yield return ProtocolMessage.Result(3, ShotOutcome.GAME_OVER, ShipKind.PATROL_BOAT);
        yield return ProtocolMessage.Chat(new string('a', ProtocolInfo.ChatMaxLength));
        yield return ProtocolMessage.Quit();
        yield return ProtocolMessage.Error(ProtocolErrorCode.FATAL, new string('e', ProtocolInfo.ErrorDetailMaxLength));
    }

    [Fact]
    public void AllFactoryMessages_SerializeUnderMaxMessageBytes()
    {
        Assert.All(Samples(), message =>
            Assert.True(Encoding.UTF8.GetByteCount(_codec.Serialize(message)) < ProtocolInfo.MaxMessageBytes));
    }

    [Fact]
    public void MaxLengthChatOfFourByteChars_FitsMaxMessageBytes()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 100));
        var message = ProtocolMessage.Chat(text);
        Assert.True(ProtocolCodec.Validate(message, out _));
        Assert.True(Encoding.UTF8.GetByteCount(_codec.Serialize(message)) < ProtocolInfo.MaxMessageBytes);
    }

    [Fact]
    public void ErrorFactoryLongDetail_SerializesAndRevalidates()
    {
        var message = ProtocolMessage.Error(ProtocolErrorCode.VALIDATION, new string('x', 1000));
        var json = _codec.Serialize(message);
        Assert.True(_codec.TryDeserialize(json, out var parsed, out var error), error);
        Assert.Equal(ProtocolInfo.ErrorDetailMaxLength, parsed!.Detail!.Length);
    }

    [Fact]
    public void CoordinatePatternAndBoardCoords_AgreeOnFuzzedInputs()
    {
        var random = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var text = new string(new[] { (char)random.Next('A', 'L'), (char)random.Next('0', ':') });
            var valid = ProtocolInfo.IsValidCoordinate(text);
            var parsed = BoardCoords.TryParse(text, out _, out _);
            if (valid)
            {
                Assert.True(parsed);
            }
        }
    }

    [Fact]
    public void PropertyBased_ValidMessages_RoundTripThroughCodec()
    {
        var random = new Random(99);
        for (var i = 0; i < 300; i++)
        {
            var coordinate = ProtocolInfo.FormatCoordinate(random.Next(10), random.Next(10));
            var message = ProtocolMessage.Shot(random.Next(1, int.MaxValue), coordinate);
            Assert.True(_codec.TryDeserialize(_codec.Serialize(message), out var parsed, out var error), error);
            Assert.Equal(message.Seq, parsed!.Seq);
            Assert.Equal(message.Coord, parsed.Coord);
        }
    }

    [Fact]
    public void Fuzz_RandomBytes_TryDeserializeUtf8NeverThrows()
    {
        var random = new Random(123);
        for (var i = 0; i < 1000; i++)
        {
            var bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            Assert.Null(Record.Exception(() => _codec.TryDeserializeUtf8(bytes, out _, out _)));
        }
    }

    [Fact]
    public void RoomCodeAlphabetSampling_AlwaysPassesValidator()
    {
        var random = new Random(5);
        for (var i = 0; i < 500; i++)
        {
            var code = new string(Enumerable.Range(0, ProtocolInfo.RoomCodeLength)
                .Select(_ => ProtocolInfo.RoomCodeAlphabet[random.Next(ProtocolInfo.RoomCodeAlphabet.Length)])
                .ToArray());
            Assert.True(ProtocolInfo.IsValidRoomCode(code));
        }
    }
}
