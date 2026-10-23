using Battleship.Web.Game;

namespace Battleship.Web.Ai;

public sealed class MediumAiStrategy : IAiStrategy
{
    private readonly Random _random;

    public MediumAiStrategy(Random random)
    {
        _random = random;
    }

    public Coordinate ChooseShot(AiView view)
    {
        if (view.Hits.Count == 0)
        {
            return AiRandom.PickUntried(view, _random);
        }

        var lineExtensions = new HashSet<Coordinate>();
        foreach (var rowGroup in view.Hits.GroupBy(h => h.Row).Where(g => g.Count() >= 2))
        {
            var min = rowGroup.Min(c => c.Col);
            var max = rowGroup.Max(c => c.Col);
            AddIfUntried(view, lineExtensions, new Coordinate(rowGroup.Key, min - 1));
            AddIfUntried(view, lineExtensions, new Coordinate(rowGroup.Key, max + 1));
        }

        foreach (var colGroup in view.Hits.GroupBy(h => h.Col).Where(g => g.Count() >= 2))
        {
            var min = colGroup.Min(c => c.Row);
            var max = colGroup.Max(c => c.Row);
            AddIfUntried(view, lineExtensions, new Coordinate(min - 1, colGroup.Key));
            AddIfUntried(view, lineExtensions, new Coordinate(max + 1, colGroup.Key));
        }

        if (lineExtensions.Count > 0)
        {
            return AiRandom.Pick(lineExtensions.ToList(), _random);
        }

        var adjacent = new HashSet<Coordinate>();
        foreach (var hit in view.Hits)
        {
            AddIfUntried(view, adjacent, new Coordinate(hit.Row - 1, hit.Col));
            AddIfUntried(view, adjacent, new Coordinate(hit.Row + 1, hit.Col));
            AddIfUntried(view, adjacent, new Coordinate(hit.Row, hit.Col - 1));
            AddIfUntried(view, adjacent, new Coordinate(hit.Row, hit.Col + 1));
        }

        return adjacent.Count > 0
            ? AiRandom.Pick(adjacent.ToList(), _random)
            : AiRandom.PickUntried(view, _random);
    }

    private static void AddIfUntried(AiView view, HashSet<Coordinate> candidates, Coordinate coordinate)
    {
        if (view.IsUntried(coordinate))
        {
            candidates.Add(coordinate);
        }
    }
}
