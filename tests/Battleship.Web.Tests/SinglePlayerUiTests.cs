using System.Diagnostics;
using Battleship.Shared;
using Battleship.Web.Ai;
using Battleship.Web.Components;
using Battleship.Web.Game;
using Battleship.Web.Pages;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class SinglePlayerUiTests
{
    [Fact]
    public async Task DifficultySelection_Applied()
    {
        using var environment = new TestEnvironment();
        var select = new DifficultySelect();
        using var harness = await ComponentHarness.MountAsync(select, environment.Services);
        TestComponent.Set(select, "Selected", "hard");
        TestComponent.Call(select, "Start");
        Assert.Equal("https://example.test/single/hard", environment.Navigation.Records[^1].Uri);

        await using var context = await SinglePlayerContext.CreateAsync("hard", runRouteHook: true);
        Assert.Equal("hard", TestComponent.Get(context.Page, "_difficulty"));
        Assert.IsType<HardAiStrategy>(TestComponent.Get(context.Page, "_strategy"));
        Assert.Contains("data-difficulty=\"hard\"", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifficultyBadge_VisibleDuringAndAfterGame()
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
    public async Task FireAtCell_ShowsHitOrMiss()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        TestComponent.Set(context.Page, "_aiBoard", TestBoards.Fleet());
        TestComponent.Set(context.Page, "_target", new TargetBoard());
        TestComponent.Set(context.Page, "_strategy", new EmptyRowStrategy());

        await context.FireAsync(new Coordinate(0, 0));
        await context.FireAsync(new Coordinate(9, 9));

        var html = context.Html;
        var enemyHtml = html[html.IndexOf("Enemy targeting board", StringComparison.Ordinal)..];
        var hit = UiAssert.ElementMarkup(enemyHtml, "data-coordinate=\"A1\"");
        var miss = UiAssert.ElementMarkup(enemyHtml, "data-coordinate=\"J10\"");
        Assert.Contains("hit", hit, StringComparison.Ordinal);
        Assert.Contains("miss", miss, StringComparison.Ordinal);
        Assert.Contains("Shots <strong>2</strong>", html, StringComparison.Ordinal);
        Assert.Contains("Hits <strong>1</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyboardTargeting_ArrowsAndEnterFire()
    {
        using var environment = new TestEnvironment();
        var target = new TargetBoard();
        var cells = BoardView.Target(target);
        var activated = new List<Coordinate>();
        var grid = new BoardGrid { Cells = cells, Interactive = true, Label = "Keyboard targeting" };
        grid.OnActivate = EventCallback.Factory.Create<Coordinate>(grid, coordinate => activated.Add(coordinate));
        using var harness = await ComponentHarness.MountAsync(grid, environment.Services);

        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowDown" });
        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowRight" });
        await TestComponent.CallAsync(grid, "ActivateAsync", cells[0]);

        Assert.Equal("battleship.focusById", environment.Js.Calls[^1].Identifier);
        Assert.EndsWith("-1-1", (string)environment.Js.Calls[^1].Arguments[0]!, StringComparison.Ordinal);
        Assert.Equal(new Coordinate(0, 0), Assert.Single(activated));

        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        var enemyHtml = context.Html[context.Html.IndexOf("Enemy targeting board", StringComparison.Ordinal)..];
        Assert.False(UiAssert.ElementHasAttribute(enemyHtml, "data-coordinate=\"A1\"", "disabled"));
    }

    [Fact]
    public async Task TouchTargeting_TapFires()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();

        await context.FireAsync(new Coordinate(4, 4));

        Assert.Single(context.Target.Shots);
        Assert.Equal(0, context.Environment.Handler.CallCount);
        Assert.True((bool)TestComponent.Get(context.Page, "_humanTurn")!);
    }

    [Fact]
    public async Task FireAtTriedCell_Ignored()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        await context.FireAsync(new Coordinate(4, 4));
        var shots = context.Target.Shots.Count;

        await context.FireAsync(new Coordinate(4, 4));

        Assert.Equal(shots, context.Target.Shots.Count);
        Assert.Contains("already been targeted", (string)TestComponent.Get(context.Page, "_lastOutcome")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FireDuringAiTurn_Blocked()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        TestComponent.Set(context.Page, "_humanTurn", false);

        await context.FireAsync(new Coordinate(4, 4));

        Assert.Empty(context.Target.Shots);
    }

    [Fact]
    public async Task AiMovesAfterConfiguredDelay()
    {
        await using var context = await SinglePlayerContext.CreateAsync(
            settings: new Dictionary<string, string?> { ["Battleship:AiMoveDelayMs"] = "80" });
        context.StartBattle();
        var watch = Stopwatch.StartNew();

        await context.FireAsync(new Coordinate(4, 4));
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds >= 60, "the AI moved without the configured delay");
        Assert.Single(context.Knowledge.Hits.Concat(context.Knowledge.Misses));
    }

    [Fact]
    public async Task SunkShip_ShownOnBoardAndFleetStatus()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        TestComponent.Set(context.Page, "_aiBoard", TestBoards.Fleet());
        TestComponent.Set(context.Page, "_target", new TargetBoard());
        TestComponent.Set(context.Page, "_strategy", new EmptyRowStrategy());
        var carrier = TestBoards.Fleet().Ships.Single(ship => ship.Kind == ShipKind.CARRIER);
        foreach (var cell in carrier.Cells)
        {
            await context.FireAsync(cell);
        }

        var html = context.Html;
        var enemyBoard = html[html.IndexOf("Enemy targeting board", StringComparison.Ordinal)..];
        Assert.Contains("sunk", UiAssert.ElementMarkup(enemyBoard, "data-coordinate=\"A1\""), StringComparison.Ordinal);
        var enemyFleet = html[html.IndexOf("Enemy fleet", StringComparison.Ordinal)..];
        Assert.Contains("Carrier", enemyFleet, StringComparison.Ordinal);
        Assert.Contains("Sunk", enemyFleet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WinAndLoseScreens_Shown()
    {
        await using var win = await SinglePlayerContext.CreateAsync("hard");
        win.StartBattle();
        TestComponent.Set(win.Page, "_aiBoard", TestBoards.Fleet());
        TestComponent.Set(win.Page, "_target", new TargetBoard());
        TestComponent.Set(win.Page, "_strategy", new EmptyRowStrategy());
        foreach (var cell in TestBoards.Fleet().Ships.SelectMany(ship => ship.Cells))
        {
            await win.FireAsync(cell);
        }

        Assert.Equal(Side.Self, (Side?)TestComponent.Get(win.Page, "_winner"));
        Assert.Contains("Victory", win.Html, StringComparison.Ordinal);
        Assert.Contains("Rematch", win.Html, StringComparison.Ordinal);
        Assert.Contains("Hard", win.Html, StringComparison.Ordinal);

        await using var lose = await SinglePlayerContext.CreateAsync();
        lose.StartBattle();
        TestComponent.SetEnum(lose.Page, "_phase", "Over");
        TestComponent.Set(lose.Page, "_winner", Side.Peer);
        lose.Harness.Refresh();
        Assert.Contains("Defeat", lose.Html, StringComparison.Ordinal);
        Assert.Contains("Game over", lose.Html, StringComparison.Ordinal);
        Assert.Contains("Rematch", lose.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rematch_ResetsStateKeepsDifficulty()
    {
        await using var context = await SinglePlayerContext.CreateAsync("easy");
        var key = (int)TestComponent.Get(context.Page, "_gameKey")!;
        var board = context.OwnBoard;

        TestComponent.Call(context.Page, "Rematch");
        context.Harness.Refresh();

        Assert.Equal("Setup", context.Phase.ToString());
        Assert.Equal(key + 1, (int)TestComponent.Get(context.Page, "_gameKey")!);
        Assert.NotSame(board, context.OwnBoard);
        Assert.Equal("easy", TestComponent.Get(context.Page, "_difficulty"));
    }

    [Fact]
    public async Task ChangeDifficulty_ReturnsToSelect()
    {
        await using var context = await SinglePlayerContext.CreateAsync("hard");

        TestComponent.Call(context.Page, "ChangeDifficulty");

        Assert.Equal("https://example.test/single", context.Environment.Navigation.Records[^1].Uri);
    }

    [Fact]
    public async Task OfflineAfterLoad_GamePlayable()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();

        await context.FireAsync(new Coordinate(4, 4));

        Assert.Single(context.Target.Shots);
        Assert.Single(context.Knowledge.Hits.Concat(context.Knowledge.Misses));
        Assert.Equal(0, context.Environment.Handler.CallCount);
        Assert.Empty(context.Environment.Js.Calls);
        Assert.Empty(context.Environment.Transport.Sent);
    }

    [Fact]
    public async Task EnemyShipPositions_NotPresentInDom()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        var html = context.Html;
        var enemy = html[html.IndexOf("Enemy targeting board", StringComparison.Ordinal)..];

        Assert.DoesNotContain("cell ship", enemy, StringComparison.Ordinal);
        Assert.All(context.AiBoard.Ships.SelectMany(ship => ship.Cells), cell =>
            Assert.DoesNotContain($"data-coordinate=\"{cell.Label}\" class=\"cell ship", enemy, StringComparison.Ordinal));
    }
}
