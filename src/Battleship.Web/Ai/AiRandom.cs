using Battleship.Web.Game;

namespace Battleship.Web.Ai;

internal static class AiRandom
{
    public static Coordinate PickUntried(AiView view, Random random)
    {
        var untried = view.Untried();
        if (untried.Count == 0)
        {
            throw new InvalidOperationException("No untried cells remain.");
        }

        return untried[random.Next(untried.Count)];
    }

    public static Coordinate Pick(List<Coordinate> candidates, Random random) =>
        candidates[random.Next(candidates.Count)];
}
