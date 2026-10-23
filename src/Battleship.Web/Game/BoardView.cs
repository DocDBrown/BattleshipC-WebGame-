using Battleship.Shared;

namespace Battleship.Web.Game;

public static class BoardView
{
    public static IReadOnlyList<CellView> Own(Board board)
    {
        var cells = new CellView[BoardCoords.Size * BoardCoords.Size];
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var coordinate = new Coordinate(row, col);
                var ship = board.ShipAt(coordinate);
                CellState state;
                if (board.TryGetShot(coordinate, out var outcome))
                {
                    if (outcome == ShotOutcome.MISS)
                    {
                        state = CellState.Miss;
                    }
                    else
                    {
                        state = ship is { IsSunk: true } ? CellState.Sunk : CellState.Hit;
                    }
                }
                else if (ship is not null)
                {
                    state = CellState.Ship;
                }
                else
                {
                    state = CellState.Empty;
                }

                cells[(row * BoardCoords.Size) + col] = new CellView(coordinate, state);
            }
        }

        return cells;
    }

    public static IReadOnlyList<CellView> Placement(Board board)
    {
        var cells = new CellView[BoardCoords.Size * BoardCoords.Size];
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var coordinate = new Coordinate(row, col);
                var state = board.ShipAt(coordinate) is not null ? CellState.Ship : CellState.Empty;
                cells[(row * BoardCoords.Size) + col] = new CellView(coordinate, state);
            }
        }

        return cells;
    }

    public static IReadOnlyList<CellView> Target(TargetBoard target)
    {
        var cells = new CellView[BoardCoords.Size * BoardCoords.Size];
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var coordinate = new Coordinate(row, col);
                CellState state;
                if (!target.Shots.TryGetValue(coordinate, out var outcome))
                {
                    state = CellState.Unknown;
                }
                else if (target.IsSunk(coordinate))
                {
                    state = CellState.Sunk;
                }
                else if (outcome == ShotOutcome.MISS)
                {
                    state = CellState.Miss;
                }
                else
                {
                    state = CellState.Hit;
                }

                cells[(row * BoardCoords.Size) + col] = new CellView(coordinate, state);
            }
        }

        return cells;
    }
}
