using Xunit;

namespace Battleship.Web.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void WebAssembly_Loads() => Assert.Equal(100, Battleship.Web.Game.Coordinate.AllCells.Count);
}
