namespace Battleship.Web.Game;

public enum CellState
{
    Unknown,
    Empty,
    Ship,
    Hit,
    Miss,
    Sunk
}

public readonly record struct CellView(Coordinate Coordinate, CellState State);
