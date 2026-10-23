using Battleship.Shared;
using Battleship.Web.Game;

namespace Battleship.Web.Ai;

public sealed class HardAiStrategy : IAiStrategy
{
    private const int HitPlacementWeight = 5;
    private const int AdjacentHitBonus = 10;

    private readonly Random _random;

    public HardAiStrategy(Random random)
    {
        _random = random;
    }

    public Coordinate ChooseShot(AiView view)
    {
        var counts = new Dictionary<Coordinate, int>();

        foreach (var kind in view.RemainingShips)
        {
            var size = Fleet.SizeOf(kind);
            foreach (var placement in EnumeratePlacements(view, size))
            {
                var coveredHits = placement.Count(cell => view.Hits.Contains(cell));
                var weight = 1 + (coveredHits * HitPlacementWeight);
                foreach (var cell in placement)
                {
                    if (view.IsUntried(cell))
                    {
                        counts[cell] = counts.GetValueOrDefault(cell) + weight;
                    }
                }
            }
        }

        var bestScore = int.MinValue;
        var highest = new List<Coordinate>();
        foreach (var pair in counts)
        {
            var score = pair.Value + (IsAdjacentToHit(view, pair.Key) ? AdjacentHitBonus : 0);

            if (score > bestScore)
            {
                bestScore = score;
                highest.Clear();
                highest.Add(pair.Key);
            }
            else if (score == bestScore)
            {
                highest.Add(pair.Key);
            }
        }

        if (highest.Count == 0)
        {
            return AiRandom.PickUntried(view, _random);
        }

        return AiRandom.Pick(highest, _random);
    }

    private static bool IsAdjacentToHit(AiView view, Coordinate cell)
    {
        foreach (var hit in view.Hits)
        {
            var rowDistance = Math.Abs(hit.Row - cell.Row);
            var colDistance = Math.Abs(hit.Col - cell.Col);
            if (rowDistance + colDistance == 1)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<List<Coordinate>> EnumeratePlacements(AiView view, int size)
    {
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var horizontal = BuildPlacement(view, new Coordinate(row, col), size, horizontal: true);
                if (horizontal is not null)
                {
                    yield return horizontal;
                }

                var vertical = BuildPlacement(view, new Coordinate(row, col), size, horizontal: false);
                if (vertical is not null)
                {
                    yield return vertical;
                }
            }
        }
    }

    private static List<Coordinate>? BuildPlacement(AiView view, Coordinate start, int size, bool horizontal)
    {
        var cells = new List<Coordinate>(size);
        for (var i = 0; i < size; i++)
        {
            var row = start.Row + (horizontal ? 0 : i);
            var col = start.Col + (horizontal ? i : 0);
            if (!BoardCoords.InBounds(row, col))
            {
                return null;
            }

            var cell = new Coordinate(row, col);
            if (view.Misses.Contains(cell) || view.SunkCells.Contains(cell))
            {
                return null;
            }

            cells.Add(cell);
        }

        return cells;
    }
}
