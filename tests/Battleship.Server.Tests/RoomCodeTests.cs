using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class OutboundTests
{
    [Fact]
    public void Message_HasTextAndNoClose()
    {
        var item = Outbound.Message("hello");
        Assert.Equal("hello", item.Text);
        Assert.Null(item.CloseCode);
        Assert.Null(item.Reason);
    }

    [Fact]
    public void Close_HasCodeReasonAndNoText()
    {
        var item = Outbound.Close(4001, "bye");
        Assert.Null(item.Text);
        Assert.Equal(4001, item.CloseCode);
        Assert.Equal("bye", item.Reason);
    }

    [Fact]
    public void Default_HasNeitherTextNorClose()
    {
        var item = default(Outbound);
        Assert.Null(item.Text);
        Assert.Null(item.CloseCode);
        Assert.Null(item.Reason);
    }
}

public sealed class RoomCodeGeneratorTests
{
    private readonly RoomCodeGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsValidRoomCode() =>
        Assert.All(Enumerable.Range(0, 100), _ => Assert.True(ProtocolInfo.IsValidRoomCode(_generator.Generate())));

    [Fact]
    public void Generate_UsesOnlyAlphabetCharacters()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.All(_generator.Generate(), ch => Assert.Contains(ch, ProtocolInfo.RoomCodeAlphabet));
        }
    }

    [Fact]
    public void Generate_DistributionIsUniform()
    {
        var counts = new Dictionary<char, int>();
        for (var i = 0; i < 20000; i++)
        {
            foreach (var ch in _generator.Generate())
            {
                counts[ch] = counts.GetValueOrDefault(ch) + 1;
            }
        }

        Assert.Equal(ProtocolInfo.RoomCodeAlphabet.Length, counts.Count);
        var expected = 20000 * ProtocolInfo.RoomCodeLength / (double)ProtocolInfo.RoomCodeAlphabet.Length;
        Assert.All(counts.Values, count => Assert.InRange(count, expected * 0.6, expected * 1.4));
    }

    [Fact]
    public void Generate_CollisionRateWithinExpectedBound()
    {
        var codes = Enumerable.Range(0, 5000).Select(_ => _generator.Generate()).ToHashSet();
        Assert.True(codes.Count > 4990);
    }
}
