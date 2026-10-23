using Battleship.Shared;

namespace Battleship.Web.Game;

public readonly record struct ShotResult(ShotOutcome Outcome, ShipKind? Ship, Coordinate Coordinate)
{
    public bool IsGameOver => Outcome == ShotOutcome.GAME_OVER;
}
