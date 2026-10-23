using Battleship.Shared;

namespace Battleship.Web.Game;

public sealed class TargetBoard
{
    private readonly Dictionary<Coordinate, ShotOutcome> _shots = new();
    private readonly HashSet<Coordinate> _sunk = new();

    public IReadOnlyDictionary<Coordinate, ShotOutcome> Shots => _shots;

    public bool IsTried(Coordinate coordinate) => _shots.ContainsKey(coordinate);

    public bool IsSunk(Coordinate coordinate) => _sunk.Contains(coordinate);

    public void Record(Coordinate coordinate, ShotOutcome outcome) =>
        _shots[coordinate] = outcome;

    public void MarkSunk(IEnumerable<Coordinate> cells)
    {
        foreach (var cell in cells)
        {
            _sunk.Add(cell);
        }
    }

    public void MarkSunkLine(Coordinate coordinate, int size)
    {
        var best = FindSunkSegment(coordinate, size);
        if (best.Count > 0)
        {
            MarkSunk(best);
            return;
        }

        _sunk.Add(coordinate);
    }

    public void Clear()
    {
        _shots.Clear();
        _sunk.Clear();
    }

    private List<Coordinate> FindSunkSegment(Coordinate coordinate, int size)
    {
        List<Coordinate>? best = null;
        var bestHits = 0;

        for (var offset = 0; offset < size; offset++)
        {
            ConsiderSegment(coordinate, size, horizontal: true, offset, ref best, ref bestHits);
            ConsiderSegment(coordinate, size, horizontal: false, offset, ref best, ref bestHits);
        }

        return bestHits == size ? best! : new List<Coordinate>();
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
            if (!cell.IsValid || !IsHitLike(cell))
            {
                return;
            }

            cells.Add(cell);
        }

        var hits = cells.Count(IsHitLike);
        if (hits > bestHits)
        {
            best = cells;
            bestHits = hits;
        }
    }

    private bool IsHitLike(Coordinate coordinate) =>
        _shots.TryGetValue(coordinate, out var outcome) && outcome != ShotOutcome.MISS;
}
