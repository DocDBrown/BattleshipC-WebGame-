using Battleship.Shared;
using Battleship.Web.Game;

namespace Battleship.Web.Ai;

public sealed class AiView
{
    private readonly HashSet<Coordinate> _shots = new();
    private readonly HashSet<Coordinate> _hits = new();
    private readonly HashSet<Coordinate> _misses = new();
    private readonly HashSet<Coordinate> _sunkCells = new();
    private readonly List<ShipKind> _remainingShips = Fleet.Ships.Select(s => s.Kind).ToList();

    public IReadOnlyCollection<Coordinate> Hits => _hits;
    public IReadOnlyCollection<Coordinate> Misses => _misses;
    public IReadOnlyCollection<Coordinate> SunkCells => _sunkCells;
    public IReadOnlyList<ShipKind> RemainingShips => _remainingShips;

    public bool IsTried(Coordinate coordinate) => _shots.Contains(coordinate);

    public bool IsUntried(Coordinate coordinate) => coordinate.IsValid && !_shots.Contains(coordinate);

    public List<Coordinate> Untried()
    {
        var cells = new List<Coordinate>(BoardCoords.Size * BoardCoords.Size);
        foreach (var cell in Coordinate.AllCells)
        {
            if (!_shots.Contains(cell))
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    public void RecordShot(Coordinate coordinate, ShotOutcome outcome, ShipKind? ship)
    {
        _shots.Add(coordinate);
        switch (outcome)
        {
            case ShotOutcome.MISS:
                _misses.Add(coordinate);
                break;
            case ShotOutcome.HIT:
                _hits.Add(coordinate);
                break;
            case ShotOutcome.SUNK:
            case ShotOutcome.GAME_OVER:
                _hits.Add(coordinate);
                if (ship is not null)
                {
                    ClearSunkShip(coordinate, Fleet.SizeOf(ship.Value));
                    _remainingShips.Remove(ship.Value);
                }

                break;
        }
    }

    private void ClearSunkShip(Coordinate coordinate, int size)
    {
        if (TryFindSunkSegment(coordinate, size, out var segment))
        {
            foreach (var cell in segment)
            {
                _hits.Remove(cell);
                _sunkCells.Add(cell);
            }

            return;
        }

        _hits.Remove(coordinate);
        _sunkCells.Add(coordinate);
    }

    private bool TryFindSunkSegment(Coordinate coordinate, int size, out List<Coordinate> segment)
    {
        List<Coordinate>? best = null;
        var bestHits = 0;

        for (var offset = 0; offset < size; offset++)
        {
            ConsiderSegment(coordinate, size, horizontal: true, offset, ref best, ref bestHits);
            ConsiderSegment(coordinate, size, horizontal: false, offset, ref best, ref bestHits);
        }

        segment = best ?? new List<Coordinate>();
        return bestHits == size;
    }

    private void ConsiderSegment(
        Coordinate coordinate,
        int size,
        bool horizontal,
        int offset,
        ref List<Coordinate>? best,
        ref int bestHits)
    {
        var row = horizontal ? coordinate.Row : coordinate.Row - offset;
        var col = horizontal ? coordinate.Col - offset : coordinate.Col;
        var cells = new List<Coordinate>(size);
        for (var i = 0; i < size; i++)
        {
            var cell = new Coordinate(row + (horizontal ? 0 : i), col + (horizontal ? i : 0));
            if (!cell.IsValid || _misses.Contains(cell) || _sunkCells.Contains(cell))
            {
                return;
            }

            cells.Add(cell);
        }

        var hits = cells.Count(_hits.Contains);
        if (hits > bestHits)
        {
            best = cells;
            bestHits = hits;
        }
    }
}
