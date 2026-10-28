using Xunit;

namespace Battleship.Shared.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void SharedAssembly_Loads() => Assert.Equal(1, ProtocolInfo.Version);
}
