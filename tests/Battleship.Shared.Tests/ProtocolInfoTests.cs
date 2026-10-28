using System.Globalization;
using Xunit;

namespace Battleship.Shared.Tests;

public sealed class ProtocolInfoTests
{
    [Fact]
    public void Constants_VersionAndMaxMessageBytesMatchSpec()
    {
        Assert.Equal(1, ProtocolInfo.Version);
        Assert.Equal(4096, ProtocolInfo.MaxMessageBytes);
        Assert.Equal(200, ProtocolInfo.ChatMaxLength);
        Assert.Equal(200, ProtocolInfo.ErrorDetailMaxLength);
        Assert.Equal(6, ProtocolInfo.RoomCodeLength);
    }

    [Theory]
    [InlineData("ABC234")]
    [InlineData("ZZZZZZ")]
    [InlineData("HJNP29")]
    public void IsValidRoomCode_SixCharsFromAlphabet_ReturnsTrue(string code) =>
        Assert.True(ProtocolInfo.IsValidRoomCode(code));

    [Theory]
    [InlineData("ABC12I")]
    [InlineData("ABC12O")]
    [InlineData("ABC120")]
    [InlineData("ABC121")]
    public void IsValidRoomCode_ContainsAmbiguousIO01_ReturnsFalse(string code) =>
        Assert.False(ProtocolInfo.IsValidRoomCode(code));

    [Fact]
    public void IsValidRoomCode_Lowercase_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidRoomCode("abc234"));

    [Theory]
    [InlineData("ABC23")]
    [InlineData("ABC2345")]
    [InlineData("")]
    public void IsValidRoomCode_WrongLength_ReturnsFalse(string code) =>
        Assert.False(ProtocolInfo.IsValidRoomCode(code));

    [Fact]
    public void IsValidRoomCode_Null_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidRoomCode(null));

    [Fact]
    public void IsValidRoomCode_TrailingNewline_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidRoomCode("ABC234\n"));

    [Fact]
    public void RoomCodeAlphabet_EveryCharMatchesRoomCodePattern()
    {
        foreach (var ch in ProtocolInfo.RoomCodeAlphabet)
        {
            Assert.True(ProtocolInfo.IsValidRoomCode(new string(ch, ProtocolInfo.RoomCodeLength)));
        }
    }

    [Fact]
    public void RoomCodeAlphabet_HasNoDuplicateCharacters() =>
        Assert.Equal(ProtocolInfo.RoomCodeAlphabet.Length, ProtocolInfo.RoomCodeAlphabet.Distinct().Count());

    [Fact]
    public void RoomCodeLength_MatchesPatternLength()
    {
        Assert.Equal(6, ProtocolInfo.RoomCodeLength);
        Assert.Equal(ProtocolInfo.RoomCodePattern, $@"\A[A-HJ-NP-Z2-9]{{{ProtocolInfo.RoomCodeLength}}}\z");
    }

    [Fact]
    public void IsValidCoordinate_AllHundredCells_ReturnsTrue()
    {
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                Assert.True(ProtocolInfo.IsValidCoordinate(ProtocolInfo.FormatCoordinate(row, col)));
            }
        }
    }

    [Theory]
    [InlineData("K1")]
    [InlineData("A0")]
    [InlineData("A11")]
    [InlineData("@1")]
    [InlineData("J100")]
    public void IsValidCoordinate_OutOfRangeLetterOrNumber_ReturnsFalse(string coordinate) =>
        Assert.False(ProtocolInfo.IsValidCoordinate(coordinate));

    [Theory]
    [InlineData("A01")]
    [InlineData("A00")]
    public void IsValidCoordinate_LeadingZero_ReturnsFalse(string coordinate) =>
        Assert.False(ProtocolInfo.IsValidCoordinate(coordinate));

    [Theory]
    [InlineData("a1")]
    [InlineData(" A1")]
    [InlineData("A1 ")]
    [InlineData("A 1")]
    public void IsValidCoordinate_LowercaseOrWhitespace_ReturnsFalse(string coordinate) =>
        Assert.False(ProtocolInfo.IsValidCoordinate(coordinate));

    [Fact]
    public void IsValidChatText_OneAndTwoHundredChars_ReturnsTrue()
    {
        Assert.True(ProtocolInfo.IsValidChatText("a"));
        Assert.True(ProtocolInfo.IsValidChatText(new string('a', 200)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValidChatText_NullEmptyOr201Chars_ReturnsFalse(string? text)
    {
        Assert.False(ProtocolInfo.IsValidChatText(text));
        Assert.False(ProtocolInfo.IsValidChatText(new string('a', 201)));
    }

    [Theory]
    [InlineData("hello\nworld")]
    [InlineData("tab\there")]
    [InlineData("nul\0here")]
    public void IsValidChatText_ControlCharacter_ReturnsFalse(string text) =>
        Assert.False(ProtocolInfo.IsValidChatText(text));

    [Fact]
    public void IsValidChatText_EachBidiOverride_ReturnsFalse()
    {
        var overrides = new[] { '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069', '\u200E', '\u200F', '\u061C' };
        foreach (var ch in overrides)
        {
            Assert.False(ProtocolInfo.IsValidChatText($"a{ch}b"));
        }
    }

    [Fact]
    public void IsValidChatText_LoneHighSurrogate_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidChatText("a\uD83D"));

    [Fact]
    public void IsValidChatText_LoneLowSurrogate_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidChatText("a\uDE00"));

    [Fact]
    public void IsValidChatText_ReversedSurrogatePair_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsValidChatText("\uDE00\uD83D"));

    [Fact]
    public void IsValidChatText_ValidEmojiPair_ReturnsTrue() =>
        Assert.True(ProtocolInfo.IsValidChatText("ahoy \U0001F600"));

    [Fact]
    public void IsValidChatText_LengthMeasuredInUtf16Units()
    {
        Assert.True(ProtocolInfo.IsValidChatText(string.Concat(Enumerable.Repeat("\U0001F600", 100))));
        Assert.False(ProtocolInfo.IsValidChatText(string.Concat(Enumerable.Repeat("\U0001F600", 101))));
    }

    [Fact]
    public void IsBidiOverride_AllNineCodepoints_ReturnsTrue()
    {
        var overrides = new[] { '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069' };
        Assert.All(overrides, ch => Assert.True(ProtocolInfo.IsBidiOverride(ch)));
        Assert.True(ProtocolInfo.IsBidiOverride('\u200E'));
        Assert.True(ProtocolInfo.IsBidiOverride('\u200F'));
        Assert.True(ProtocolInfo.IsBidiOverride('\u061C'));
    }

    [Fact]
    public void IsBidiOverride_OrdinaryText_ReturnsFalse() =>
        Assert.False(ProtocolInfo.IsBidiOverride('A'));

    [Fact]
    public void FormatCoordinate_Origin_ReturnsA1() =>
        Assert.Equal("A1", ProtocolInfo.FormatCoordinate(0, 0));

    [Fact]
    public void FormatCoordinate_MaxCell_ReturnsJ10() =>
        Assert.Equal("J10", ProtocolInfo.FormatCoordinate(9, 9));

    [Fact]
    public void FormatCoordinate_UnderTurkishCulture_IsInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("J10", ProtocolInfo.FormatCoordinate(9, 9));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TruncateDetail_NullOrEmpty_ReturnsNull(string? detail) =>
        Assert.Null(ProtocolInfo.TruncateDetail(detail));

    [Fact]
    public void TruncateDetail_AtLimit_Unchanged()
    {
        var detail = new string('x', 200);
        Assert.Equal(detail, ProtocolInfo.TruncateDetail(detail));
    }

    [Fact]
    public void TruncateDetail_OverLimit_TruncatedTo200()
    {
        var detail = ProtocolInfo.TruncateDetail(new string('x', 201));
        Assert.Equal(200, detail!.Length);
    }

    [Fact]
    public void TruncateDetail_SurrogateAtBoundary_NotSplit()
    {
        var detail = new string('x', 199) + "\U0001F600";
        var truncated = ProtocolInfo.TruncateDetail(detail);
        Assert.Equal(199, truncated!.Length);
        Assert.False(ProtocolInfo.HasInvalidSurrogates(truncated));
    }

    [Fact]
    public void Enums_WireNames_AreStable()
    {
        Assert.Equal(new[] { "HELLO", "READY", "SHOT", "RESULT", "CHAT", "QUIT", "ERROR" }, Enum.GetNames<MessageType>());
        Assert.Equal(new[] { "HIT", "MISS", "SUNK", "GAME_OVER" }, Enum.GetNames<ShotOutcome>());
        Assert.Equal(new[] { "CARRIER", "BATTLESHIP", "CRUISER", "SUBMARINE", "PATROL_BOAT" }, Enum.GetNames<ShipKind>());
        Assert.Equal(new[] { "VALIDATION", "CONFLICT", "VERSION_MISMATCH", "RATE_LIMITED", "FATAL" }, Enum.GetNames<ProtocolErrorCode>());
    }
}
