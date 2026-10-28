using Battleship.Shared;
using Battleship.Web.Ai;
using Battleship.Web.Game;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class SinglePlayerPageTests
{
    [Fact]
    public async Task Start_TurnHumanAndDifficultyFromRoute()
    {
        await using var context = await SinglePlayerContext.CreateAsync("hard", runRouteHook: true);
        Assert.Equal("hard", TestComponent.Get(context.Page, "_difficulty"));
        Assert.True((bool)TestComponent.Get(context.Page, "_humanTurn")!);
        Assert.Equal("Setup", context.Phase.ToString());
        Assert.Equal(5, context.AiBoard.Ships.Count);
        Assert.Contains("data-difficulty=\"hard\"", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiStrategy_ChosenOnceAndNeverReplaced()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        var strategy = TestComponent.Get(context.Page, "_strategy");
        context.StartBattle();
        await context.FireAsync(new Coordinate(9, 9));
        Assert.Same(strategy, TestComponent.Get(context.Page, "_strategy"));
    }

    [Fact]
    public async Task AiStrategy_ReceivesAiViewNeverOwnBoard()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        var strategy = new RecordingStrategy();
        TestComponent.Set(context.Page, "_strategy", strategy);
        await context.FireAsync(new Coordinate(9, 9));
        var view = Assert.Single(strategy.Views);
        Assert.IsType<AiView>(view);
        Assert.IsNotType<Board>(view);
    }

    [Fact]
    public async Task AiFleet_AlwaysAutoPlacedAtEveryDifficulty()
    {
        foreach (var difficulty in new[] { "easy", "medium", "hard" })
        {
            await using var context = await SinglePlayerContext.CreateAsync(difficulty);
            var board = context.AiBoard;
            Assert.Equal(5, board.Ships.Count);
            Assert.All(board.Ships.SelectMany(ship => ship.Cells), cell => Assert.True(cell.IsValid));
            Assert.Equal(17, board.Ships.SelectMany(ship => ship.Cells).Distinct().Count());
        }
    }

    [Fact]
    public async Task DifficultyBadge_ShownDuringGameAndOnGameOver()
    {
        await using var context = await SinglePlayerContext.CreateAsync("easy");
        context.StartBattle();
        Assert.Contains("data-difficulty=\"easy\"", context.Html, StringComparison.Ordinal);
        Assert.Contains("Easy", context.Html, StringComparison.Ordinal);
        TestComponent.SetEnum(context.Page, "_phase", "Over");
        TestComponent.Set(context.Page, "_winner", Side.Peer);
        context.Harness.Refresh();
        Assert.Contains("Game over", context.Html, StringComparison.Ordinal);
        Assert.Contains("data-difficulty=\"easy\"", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HumanThenAi_StrictAlternation()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        bool humanTurnDuringAi = true;
        var strategy = new RecordingStrategy
        {
            OnChoose = _ => humanTurnDuringAi = (bool)TestComponent.Get(context.Page, "_humanTurn")!
        };
        TestComponent.Set(context.Page, "_strategy", strategy);
        await context.FireAsync(new Coordinate(9, 9));
        Assert.False(humanTurnDuringAi);
        Assert.True((bool)TestComponent.Get(context.Page, "_humanTurn")!);
    }

    [Fact]
    public async Task InvalidOrRepeatedShot_DoesNotAdvanceTurn()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        await context.FireAsync(new Coordinate(9, 9));
        var shots = context.Target.Shots.Count;
        var aiShots = context.Knowledge.Hits.Count + context.Knowledge.Misses.Count;
        await context.FireAsync(new Coordinate(9, 9));
        await context.FireAsync(new Coordinate(-1, 0));
        Assert.Equal(shots, context.Target.Shots.Count);
        Assert.Equal(aiShots, context.Knowledge.Hits.Count + context.Knowledge.Misses.Count);
        Assert.True((bool)TestComponent.Get(context.Page, "_humanTurn")!);
    }

    [Fact]
    public async Task ShotDuringAiTurn_Rejected()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        TestComponent.Set(context.Page, "_humanTurn", false);
        await context.FireAsync(new Coordinate(9, 9));
        Assert.Empty(context.Target.Shots);
    }

    [Fact]
    public async Task AiShot_ShownAfterMoveDelay()
    {
        await using var context = await SinglePlayerContext.CreateAsync(
            settings: new Dictionary<string, string?> { ["Battleship:AiMoveDelayMs"] = "10" });
        context.StartBattle();
        await context.FireAsync(new Coordinate(9, 9));
        Assert.Single(context.Knowledge.Hits.Concat(context.Knowledge.Misses));
        Assert.StartsWith("AI ", (string)TestComponent.Get(context.Page, "_lastOutcome")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiDecisionBudgetExceeded_ShowsInternalError()
    {
        await using var context = await SinglePlayerContext.CreateAsync(
            settings: new Dictionary<string, string?> { ["Battleship:AiDecisionBudgetMs"] = "1" });
        context.StartBattle();
        TestComponent.Set(context.Page, "_strategy", new SlowStrategy(TimeSpan.FromMilliseconds(25)));
        await context.FireAsync(new Coordinate(9, 9));
        Assert.Equal("Combat", context.Phase.ToString());
        Assert.Single(context.Knowledge.Hits.Concat(context.Knowledge.Misses));
        Assert.Null(TestComponent.Get(context.Page, "_error"));
    }

    [Fact]
    public async Task AiAutoPlacementFailure_ShowsInternalError()
    {
        await using var context = await SinglePlayerContext.CreateAsync(randomFactory: () => new ZeroRandom());
        Assert.Equal("Error", context.Phase.ToString());
        Assert.Contains("could not be placed", (string)TestComponent.Get(context.Page, "_error")!, StringComparison.Ordinal);
        Assert.Contains("Something went wrong", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachOutcome_HitMissSunkGameOver_Reported()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        var target = new TargetBoard();
        var outcomes = new Dictionary<ShotOutcome, string>();
        foreach (var shot in new[]
                 {
                     new Coordinate(0, 5), new Coordinate(1, 0), new Coordinate(1, 1), new Coordinate(1, 2), new Coordinate(1, 3),
                     new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(0, 2), new Coordinate(0, 3), new Coordinate(0, 4),
                     new Coordinate(2, 0), new Coordinate(2, 1), new Coordinate(2, 2),
                     new Coordinate(3, 0), new Coordinate(3, 1), new Coordinate(3, 2),
                     new Coordinate(4, 0), new Coordinate(4, 1)
                 })
        {
            var result = resolver.Resolve(board, shot);
            target.Record(shot, result.Outcome);
            outcomes.TryAdd(result.Outcome, (string)TestComponent.Call(context.Page, "Describe", result, "You")!);
        }

        Assert.Equal(18, target.Shots.Count);
        Assert.Contains("missed", outcomes[ShotOutcome.MISS], StringComparison.Ordinal);
        Assert.Contains("hit a ship", outcomes[ShotOutcome.HIT], StringComparison.Ordinal);
        Assert.Contains("sank the", outcomes[ShotOutcome.SUNK], StringComparison.Ordinal);
        Assert.Contains("game over", outcomes[ShotOutcome.GAME_OVER], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameOver_NamesWinnerAndDifficulty()
    {
        await using var context = await SinglePlayerContext.CreateAsync("hard");
        context.StartBattle();
        TestComponent.Set(context.Page, "_aiBoard", TestBoards.Fleet());
        TestComponent.Set(context.Page, "_target", new TargetBoard());
        TestComponent.Set(context.Page, "_strategy", new EmptyRowStrategy());
        foreach (var ship in TestBoards.Fleet().Ships)
        {
            foreach (var cell in ship.Cells)
            {
                await context.FireAsync(cell);
            }
        }

        Assert.Equal(Side.Self, (Side?)TestComponent.Get(context.Page, "_winner"));
        Assert.Contains("Victory", context.Html, StringComparison.Ordinal);
        Assert.Contains("Hard", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rematch_NewSessionSameDifficulty()
    {
        await using var context = await SinglePlayerContext.CreateAsync("easy");
        var key = (int)TestComponent.Get(context.Page, "_gameKey")!;
        var board = context.OwnBoard;
        TestComponent.Call(context.Page, "Rematch");
        Assert.Equal("Setup", context.Phase.ToString());
        Assert.Equal(key + 1, (int)TestComponent.Get(context.Page, "_gameKey")!);
        Assert.NotSame(board, context.OwnBoard);
        Assert.Equal("easy", TestComponent.Get(context.Page, "_difficulty"));
    }

    [Fact]
    public async Task ChangeDifficulty_NavigatesToSelect()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        TestComponent.Call(context.Page, "ChangeDifficulty");
        Assert.Equal("https://example.test/single", context.Environment.Navigation.Records[^1].Uri);
    }

    [Fact]
    public async Task Render_NeverContainsAiShipCells()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        var html = context.Html;
        var enemyIndex = html.IndexOf("Enemy targeting board", StringComparison.Ordinal);
        Assert.True(enemyIndex >= 0);
        var enemyMarkup = html[enemyIndex..];
        Assert.DoesNotContain("cell ship", enemyMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoNetworkIoOrBrowserStorageDuringGame()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        await context.FireAsync(new Coordinate(9, 9));
        Assert.Equal(0, context.Environment.Handler.CallCount);
        Assert.Empty(context.Environment.Js.Calls);
        Assert.Empty(context.Environment.Transport.Sent);
    }

    [Fact]
    public async Task Dispose_ReleasesSession()
    {
        var context = await SinglePlayerContext.CreateAsync();
        await context.Page.DisposeAsync();
        Assert.True((bool)TestComponent.Get(context.Page, "_disposed")!);
        var lifetime = (CancellationTokenSource)TestComponent.Get(context.Page, "_lifetime")!;
        Assert.True(lifetime.IsCancellationRequested);
        await context.Page.DisposeAsync();
        context.Harness.Dispose();
        context.Environment.Dispose();
    }
}
