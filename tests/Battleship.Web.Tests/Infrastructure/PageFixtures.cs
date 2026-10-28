using System.Net;
using System.Net.Http;
using Battleship.Shared;
using Battleship.Web.Ai;
using Battleship.Web.Components;
using Battleship.Web.Game;
using Battleship.Web.Net;
using Battleship.Web.Session;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class SinglePlayerContext : IAsyncDisposable
{
    private SinglePlayerContext(TestEnvironment environment, SinglePlayerProbe page, ComponentHarness harness)
    {
        Environment = environment;
        Page = page;
        Harness = harness;
    }

    public TestEnvironment Environment { get; }

    public SinglePlayerProbe Page { get; }

    public ComponentHarness Harness { get; }

    public string Html => Harness.Html;

    public static async Task<SinglePlayerContext> CreateAsync(
        string difficulty = "medium",
        bool runRouteHook = false,
        Dictionary<string, string?>? settings = null,
        Func<Random>? randomFactory = null)
    {
        var values = Defaults(settings);
        var environment = new TestEnvironment(values);
        var page = new SinglePlayerProbe
        {
            Difficulty = difficulty,
            RunParametersHook = runRouteHook,
            RandomFactory = randomFactory
        };
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        if (!runRouteHook)
        {
            TestComponent.Set(page, "_difficulty", difficulty.Trim().ToLowerInvariant());
            TestComponent.Set(page, "_options", GameOptions.FromConfiguration(environment.Configuration));
            TestComponent.Set(page, "_initialized", true);
            TestComponent.Call(page, "StartNewGame");
            harness.Refresh();
        }

        return new SinglePlayerContext(environment, page, harness);
    }

    public Board OwnBoard => (Board)TestComponent.Get(Page, "_humanBoard")!;

    public Board AiBoard => (Board)TestComponent.Get(Page, "_aiBoard")!;

    public TargetBoard Target => (TargetBoard)TestComponent.Get(Page, "_target")!;

    public AiView Knowledge => (AiView)TestComponent.Get(Page, "_aiKnowledge")!;

    public object Phase => TestComponent.Get(Page, "_phase")!;

    public void PlaceFleet()
    {
        foreach (var (kind, start, horizontal) in TestBoards.Layout)
        {
            Assert.True(OwnBoard.TryPlace(kind, start, horizontal));
        }
    }

    public void StartBattle()
    {
        PlaceFleet();
        TestComponent.Call(Page, "StartBattle");
    }

    public Task FireAsync(Coordinate coordinate) => TestComponent.CallAsync(Page, "FireAsync", coordinate);

    public ValueTask DisposeAsync()
    {
        Harness.Dispose();
        Environment.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string?> Defaults(Dictionary<string, string?>? settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Battleship:AiMoveDelayMs"] = "0",
            ["Battleship:AiDecisionBudgetMs"] = "250",
            ["Battleship:AiSeed"] = "1234",
            ["Battleship:ReadTimeoutSeconds"] = "1",
            ["Battleship:HandshakeTimeoutSeconds"] = "1",
            ["Battleship:JoinTimeoutSeconds"] = "1",
            ["Battleship:JoinRetryCount"] = "2",
            ["Battleship:JoinRetryDelayMs"] = "0"
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}

internal sealed class MultiplayerContext : IAsyncDisposable
{
    private MultiplayerContext(TestEnvironment environment, MultiplayerGameProbe page, ComponentHarness harness)
    {
        Environment = environment;
        Page = page;
        Harness = harness;
    }

    public TestEnvironment Environment { get; }

    public MultiplayerGameProbe Page { get; }

    public ComponentHarness Harness { get; }

    public FakeWsTransport Transport => Environment.Transport;

    public string Html => Harness.Html;

    public MultiplayerSession Session => (MultiplayerSession)TestComponent.Get(Page, "_session")!;

    public object Phase => TestComponent.Get(Page, "_phase")!;

    public string? ErrorCode => (string?)TestComponent.Get(Page, "_errorCode");

    public string? ErrorDetail => (string?)TestComponent.Get(Page, "_errorDetail");

    public string? Notice => (string?)TestComponent.Get(Page, "_notice");

    public static async Task<MultiplayerContext> CreateAsync(
        bool isHost,
        bool runInitialize,
        string roomCode = "ABC234",
        Dictionary<string, string?>? settings = null,
        StubHttpMessageHandler? handler = null,
        FakeWsTransport? transport = null)
    {
        var values = Defaults(settings);
        var resolvedHandler = handler ?? StubHttpMessageHandler.Json(
            isHost ? HttpStatusCode.Created : HttpStatusCode.OK,
            isHost ? "{\"roomCode\":\"ABC234\"}" : "{\"participants\":1}");
        var resolvedTransport = transport ?? new FakeWsTransport();
        var environment = new TestEnvironment(values, resolvedHandler, resolvedTransport);
        var page = new MultiplayerGameProbe
        {
            IsHost = isHost,
            RoomCode = isHost ? null : roomCode,
            RunInitialize = runInitialize
        };
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        return new MultiplayerContext(environment, page, harness);
    }

    public static async Task<MultiplayerContext> PrepareCombatAsync(
        bool isHost,
        string roomCode = "ABC234",
        Dictionary<string, string?>? settings = null)
    {
        var context = await CreateAsync(isHost, runInitialize: false, roomCode, settings);
        var session = new MultiplayerSession
        {
            Role = isHost ? "host" : "client",
            RoomCode = roomCode,
            HandshakeComplete = true,
            ReadySent = true,
            PeerReady = true,
            Turn = isHost ? Side.Self : Side.Peer
        };
        foreach (var (kind, start, horizontal) in TestBoards.Layout)
        {
            Assert.True(session.OwnBoard.TryPlace(kind, start, horizontal));
        }

        TestComponent.Set(context.Page, "_session", session);
        TestComponent.Set(context.Page, "_options", GameOptions.FromConfiguration(context.Environment.Configuration));
        TestComponent.Set(context.Page, "_roomCode", roomCode);
        TestComponent.SetEnum(context.Page, "_phase", "Combat");
        return context;
    }

    public Task DispatchAsync(ProtocolMessage message) =>
        TestComponent.CallAsync(Page, "DispatchAsync", message);

    public Task ReceiveLoopAsync() => TestComponent.CallAsync(Page, "ReceiveLoopAsync");

    public ValueTask DisposeAsync()
    {
        Harness.Dispose();
        Environment.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string?> Defaults(Dictionary<string, string?>? settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Battleship:AiMoveDelayMs"] = "0",
            ["Battleship:HttpTimeoutSeconds"] = "2",
            ["Battleship:JoinTimeoutSeconds"] = "1",
            ["Battleship:HandshakeTimeoutSeconds"] = "1",
            ["Battleship:ReadTimeoutSeconds"] = "1",
            ["Battleship:JoinRetryCount"] = "2",
            ["Battleship:JoinRetryDelayMs"] = "0"
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}
