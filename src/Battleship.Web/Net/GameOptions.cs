using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Battleship.Web.Net;

public sealed class GameOptions
{
    public int? AiSeed { get; init; }
    public int AiMoveDelayMs { get; init; } = 600;
    public int AiDecisionBudgetMs { get; init; } = 250;
    public int HttpTimeoutSeconds { get; init; } = 10;
    public int JoinTimeoutSeconds { get; init; } = 600;
    public int HandshakeTimeoutSeconds { get; init; } = 15;
    public int ReadTimeoutSeconds { get; init; } = 60;
    public int JoinRetryCount { get; init; } = 20;
    public int JoinRetryDelayMs { get; init; } = 500;

    public static GameOptions FromConfiguration(IConfiguration configuration)
    {
        return new GameOptions
        {
            AiSeed = NullableInt(configuration, "Battleship:AiSeed"),
            AiMoveDelayMs = Math.Max(0, Integer(configuration, "Battleship:AiMoveDelayMs", 600)),
            AiDecisionBudgetMs = Math.Max(1, Integer(configuration, "Battleship:AiDecisionBudgetMs", 250)),
            HttpTimeoutSeconds = Math.Max(1, Integer(configuration, "Battleship:HttpTimeoutSeconds", 10)),
            JoinTimeoutSeconds = Math.Max(1, Integer(configuration, "Battleship:JoinTimeoutSeconds", 600)),
            HandshakeTimeoutSeconds = Math.Max(1, Integer(configuration, "Battleship:HandshakeTimeoutSeconds", 15)),
            ReadTimeoutSeconds = Math.Max(1, Integer(configuration, "Battleship:ReadTimeoutSeconds", 60)),
            JoinRetryCount = Math.Max(0, Integer(configuration, "Battleship:JoinRetryCount", 20)),
            JoinRetryDelayMs = Math.Max(0, Integer(configuration, "Battleship:JoinRetryDelayMs", 500))
        };
    }

    private static int Integer(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int? NullableInt(IConfiguration configuration, string key) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
