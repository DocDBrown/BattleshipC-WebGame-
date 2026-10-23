using Battleship.Shared;

namespace Battleship.Web.Game;

public sealed class ShotResolver
{
    public ShotResult Resolve(Board board, Coordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate), "Coordinate is outside the board.");
        }

        if (board.IsTried(coordinate))
        {
            throw new InvalidOperationException("The coordinate has already been targeted.");
        }

        var ship = board.ShipAt(coordinate);
        if (ship is null)
        {
            board.RecordShot(coordinate, ShotOutcome.MISS);
            return new ShotResult(ShotOutcome.MISS, null, coordinate);
        }

        ship.Hits.Add(coordinate);
        var outcome = ship.IsSunk
            ? (board.AllSunk ? ShotOutcome.GAME_OVER : ShotOutcome.SUNK)
            : ShotOutcome.HIT;

        board.RecordShot(coordinate, outcome);
        return new ShotResult(outcome, ship.IsSunk ? ship.Kind : null, coordinate);
    }
}
