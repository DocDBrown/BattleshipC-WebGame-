using Battleship.Shared;
using Battleship.Web.Game;
using Xunit;

namespace Battleship.Web.Tests.Infrastructure;

internal static class TestBoards
{
    public static readonly (ShipKind Kind, Coordinate Start, bool Horizontal)[] Layout =
    {
        (ShipKind.CARRIER, new Coordinate(0, 0), true),
        (ShipKind.BATTLESHIP, new Coordinate(1, 0), true),
        (ShipKind.CRUISER, new Coordinate(2, 0), true),
        (ShipKind.SUBMARINE, new Coordinate(3, 0), true),
        (ShipKind.PATROL_BOAT, new Coordinate(4, 0), true)
    };

    public static Board Fleet()
    {
        var board = new Board();
        foreach (var (kind, start, horizontal) in Layout)
        {
            Assert.True(board.TryPlace(kind, start, horizontal));
        }

        return board;
    }

    public static void Sink(Board board, ShipKind kind)
    {
        var resolver = new ShotResolver();
        var ship = board.Ships.Single(ship => ship.Kind == kind);
        foreach (var cell in ship.Cells)
        {
            resolver.Resolve(board, cell);
        }
    }
}
