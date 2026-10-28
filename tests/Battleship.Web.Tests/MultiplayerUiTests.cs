using System.Net;
using System.Net.Http;
using Battleship.Shared;
using Battleship.Web.Net;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

[Collection("real-server")]
public sealed class MultiplayerUiTests
{
    private readonly RealServerFixture _fixture;

    public MultiplayerUiTests(RealServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Host_CreateRoom_ShowsCodeAndProtocolVersion()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            true,
            runInitialize: true,
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("Placement", context.Phase.ToString());
        Assert.Contains("ABC234", context.Html, StringComparison.Ordinal);
        Assert.Contains("protocol v1", context.Html, StringComparison.Ordinal);
        Assert.Contains("Host a game", context.Html, StringComparison.Ordinal);
        Assert.Contains("id=\"join-link\"", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyRoomCode_WritesToClipboard()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            true,
            runInitialize: true,
            transport: new FakeWsTransport { AutoHello = true });

        await TestComponent.CallAsync(context.Page, "CopyLinkAsync");

        var call = Assert.Single(context.Environment.Js.Calls);
        Assert.Equal("battleship.copyText", call.Identifier);
        Assert.Contains("ABC234", (string)call.Arguments[0]!, StringComparison.Ordinal);
        Assert.Equal("Join link copied.", TestComponent.Get(context.Page, "_notice"));
        context.Harness.Refresh();
        Assert.Contains("Join link copied.", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_LowercaseCode_Accepted()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "abc234",
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("Placement", context.Phase.ToString());
        Assert.Equal("ABC234", TestComponent.Get(context.Page, "_roomCode"));
        Assert.Equal("/ws/rooms/ABC234", Assert.Single(context.Transport.ConnectUris).AbsolutePath);
    }

    [Fact]
    public async Task Join_InvalidCode_ShowsValidationMessage()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "bad",
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("VALIDATION_FAILED", context.ErrorCode);
        Assert.Empty(context.Transport.ConnectUris);
        Assert.Contains("not valid", context.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALIDATION_FAILED", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_UnknownRoom_ShowsNotFound()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{\"detail\":\"No room with that code exists.\"}");
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "ABC234",
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("NOT_FOUND", context.ErrorCode);
        Assert.Contains("No room with that code exists.", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_FullRoom_ShowsRoomFull()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"participants\":2}");
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "ABC234",
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("CONFLICT", context.ErrorCode);
        Assert.Contains("already full", context.ErrorDetail!, StringComparison.Ordinal);
        Assert.Contains("CONFLICT", context.Html, StringComparison.Ordinal);
        Assert.Contains("already full", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_HostNotYetConnected_RetriesThenJoins()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            var body = call < 3 ? "{\"participants\":0}" : "{\"participants\":1}";
            return Task.FromResult(StubHttpMessageHandler.Response(HttpStatusCode.OK, body));
        });
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "ABC234",
            settings: new Dictionary<string, string?>
            {
                ["Battleship:JoinRetryCount"] = "5",
                ["Battleship:JoinRetryDelayMs"] = "0"
            },
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("Placement", context.Phase.ToString());
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task TwoBrowsers_PlayFullGame()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await RealGameDriver.PlayAsync(host, client);

        Assert.Equal("GameOver", host.Phase);
        Assert.Equal("GameOver", client.Phase);
        Assert.Equal(Side.Self, host.Session.Winner);
        Assert.Equal(Side.Peer, client.Session.Winner);
        await RealGameDriver.WaitForHtmlAsync(host, "You win!", "the host win screen was not rendered");
        await RealGameDriver.WaitForHtmlAsync(client, "You lose.", "the client loss screen was not rendered");
    }

    [Fact]
    public async Task TurnIndicator_ReflectsCurrentTurn()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Harness.Refresh();
        Assert.Contains("Your turn", context.Html, StringComparison.Ordinal);

        context.Session.Turn = Side.Peer;
        context.Harness.Refresh();
        Assert.Contains("Opponent's turn", context.Html, StringComparison.Ordinal);

        context.Session.PendingSeq = 1;
        context.Harness.Refresh();
        Assert.Contains("Waiting for the result", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_SendAndReceive()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        await TestComponent.CallAsync(context.Page, "SendChatAsync", "hello");
        await context.DispatchAsync(ProtocolMessage.Chat("hi"));
        context.Harness.Refresh();

        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.CHAT && message.Text == "hello");
        Assert.Contains(context.Session.Chat, entry => entry.Side == Side.Self && entry.Text == "hello");
        Assert.Contains(context.Session.Chat, entry => entry.Side == Side.Peer && entry.Text == "hi");
        Assert.Contains("hello", context.Html, StringComparison.Ordinal);
        Assert.Contains("hi", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_DoesNotChangeTurn()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        var turn = context.Session.Turn;
        var sequence = context.Session.NextSeq;
        var shots = context.Session.Target.Shots.Count;

        await context.DispatchAsync(ProtocolMessage.Chat("side channel"));

        Assert.Equal(turn, context.Session.Turn);
        Assert.Equal(sequence, context.Session.NextSeq);
        Assert.Equal(shots, context.Session.Target.Shots.Count);
        Assert.Equal("side channel", context.Session.Chat[^1].Text);
    }

    [Fact]
    public async Task Chat_HtmlPayload_RenderedAsText()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        await context.DispatchAsync(ProtocolMessage.Chat("<img src=x onerror=alert(1)>"));
        context.Harness.Refresh();

        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", context.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x onerror=alert(1)>", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_BidiOverride_Stripped()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        await context.DispatchAsync(ProtocolMessage.Chat("safe\u202Etext"));
        await TestComponent.CallAsync(context.Page, "SendChatAsync", "a\u2066b");
        context.Harness.Refresh();

        Assert.Equal("safetext", context.Session.Chat[0].Text);
        Assert.DoesNotContain('\u202E', context.Html);
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.CHAT && message.Text == "ab");
    }

    [Fact]
    public async Task Chat_MaxLength_Enforced()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        var longText = new string('a', 250);

        await context.DispatchAsync(ProtocolMessage.Chat(longText));
        context.Harness.Refresh();

        Assert.Equal(200, context.Session.Chat[^1].Text.Length);
        Assert.Contains("maxlength=\"200\"", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpponentDisconnect_ShowsMessage()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((System.Net.WebSockets.WebSocketCloseStatus)4001, "gone"));
        context.Harness.Refresh();

        Assert.Equal("GameOver", context.Phase.ToString());
        Assert.Equal("peer_left", context.Session.EndReason);
        Assert.Equal(Side.Self, context.Session.Winner);
        Assert.Contains("opponent left", context.Notice!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opponent left", context.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionLost_ShowsReasonAndReturnToMenu()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((System.Net.WebSockets.WebSocketCloseStatus)1006, "lost"));
        context.Harness.Refresh();

        Assert.Equal("GameOver", context.Phase.ToString());
        Assert.Equal("connection_lost", context.Session.EndReason);
        Assert.Contains("connection was lost", context.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Return to menu", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quit_NotifiesOpponent()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        await TestComponent.CallAsync(context.Page, "QuitAsync");

        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.QUIT);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
        Assert.Equal("https://example.test/", context.Environment.Navigation.Records[^1].Uri);
    }

    [Fact]
    public async Task ReloadMidGame_OpponentWinsByForfeitAndRoomDisposed()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        var code = pair.Host.RoomCode!;
        await using var host = pair.Host;
        await using var client = pair.Client;

        await client.Page.DisposeAsync();
        await RealGameDriver.WaitUntilAsync(() => host.Phase == "GameOver", "the host did not win by forfeit");
        Assert.Equal(Side.Self, host.Session.Winner);
        Assert.Equal("peer_left", host.Session.EndReason);

        await host.Page.DisposeAsync();
        using var http = _fixture.Server.CreateClient();
        var rooms = new RoomApiClient(http);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            var room = await rooms.GetRoomAsync(code, CancellationToken.None);
            if (!room.Success && room.ErrorCode == "NOT_FOUND")
            {
                break;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("the room was not disposed");
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task VersionMismatch_ShowsReloadMessage()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);

        context.Transport.Enqueue(WsFrame.FromText(new ProtocolCodec().Serialize(new ProtocolMessage
        {
            V = ProtocolInfo.Version + 1,
            Type = MessageType.CHAT,
            Text = "future"
        })));
        await context.ReceiveLoopAsync();
        context.Harness.Refresh();

        Assert.Equal("Error", context.Phase.ToString());
        Assert.Equal("VALIDATION_FAILED", context.ErrorCode);
        Assert.Contains("out of date. Reload", context.Html, StringComparison.Ordinal);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1002);
    }

    [Fact]
    public async Task RateLimited_ShowsMessage()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.PendingSeq = 1;
        context.Session.PendingCoordinate = new Battleship.Web.Game.Coordinate(4, 4);

        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.RATE_LIMITED, "Too many messages."));
        context.Harness.Refresh();

        Assert.Equal("Combat", context.Phase.ToString());
        Assert.Null(context.Session.PendingSeq);
        Assert.Equal(Side.Self, context.Session.Turn);
        Assert.Contains("Slow down", context.Notice!, StringComparison.Ordinal);
        Assert.Contains("Slow down", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerUnreachable_ShowsError()
    {
        var handler = StubHttpMessageHandler.Throws(new HttpRequestException("offline"));
        await using var context = await MultiplayerContext.CreateAsync(
            true,
            runInitialize: true,
            handler: handler,
            transport: new FakeWsTransport { AutoHello = true });

        Assert.Equal("UPSTREAM_UNAVAILABLE", context.ErrorCode);
        Assert.Contains("could not be reached", context.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPSTREAM_UNAVAILABLE", context.Html, StringComparison.Ordinal);
        Assert.Contains("could not be reached", context.Html, StringComparison.OrdinalIgnoreCase);
    }
}
