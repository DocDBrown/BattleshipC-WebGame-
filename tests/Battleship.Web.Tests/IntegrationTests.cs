using System.Net;
using Battleship.Shared;
using Battleship.Web.Ai;
using Battleship.Web.Game;
using Battleship.Web.Net;
using Battleship.Web.Pages;
using Battleship.Web.Security;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class GameplayIntegrationTests
{
    [Fact]
    public void ResolverBoardAndView_FullGameReachesGameOver()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        ShotResult last = default;
        foreach (var cell in board.Ships.SelectMany(ship => ship.Cells))
        {
            last = resolver.Resolve(board, cell);
        }

        Assert.True(last.IsGameOver);
        Assert.True(board.AllSunk);
        Assert.All(
            BoardView.Own(board).Where(cell => board.ShipAt(cell.Coordinate) is not null),
            cell => Assert.Equal(CellState.Sunk, cell.State));
    }

    [Fact]
    public void ResultSequence_TargetBoardRendersSunkCorrectly()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        var target = new TargetBoard();
        foreach (var cell in board.Ships.Single(ship => ship.Kind == ShipKind.CRUISER).Cells)
        {
            var result = resolver.Resolve(board, cell);
            target.Record(cell, result.Outcome);
            if (result.Ship is ShipKind kind)
            {
                target.MarkSunkLine(cell, Fleet.SizeOf(kind));
            }
        }

        var view = BoardView.Target(target).ToDictionary(cell => cell.Coordinate, cell => cell.State);
        foreach (var cell in board.Ships.Single(ship => ship.Kind == ShipKind.CRUISER).Cells)
        {
            Assert.Equal(CellState.Sunk, view[cell]);
        }
    }

    [Fact]
    public void AiViewAndTargetBoard_AgreeOnSunkSegments()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        var view = new AiView();
        var target = new TargetBoard();
        foreach (var cell in board.Ships.SelectMany(ship => ship.Cells))
        {
            var result = resolver.Resolve(board, cell);
            view.RecordShot(cell, result.Outcome, result.Ship);
            target.Record(cell, result.Outcome);
            if (result.Ship is ShipKind kind)
            {
                target.MarkSunkLine(cell, Fleet.SizeOf(kind));
            }
        }

        Assert.Equal(view.SunkCells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Col), target.Shots.Keys.Where(target.IsSunk).OrderBy(cell => cell.Row).ThenBy(cell => cell.Col));
        Assert.Empty(view.RemainingShips);
    }
}

public sealed class AiIntegrationTests
{
    [Fact]
    public void EachStrategy_VsAutoPlacedBoard_FinishesWithin100Shots()
    {
        foreach (var difficulty in new[] { "easy", "medium", "hard" })
        {
            var board = new Board();
            Assert.True(board.TryAutoPlaceAll(new Random(101)));
            var strategy = AiStrategyFactory.Create(difficulty, new Random(7));
            var resolver = new ShotResolver();
            var view = new AiView();
            var shots = 0;
            while (!board.AllSunk)
            {
                var shot = strategy.ChooseShot(view);
                var result = resolver.Resolve(board, shot);
                view.RecordShot(shot, result.Outcome, result.Ship);
                shots++;
                Assert.True(shots <= 100, $"{difficulty} did not finish within 100 shots.");
            }
        }
    }

    [Fact]
    public void EachStrategy_ManySeeds_NeverRepeatsOrThrows()
    {
        foreach (var difficulty in new[] { "easy", "medium", "hard" })
        {
            for (var seed = 0; seed < 5; seed++)
            {
                var strategy = AiStrategyFactory.Create(difficulty, new Random(seed));
                var view = new AiView();
                for (var index = 0; index < 30; index++)
                {
                    var shot = strategy.ChooseShot(view);
                    Assert.True(view.IsUntried(shot));
                    view.RecordShot(shot, index % 5 == 0 ? ShotOutcome.HIT : ShotOutcome.MISS, null);
                }
            }
        }
    }
}

public sealed class NetIntegrationTests
{
    [Fact]
    public async Task WsTransportAndCodec_LoopbackRoundTrip()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var codec = new ProtocolCodec();
        var message = ProtocolMessage.Chat("hello over the wire");
        await transport.SendAsync(message, CancellationToken.None);
        var frame = await transport.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.True(codec.TryDeserialize(frame.Text!, out var parsed, out var error), error);
        Assert.Equal(MessageType.CHAT, parsed!.Type);
        Assert.Equal("hello over the wire", parsed.Text);
    }

    [Fact]
    public async Task RoomApiClient_AgainstProblemJsonStub_MapsAllErrors()
    {
        var cases = new (HttpStatusCode Status, string Code, bool Create)[]
        {
            (HttpStatusCode.BadRequest, "VALIDATION_FAILED", true),
            (HttpStatusCode.NotFound, "NOT_FOUND", false),
            (HttpStatusCode.Conflict, "CONFLICT", true),
            (HttpStatusCode.TooManyRequests, "RATE_LIMITED", true),
            (HttpStatusCode.InternalServerError, "UPSTREAM_UNAVAILABLE", true)
        };
        foreach (var testCase in cases)
        {
            var handler = StubHttpMessageHandler.Json(testCase.Status, "{\"detail\":\"problem\"}");
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            var client = new RoomApiClient(http);
            var code = testCase.Create
                ? (await client.CreateRoomAsync(CancellationToken.None)).ErrorCode
                : (await client.GetRoomAsync("ABC234", CancellationToken.None)).ErrorCode;
            Assert.Equal(testCase.Code, code);
        }
    }
}

public sealed class SecurityIntegrationTests
{
    [Fact]
    public void ChatSanitizerOutput_AcceptedByCodec()
    {
        var clean = ChatSanitizer.Sanitize("  <b>hello</b>\u202E\u0000 " + new string('z', 300));
        var codec = new ProtocolCodec();
        var json = codec.Serialize(ProtocolMessage.Chat(clean));
        Assert.True(codec.TryDeserialize(json, out var parsed, out var error), error);
        Assert.Equal(clean, parsed!.Text);
    }
}

public sealed class RoutingTests
{
    [Fact]
    public async Task UnknownRoute_NavigatesToRootReplacingHistory()
    {
        using var environment = new TestEnvironment();
        var page = new NotFoundRedirect();
        TestComponent.Inject(page, environment.Services);
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        var navigation = Assert.Single(environment.Navigation.Records);
        Assert.Equal("https://example.test/", navigation.Uri);
        Assert.True(navigation.Replace);
        harness.Dispose();
    }

    [Fact]
    public async Task SingleInvalidDifficulty_NavigatesToMediumReplacingHistory()
    {
        await using var context = await SinglePlayerContext.CreateAsync("expert", runRouteHook: true);
        var navigation = Assert.Single(context.Environment.Navigation.Records);
        Assert.Equal("https://example.test/single/medium", navigation.Uri);
        Assert.True(navigation.Replace);
    }

    [Fact]
    public async Task SingleMixedCaseDifficulty_NormalizedOrRedirected()
    {
        await using var context = await SinglePlayerContext.CreateAsync("EASY", runRouteHook: true);
        Assert.Equal("easy", TestComponent.Get(context.Page, "_difficulty"));
        Assert.Empty(context.Environment.Navigation.Records);
        Assert.Equal("Setup", context.Phase.ToString());
    }

    [Fact]
    public async Task JoinInvalidRoomCode_ShowsValidationFailedWithoutNetwork()
    {
        await using var context = await MultiplayerContext.CreateAsync(false, runInitialize: false, roomCode: "bad");
        await TestComponent.CallAsync(context.Page, "StartClientAsync");
        Assert.Equal("VALIDATION_FAILED", context.ErrorCode);
        Assert.Equal(0, context.Environment.Handler.CallCount);
        Assert.Empty(context.Transport.ConnectUris);
    }
}

public sealed class MenuPageTests
{
    [Fact]
    public async Task RoomCodeInput_UppercasedAndLimitedToSix()
    {
        using var environment = new TestEnvironment();
        var page = new Battleship.Web.Pages.Index();
        TestComponent.Inject(page, environment.Services);
        TestComponent.Set(page, "RoomCodeInput", "ab-cd234xyz");
        Assert.Equal("ABCD23", TestComponent.Get(page, "RoomCodeInput"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task JoinSubmit_NavigatesToJoinRoute()
    {
        using var environment = new TestEnvironment();
        var page = new Battleship.Web.Pages.Index();
        TestComponent.Inject(page, environment.Services);
        TestComponent.Set(page, "RoomCodeInput", "abc234");
        TestComponent.Call(page, "Join");
        Assert.Equal("https://example.test/join/ABC234", Assert.Single(environment.Navigation.Records).Uri);
        await Task.CompletedTask;
    }
}

public sealed class DifficultySelectPageTests
{
    [Fact]
    public async Task MediumPreselected()
    {
        using var environment = new TestEnvironment();
        var page = new DifficultySelect();
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        Assert.Equal("medium", TestComponent.Get(page, "Selected"));
        Assert.Contains("value=\"medium\"", harness.Html, StringComparison.Ordinal);
        Assert.Contains("checked", harness.Html, StringComparison.Ordinal);
        harness.Dispose();
    }

    [Fact]
    public async Task EnterWithoutChoice_StartsMedium()
    {
        using var environment = new TestEnvironment();
        var page = new DifficultySelect();
        TestComponent.Inject(page, environment.Services);
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        TestComponent.Call(page, "Start");
        Assert.Equal("https://example.test/single/medium", Assert.Single(environment.Navigation.Records).Uri);
        harness.Dispose();
    }

    [Fact]
    public async Task EachOption_KeyboardReachableWithFocusState()
    {
        using var environment = new TestEnvironment();
        var page = new DifficultySelect();
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        foreach (var option in new[] { "easy", "medium", "hard" })
        {
            Assert.Contains($"value=\"{option}\"", harness.Html, StringComparison.Ordinal);
        }

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(harness.Html, "type=\"radio\"").Count);
        Assert.DoesNotContain("tabindex=\"-1\"", harness.Html, StringComparison.Ordinal);
        harness.Dispose();
    }
}
