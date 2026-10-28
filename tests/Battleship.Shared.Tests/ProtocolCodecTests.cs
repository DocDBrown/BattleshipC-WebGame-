using System.Text;
using Xunit;

namespace Battleship.Shared.Tests;

public sealed class ProtocolCodecTests
{
    private readonly ProtocolCodec _codec = new();

    private static ProtocolMessage[] RoundTripMessages() =>
    [
        ProtocolMessage.Hello(),
        ProtocolMessage.Ready(),
        ProtocolMessage.Shot(7, "J10"),
        ProtocolMessage.Result(7, ShotOutcome.HIT),
        ProtocolMessage.Result(8, ShotOutcome.SUNK, ShipKind.CRUISER),
        ProtocolMessage.Result(9, ShotOutcome.GAME_OVER, ShipKind.PATROL_BOAT),
        ProtocolMessage.Chat("ahoy \U0001F600"),
        ProtocolMessage.Quit(),
        ProtocolMessage.Error(ProtocolErrorCode.RATE_LIMITED, "slow down")
    ];

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var json = _codec.Serialize(ProtocolMessage.Shot(3, "B4"));
        Assert.Equal("{\"v\":1,\"type\":\"SHOT\",\"seq\":3,\"coord\":\"B4\"}", json);
    }

    [Fact]
    public void Serialize_OmitsNullFields()
    {
        var json = _codec.Serialize(ProtocolMessage.Hello());
        Assert.DoesNotContain("seq", json, StringComparison.Ordinal);
        Assert.DoesNotContain("coord", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_EnumsAsStrings()
    {
        var json = _codec.Serialize(ProtocolMessage.Result(1, ShotOutcome.SUNK, ShipKind.CARRIER));
        Assert.Contains("\"SUNK\"", json, StringComparison.Ordinal);
        Assert.Contains("\"CARRIER\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_EachMessageType_Preserved()
    {
        foreach (var original in RoundTripMessages())
        {
            Assert.True(_codec.TryDeserialize(_codec.Serialize(original), out var parsed, out var error), error);
            Assert.NotNull(parsed);
            Assert.Equal(original.V, parsed!.V);
            Assert.Equal(original.Type, parsed.Type);
            Assert.Equal(original.Seq, parsed.Seq);
            Assert.Equal(original.Coord, parsed.Coord);
            Assert.Equal(original.Outcome, parsed.Outcome);
            Assert.Equal(original.Ship, parsed.Ship);
            Assert.Equal(original.Text, parsed.Text);
            Assert.Equal(original.Code, parsed.Code);
            Assert.Equal(original.Detail, parsed.Detail);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TryDeserialize_Empty_Rejected(string? text)
    {
        Assert.False(_codec.TryDeserialize(text!, out _, out _));
    }

    [Fact]
    public void TryDeserialize_JsonNullLiteral_Rejected() =>
        Assert.False(_codec.TryDeserialize("null", out _, out _));

    [Fact]
    public void TryDeserialize_Exactly4096Bytes_Accepted()
    {
        var json = _codec.Serialize(ProtocolMessage.Hello());
        var padded = json + new string(' ', ProtocolInfo.MaxMessageBytes - Encoding.UTF8.GetByteCount(json));
        Assert.Equal(ProtocolInfo.MaxMessageBytes, Encoding.UTF8.GetByteCount(padded));
        Assert.True(_codec.TryDeserialize(padded, out var message, out var error), error);
        Assert.Equal(MessageType.HELLO, message!.Type);
    }

    [Fact]
    public void TryDeserialize_Over4096Utf8Bytes_Rejected()
    {
        var padded = _codec.Serialize(ProtocolMessage.Hello()) + new string(' ', ProtocolInfo.MaxMessageBytes);
        Assert.False(_codec.TryDeserialize(padded, out _, out var error));
        Assert.Contains("maximum size", error);
    }

    [Fact]
    public void TryDeserialize_MultibyteCharsCountedAsBytes()
    {
        var text = "{\"v\":1,\"type\":\"CHAT\",\"text\":\"" + string.Concat(Enumerable.Repeat("\U0001F600", 1400)) + "\"}";
        Assert.True(text.Length < ProtocolInfo.MaxMessageBytes);
        Assert.True(Encoding.UTF8.GetByteCount(text) > ProtocolInfo.MaxMessageBytes);
        Assert.False(_codec.TryDeserialize(text, out _, out var error));
        Assert.Contains("maximum size", error);
    }

    [Fact]
    public void TryDeserialize_MalformedJson_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,", out _, out _));

    [Fact]
    public void TryDeserialize_UnknownProperty_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"HELLO\",\"extra\":1}", out _, out _));

    [Fact]
    public void TryDeserialize_PascalCaseProperty_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"V\":1,\"Type\":\"HELLO\"}", out _, out _));

    [Fact]
    public void TryDeserialize_DuplicateProperty_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"SHOT\",\"seq\":1,\"coord\":\"A1\",\"coord\":\"ZZ\"}", out _, out _));

    [Fact]
    public void TryDeserialize_IntegerEnumValue_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":2}", out _, out _));

    [Fact]
    public void TryDeserialize_UnknownEnumString_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"WAT\"}", out _, out _));

    [Fact]
    public void TryDeserialize_NonUppercaseEnumValue_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"hello\"}", out _, out _));

    [Fact]
    public void TryDeserialize_SeqExceedsInt32_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"SHOT\",\"seq\":2147483648,\"coord\":\"A1\"}", out _, out _));

    [Fact]
    public void TryDeserialize_NestedObjectInScalarField_Rejected() =>
        Assert.False(_codec.TryDeserialize("{\"v\":1,\"type\":\"SHOT\",\"seq\":1,\"coord\":{}}", out _, out _));

    [Fact]
    public void TryDeserialize_ArrayRoot_Rejected() =>
        Assert.False(_codec.TryDeserialize("[{\"v\":1,\"type\":\"HELLO\"}]", out _, out _));

    [Fact]
    public void TryDeserialize_ArbitraryInput_NeverThrows()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var chars = new char[random.Next(0, 80)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = (char)random.Next(1, 0xFFFF);
            }

            var exception = Record.Exception(() => _codec.TryDeserialize(new string(chars), out _, out _));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Validate_MissingV_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { Type = MessageType.HELLO }, out var error));
        Assert.Contains("v", error);
    }

    [Fact]
    public void Validate_MissingType_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1 }, out var error));
        Assert.Contains("type", error);
    }

    [Fact]
    public void Validate_DoesNotEnforceVersionValue()
    {
        Assert.True(ProtocolCodec.Validate(new ProtocolMessage { V = 999, Type = MessageType.HELLO }, out _));
    }

    [Fact]
    public void Validate_HelloReadyQuitWithSeq_Rejected()
    {
        foreach (var type in new[] { MessageType.HELLO, MessageType.READY, MessageType.QUIT })
        {
            Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = type, Seq = 1 }, out _));
        }
    }

    [Fact]
    public void Validate_HelloReadyQuitWithPayload_Rejected()
    {
        foreach (var type in new[] { MessageType.HELLO, MessageType.READY, MessageType.QUIT })
        {
            Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = type, Text = "x" }, out _));
        }
    }

    [Fact]
    public void Validate_ShotMissingOrNonPositiveSeq_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.SHOT, Coord = "A1" }, out _));
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.SHOT, Seq = 0, Coord = "A1" }, out _));
    }

    [Fact]
    public void Validate_ShotInvalidCoord_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.SHOT, Seq = 1, Coord = "A0" }, out _));
    }

    [Fact]
    public void Validate_ShotExtraFields_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.SHOT, Seq = 1, Coord = "A1", Outcome = ShotOutcome.HIT }, out _));
    }

    [Fact]
    public void Validate_ResultMissingOrNonPositiveSeq_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Outcome = ShotOutcome.HIT }, out _));
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = -1, Outcome = ShotOutcome.HIT }, out _));
    }

    [Fact]
    public void Validate_ResultMissingOutcome_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1 }, out _));
    }

    [Fact]
    public void Validate_ResultSunkOrGameOverWithoutShip_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1, Outcome = ShotOutcome.SUNK }, out _));
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1, Outcome = ShotOutcome.GAME_OVER }, out _));
    }

    [Fact]
    public void Validate_ResultHitOrMissWithShip_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1, Outcome = ShotOutcome.HIT, Ship = ShipKind.CARRIER }, out _));
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1, Outcome = ShotOutcome.MISS, Ship = ShipKind.CARRIER }, out _));
    }

    [Fact]
    public void Validate_ResultExtraFields_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.RESULT, Seq = 1, Outcome = ShotOutcome.HIT, Coord = "A1" }, out _));
    }

    [Fact]
    public void Validate_ChatInvalidText_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.CHAT, Text = "" }, out _));
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.CHAT, Text = new string('x', 201) }, out _));
    }

    [Fact]
    public void Validate_ChatExtraFields_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.CHAT, Text = "x", Seq = 1 }, out _));
    }

    [Fact]
    public void Validate_ErrorMissingCode_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.ERROR }, out _));
    }

    [Fact]
    public void Validate_ErrorDetailOver200_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.ERROR, Code = ProtocolErrorCode.FATAL, Detail = new string('x', 201) }, out _));
    }

    [Fact]
    public void Validate_ErrorDetailInvalidSurrogates_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.ERROR, Code = ProtocolErrorCode.FATAL, Detail = "a\uD800" }, out _));
    }

    [Fact]
    public void Validate_ErrorExtraFields_Rejected()
    {
        Assert.False(ProtocolCodec.Validate(new ProtocolMessage { V = 1, Type = MessageType.ERROR, Code = ProtocolErrorCode.FATAL, Text = "x" }, out _));
    }

    [Fact]
    public void TryDeserializeUtf8_InvalidUtf8_Rejected()
    {
        Assert.False(_codec.TryDeserializeUtf8(new byte[] { 0xFF, 0xFE }, out _, out var error));
        Assert.Contains("UTF-8", error);
    }

    [Fact]
    public void TryDeserializeUtf8_OverlongEncoding_Rejected()
    {
        var bytes = new byte[] { 0xC0, 0xAF };
        Assert.False(_codec.TryDeserializeUtf8(bytes, out _, out _));
    }

    [Fact]
    public void TryDeserializeUtf8_EncodedSurrogate_Rejected()
    {
        var bytes = new byte[] { 0xED, 0xA0, 0x80 };
        Assert.False(_codec.TryDeserializeUtf8(bytes, out _, out _));
    }

    [Fact]
    public void TryDeserializeUtf8_LeadingBom_Rejected()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(_codec.Serialize(ProtocolMessage.Hello()))).ToArray();
        Assert.False(_codec.TryDeserializeUtf8(bytes, out _, out _));
    }

    [Fact]
    public void TryDeserializeUtf8_Over4096Bytes_RejectedBeforeDecode()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, ProtocolInfo.MaxMessageBytes + 1).ToArray();
        Assert.False(_codec.TryDeserializeUtf8(bytes, out _, out var error));
        Assert.Contains("maximum size", error);
    }

    [Fact]
    public void Codec_ConcurrentSerializeDeserialize_IsThreadSafe()
    {
        var messages = RoundTripMessages();
        Parallel.For(0, 1000, i =>
        {
            var original = messages[i % messages.Length];
            var json = _codec.Serialize(original);
            Assert.True(_codec.TryDeserialize(json, out var parsed, out _));
            Assert.Equal(original.Type, parsed!.Type);
        });
    }
}
