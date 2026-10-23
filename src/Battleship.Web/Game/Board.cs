using Battleship.Shared;

namespace Battleship.Web.Game;

public sealed class Board
{
    private readonly Ship?[,] _ships = new Ship?[BoardCoords.Size, BoardCoords.Size];
    private readonly Dictionary<Coordinate, ShotOutcome> _shots = new();

    public List<Ship> Ships { get; } = new();
    public IReadOnlyDictionary<Coordinate, ShotOutcome> Shots => _shots;
    public bool AllShipsPlaced => Ships.Count == Fleet.Ships.Count;
    public bool AllSunk => Ships.Count == Fleet.Ships.Count && Ships.All(s => s.IsSunk);

    public Ship? ShipAt(Coordinate coordinate) =>
        coordinate.IsValid ? _ships[coordinate.Row, coordinate.Col] : null;

    public bool IsPlaced(ShipKind kind) => Ships.Any(s => s.Kind == kind);

    public bool IsTried(Coordinate coordinate) => _shots.ContainsKey(coordinate);

    public bool TryGetShot(Coordinate coordinate, out ShotOutcome outcome) =>
        _shots.TryGetValue(coordinate, out outcome);

    public void RecordShot(Coordinate coordinate, ShotOutcome outcome) =>
        _shots[coordinate] = outcome;

    public void Clear()
    {
        Array.Clear(_ships);
        Ships.Clear();
        _shots.Clear();
    }

    public bool TryPlace(ShipKind kind, Coordinate start, bool horizontal)
    {
        if (!start.IsValid || IsPlaced(kind))
        {
            return false;
        }

        var definition = Fleet.Get(kind);
        var cells = new List<Coordinate>(definition.Size);
        for (var i = 0; i < definition.Size; i++)
        {
            var row = start.Row + (horizontal ? 0 : i);
            var col = start.Col + (horizontal ? i : 0);
            if (!BoardCoords.InBounds(row, col))
            {
                return false;
            }

            var coordinate = new Coordinate(row, col);
            if (ShipAt(coordinate) is not null)
            {
                return false;
            }

            cells.Add(coordinate);
        }

        var ship = new Ship(definition, cells, horizontal);
        foreach (var cell in cells)
        {
            _ships[cell.Row, cell.Col] = ship;
        }

        Ships.Add(ship);
        return true;
    }

    public bool TryAutoPlaceAll(Random random)
    {
        for (var boardAttempt = 0; boardAttempt < 64; boardAttempt++)
        {
            Clear();
            var success = true;
            foreach (var definition in Fleet.Ships)
            {
                var placed = false;
                for (var attempt = 0; attempt < 400; attempt++)
                {
                    var horizontal = random.Next(2) == 0;
                    var start = new Coordinate(random.Next(BoardCoords.Size), random.Next(BoardCoords.Size));
                    if (TryPlace(definition.Kind, start, horizontal))
                    {
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    success = false;
                    break;
                }
            }

            if (success)
            {
                return true;
            }
        }

        Clear();
        return false;
    }
}
