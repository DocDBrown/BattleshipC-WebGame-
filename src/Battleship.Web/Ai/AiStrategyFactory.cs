namespace Battleship.Web.Ai;

public static class AiStrategyFactory
{
    public static IAiStrategy Create(string difficulty, Random random) => difficulty switch
    {
        "easy" => new EasyAiStrategy(random),
        "hard" => new HardAiStrategy(random),
        _ => new MediumAiStrategy(random)
    };
}
