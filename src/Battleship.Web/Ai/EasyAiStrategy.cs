using Battleship.Web.Game;

namespace Battleship.Web.Ai;

public sealed class EasyAiStrategy : IAiStrategy
{
    private readonly Random _random;

    public EasyAiStrategy(Random random)
    {
        _random = random;
    }

    public Coordinate ChooseShot(AiView view) => AiRandom.PickUntried(view, _random);
}
