using Xunit;

namespace Battleship.Server.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void ServerAssembly_Loads() => Assert.NotNull(new Battleship.Server.Rooms.RoomRegistry());
}
