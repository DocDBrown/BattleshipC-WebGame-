using Xunit;

namespace Battleship.Shared.Tests;

public sealed class BoardCoordsTests
{
    [Fact]
    public void TryParse_AllHundredCells_RoundTripWithFormat()
    {
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                Assert.True(BoardCoords.TryParse(BoardCoords.Format(row, col), out var parsedRow, out var parsedCol));
                Assert.Equal(row, parsedRow);
                Assert.Equal(col, parsedCol);
            }
        }
    }

    [Theory]
    [InlineData(" a1 ", 0, 0)]
    [InlineData("j10", 9, 9)]
    public void TryParse_TrimsAndUppercasesInput(string text, int expectedRow, int expectedCol)
    {
        Assert.True(BoardCoords.TryParse(text, out var row, out var col));
        Assert.Equal(expectedRow, row);
        Assert.Equal(expectedCol, col);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrWhitespace_ReturnsFalseWithMinusOne(string? text)
    {
        Assert.False(BoardCoords.TryParse(text, out var row, out var col));
        Assert.Equal(-1, row);
        Assert.Equal(-1, col);
    }

    [Theory]
    [InlineData("K1")]
    [InlineData("@1")]
    [InlineData("[1")]
    public void TryParse_LetterOutsideAToJ_ReturnsFalse(string text) =>
        Assert.False(BoardCoords.TryParse(text, out _, out _));

    [Theory]
    [InlineData("A0")]
    [InlineData("A11")]
    public void TryParse_NumberZeroOrEleven_ReturnsFalse(string text) =>
        Assert.False(BoardCoords.TryParse(text, out _, out _));

    [Theory]
    [InlineData("A+1")]
    [InlineData("A-1")]
    public void TryParse_SignedNumber_ReturnsFalse(string text) =>
        Assert.False(BoardCoords.TryParse(text, out _, out _));

    [Fact]
    public void TryParse_LeadingZero_ReturnsFalse() =>
        Assert.False(BoardCoords.TryParse("A01", out _, out _));

    [Fact]
    public void TryParse_InnerWhitespace_ReturnsFalse() =>
        Assert.False(BoardCoords.TryParse("A 1", out _, out _));

    [Fact]
    public void TryParse_AgreesWithIsValidCoordinate_ForNormalizedInput()
    {
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var text = BoardCoords.Format(row, col);
                Assert.Equal(ProtocolInfo.IsValidCoordinate(text), BoardCoords.TryParse(text, out _, out _));
            }
        }

        Assert.False(BoardCoords.TryParse("A 1", out _, out _));
        Assert.False(ProtocolInfo.IsValidCoordinate("A 1"));
    }

    [Fact]
    public void InBounds_EdgesAndOutside()
    {
        Assert.True(BoardCoords.InBounds(0, 0));
        Assert.True(BoardCoords.InBounds(9, 9));
        Assert.False(BoardCoords.InBounds(-1, 0));
        Assert.False(BoardCoords.InBounds(0, -1));
        Assert.False(BoardCoords.InBounds(10, 9));
        Assert.False(BoardCoords.InBounds(9, 10));
    }

    [Fact]
    public void Format_DelegatesToProtocolInfo() =>
        Assert.Equal(ProtocolInfo.FormatCoordinate(4, 7), BoardCoords.Format(4, 7));
}
