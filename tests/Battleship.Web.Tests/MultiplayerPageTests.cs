using System.Net;
using Battleship.Shared;
using Battleship.Web.Net;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class MultiplayerHostPageTests
{
    [Fact]
    public async Task Start_TurnSelfSeqZero()
    {
        await using var context = await StartHostPlacedAsync();
        await TestComponent.CallAsync(context.Page, "ReadyAsync");
        await context.DispatchAsync(ProtocolMessage.Ready());
        Assert.Equal(Side.Self, context.Session.Turn);
        Assert.Equal(1, context.Session.NextSeq);
        Assert.Null(context.Session.PendingSeq);
    }

    [Fact]
    public async Task SocketOpenedWithRoleHostBeforeRoomCodeShown()
    {
        await using var context = await StartHostPlacedAsync();
        var uri = Assert.Single(context.Transport.ConnectUris);
        Assert.Equal("/ws/rooms/ABC234", uri.AbsolutePath);
        Assert.Contains("role=host", uri.Query, StringComparison.Ordinal);
        Assert.Equal("ABC234", TestComponent.Get(context.Page, "_roomCode"));
    }

    [Fact]
    public async Task JoinLink_IsOriginJoinRoomCode()
    {
        await using var context = await StartHostPlacedAsync();
        Assert.Equal("https://example.test/join/ABC234", TestComponent.Get(context.Page, "_joinLink"));
    }

    [Fact]
    public async Task CreateRoom429_ShowsRateLimited()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{\"detail\":\"slow down\"}");
        var transport = new FakeWsTransport { AutoHello = true };
        await using var context = await MultiplayerContext.CreateAsync(true, true, handler: handler, transport: transport);
        Assert.Equal("RATE_LIMITED", context.ErrorCode);
        Assert.Equal("Error", context.Phase.ToString());
    }

    [Fact]
    public async Task CreateRoom409Or5xxOrTimeout_ShowsUpstreamUnavailable()
    {
        foreach (var status in new[] { HttpStatusCode.Conflict, HttpStatusCode.InternalServerError })
        {
            var handler = StubHttpMessageHandler.Json(status, "{\"detail\":\"problem\"}");
            await using var context = await MultiplayerContext.CreateAsync(
                true, true, handler: handler, transport: new FakeWsTransport { AutoHello = true });
            Assert.Equal("UPSTREAM_UNAVAILABLE", context.ErrorCode);
        }

        var slow = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return StubHttpMessageHandler.Response(HttpStatusCode.Created, "{\"roomCode\":\"ABC234\"}");
        });
        await using var timeoutContext = await MultiplayerContext.CreateAsync(
            true,
            true,
            settings: new Dictionary<string, string?> { ["Battleship:HttpTimeoutSeconds"] = "1" },
            handler: slow,
            transport: new FakeWsTransport { AutoHello = true });
        Assert.Equal("UPSTREAM_UNAVAILABLE", timeoutContext.ErrorCode);
    }

    [Fact]
    public async Task RepliesHelloOnlyAfterClientHello()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.SetEnum(context.Page, "_phase", "WaitingForPeer");
        Assert.Empty(context.Transport.Sent);
        await TestComponent.CallAsync(context.Page, "OnHelloAsync");
        var hello = Assert.Single(context.Transport.Sent);
        Assert.Equal(MessageType.HELLO, hello.Type);
        Assert.True(context.Session.HandshakeComplete);
    }

    [Fact]
    public async Task JoinTimeoutBeforeHello_ShowsInternal()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            true,
            true,
            settings: new Dictionary<string, string?> { ["Battleship:JoinTimeoutSeconds"] = "1" },
            transport: new FakeWsTransport { AutoHello = false });
        Assert.Equal("INTERNAL", context.ErrorCode);
        Assert.Contains("Timed out", context.ErrorDetail!, StringComparison.Ordinal);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1011);
    }

    [Fact]
    public async Task PreHandshakeCloseCodes_MappedPerPageErrorMapping()
    {
        var cases = new (int Code, string Error)[]
        {
            (4004, "NOT_FOUND"),
            (4009, "CONFLICT"),
            (4029, "RATE_LIMITED"),
            (4400, "VALIDATION_FAILED"),
            (1001, "UPSTREAM_UNAVAILABLE"),
            (1002, "VALIDATION_FAILED"),
            (1011, "UPSTREAM_UNAVAILABLE")
        };
        foreach (var testCase in cases)
        {
            await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
            TestComponent.SetEnum(context.Page, "_phase", "Connecting");
            TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((System.Net.WebSockets.WebSocketCloseStatus)testCase.Code, "closed"));
            Assert.Equal(testCase.Error, context.ErrorCode);
        }
    }

    [Fact]
    public async Task FiresFirstAfterBothReady()
    {
        await using var context = await StartHostPlacedAsync();
        await TestComponent.CallAsync(context.Page, "ReadyAsync");
        await context.DispatchAsync(ProtocolMessage.Ready());
        await TestComponent.CallAsync(context.Page, "FireAsync", new Battleship.Web.Game.Coordinate(9, 9));
        var shot = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.SHOT);
        Assert.Equal(1, shot.Seq);
        Assert.Equal(Side.Peer, context.Session.Turn);
        Assert.Equal(1, context.Session.PendingSeq);
    }

    private static async Task<MultiplayerContext> StartHostPlacedAsync()
    {
        var context = await MultiplayerContext.CreateAsync(true, true, transport: new FakeWsTransport { AutoHello = true });
        var session = context.Session;
        foreach (var (kind, start, horizontal) in TestBoards.Layout)
        {
            Assert.True(session.OwnBoard.TryPlace(kind, start, horizontal));
        }

        Assert.Equal("Placement", context.Phase.ToString());
        return context;
    }
}

public sealed class MultiplayerClientPageTests
{
    [Fact]
    public async Task Start_TurnPeerSeqZero()
    {
        await using var context = await StartClientAsync();
        Assert.Equal(Side.Peer, context.Session.Turn);
        Assert.Equal(1, context.Session.NextSeq);
        Assert.Null(context.Session.PendingSeq);
    }

    [Fact]
    public async Task ParticipantsZero_RetriesThenNotFound()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"participants\":0}");
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            true,
            roomCode: "ABC234",
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });
        Assert.Equal("NOT_FOUND", context.ErrorCode);
        Assert.Equal(3, handler.CallCount);
        Assert.Empty(context.Transport.ConnectUris);
    }

    [Fact]
    public async Task ParticipantsTwo_ShowsConflictWithoutConnecting()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"participants\":2}");
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            true,
            roomCode: "ABC234",
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });
        Assert.Equal("CONFLICT", context.ErrorCode);
        Assert.Empty(context.Transport.ConnectUris);
    }

    [Fact]
    public async Task SendsHelloFirst()
    {
        await using var context = await StartClientAsync();
        var hello = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.HELLO);
        Assert.Equal(ProtocolInfo.Version, hello.V);
        Assert.Equal(MessageType.HELLO, context.Transport.Sent[0].Type);
    }

    [Fact]
    public async Task HandshakeTimeout_ShowsInternal()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            true,
            roomCode: "ABC234",
            settings: new Dictionary<string, string?> { ["Battleship:HandshakeTimeoutSeconds"] = "1" },
            transport: new FakeWsTransport { AutoHello = false });
        Assert.Equal("INTERNAL", context.ErrorCode);
        Assert.Contains("handshake", context.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1011);
    }

    [Fact]
    public async Task PreHandshakeCloseCodes_MappedPerPageErrorMapping()
    {
        var cases = new (int Code, string Error)[]
        {
            (4004, "NOT_FOUND"),
            (4009, "CONFLICT"),
            (4029, "RATE_LIMITED"),
            (4400, "VALIDATION_FAILED"),
            (1001, "UPSTREAM_UNAVAILABLE"),
            (1002, "VALIDATION_FAILED"),
            (1011, "UPSTREAM_UNAVAILABLE")
        };
        foreach (var testCase in cases)
        {
            await using var context = await MultiplayerContext.CreateAsync(false, runInitialize: false, roomCode: "ABC234");
            TestComponent.SetEnum(context.Page, "_phase", "Connecting");
            TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((System.Net.WebSockets.WebSocketCloseStatus)testCase.Code, "closed"));
            Assert.Equal(testCase.Error, context.ErrorCode);
        }
    }

    [Fact]
    public async Task NeverFiresBeforeHostFirstShot()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: false);
        Assert.Equal(Side.Peer, context.Session.Turn);
        await TestComponent.CallAsync(context.Page, "FireAsync", new Battleship.Web.Game.Coordinate(9, 9));
        Assert.Empty(context.Transport.Sent);
        Assert.Empty(context.Session.Target.Shots);
    }

    private static async Task<MultiplayerContext> StartClientAsync()
    {
        var context = await MultiplayerContext.CreateAsync(
            false,
            true,
            roomCode: "abc234",
            transport: new FakeWsTransport { AutoHello = true });
        Assert.Equal("Placement", context.Phase.ToString());
        Assert.Equal("ABC234", TestComponent.Get(context.Page, "_roomCode"));
        return context;
    }
}
