using System.Net.WebSockets;
using System.Text.Json;
using Battleship.Shared;
using Battleship.Web.Game;
using Battleship.Web.Net;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class CloseCodeMappingTests
{
    [Fact]
    public async Task PreHandshake_4004_4009_4029_4400_MapToNotFoundConflictRateLimitedValidationFailed()
    {
        var cases = new (int Code, string Error)[]
        {
            (4004, "NOT_FOUND"),
            (4009, "CONFLICT"),
            (4029, "RATE_LIMITED"),
            (4400, "VALIDATION_FAILED")
        };
        foreach (var testCase in cases)
        {
            await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
            TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)testCase.Code, null));
            Assert.Equal(testCase.Error, context.ErrorCode);
        }
    }

    [Fact]
    public async Task Map_1001And1011_ToUpstreamUnavailable()
    {
        foreach (var code in new[] { 1001, 1011 })
        {
            await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
            TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)code, null));
            Assert.Equal("UPSTREAM_UNAVAILABLE", context.ErrorCode);
        }
    }

    [Fact]
    public async Task Map_1002_ToValidationFailed()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1002, null));
        Assert.Equal("VALIDATION_FAILED", context.ErrorCode);
    }

    [Fact]
    public async Task Map_UnlistedCode_ToInternal()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1234, null));
        Assert.Equal("UPSTREAM_UNAVAILABLE", context.ErrorCode);
        Assert.Contains("1234", context.ErrorDetail!, StringComparison.Ordinal);
    }
}

public sealed class ProtocolHandlerTests
{
    [Fact]
    public async Task RecvHello_VersionMismatch_SendsErrorAndCloses1002()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.SetEnum(context.Page, "_phase", "Connecting");
        context.Transport.Enqueue(WsFrame.FromText(new ProtocolCodec().Serialize(new ProtocolMessage { V = 2, Type = MessageType.HELLO })));
        await context.ReceiveLoopAsync();
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.VERSION_MISMATCH);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1002);
    }

    [Fact]
    public async Task RecvHello_Malformed_SendsErrorAndCloses1002()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.SetEnum(context.Page, "_phase", "Connecting");
        context.Transport.Enqueue(WsFrame.FromText("not-json"));
        await context.ReceiveLoopAsync();
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.VALIDATION);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1002);
        Assert.Equal("VALIDATION_FAILED", context.ErrorCode);
    }

    [Fact]
    public async Task RecvNonHelloBeforeHandshake_Rejected()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        await context.DispatchAsync(ProtocolMessage.Ready());
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR);
        Assert.False(context.Session.HandshakeComplete);
    }

    [Fact]
    public async Task RecvReady_BeforeHandshake_RespondsErrorConflict()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        await context.DispatchAsync(ProtocolMessage.Ready());
        var error = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.ERROR);
        Assert.Equal(ProtocolErrorCode.VALIDATION, error.Code);
        Assert.Contains("before HELLO", error.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecvReady_Duplicate_RespondsErrorConflict()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Ready());
        var error = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.ERROR);
        Assert.Equal(ProtocolErrorCode.CONFLICT, error.Code);
    }

    [Fact]
    public async Task RecvShot_SeqNotLastPlusOne_RespondsErrorTurnUnchanged()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        await context.DispatchAsync(ProtocolMessage.Shot(5, "E5"));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
        Assert.Equal(Side.Peer, context.Session.Turn);
    }

    [Fact]
    public async Task RecvShot_ResultEchoesSeq()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        await context.DispatchAsync(ProtocolMessage.Shot(1, "E5"));
        var result = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.RESULT);
        Assert.Equal(1, result.Seq);
    }

    [Fact]
    public async Task RecvShot_ResultSendFails_ReleasesSocket()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        context.Transport.SendFailure = message => message.Type == MessageType.RESULT ? new IOException("gone") : null;
        context.Transport.Enqueue(ProtocolMessage.Shot(1, "E5"));
        await context.ReceiveLoopAsync();
        Assert.Equal("INTERNAL", context.ErrorCode);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1011);
    }

    [Fact]
    public async Task RecvShot_GameOver_SetsWinnerPeer()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        var seq = 1;
        foreach (var cell in context.Session.OwnBoard.Ships.SelectMany(ship => ship.Cells))
        {
            context.Session.Turn = Side.Peer;
            await context.DispatchAsync(ProtocolMessage.Shot(seq++, cell.Label));
        }

        Assert.Equal(Side.Peer, context.Session.Winner);
        Assert.Equal("fleet_sunk", context.Session.EndReason);
        Assert.Equal("GameOver", context.Phase.ToString());
    }

    [Fact]
    public async Task RecvShot_Valid_TurnPassesToSelf()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        await context.DispatchAsync(ProtocolMessage.Shot(1, "E5"));
        Assert.Equal(Side.Self, context.Session.Turn);
        Assert.Equal(1, context.Session.PeerLastSeq);
    }

    [Fact]
    public async Task RecvResult_Valid_TurnPassesToPeer()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.PendingSeq = 1;
        context.Session.PendingCoordinate = new Coordinate(9, 9);
        await context.DispatchAsync(ProtocolMessage.Result(1, ShotOutcome.HIT));
        Assert.Equal(Side.Peer, context.Session.Turn);
        Assert.Null(context.Session.PendingSeq);
    }

    [Fact]
    public async Task RecvResult_GameOver_SetsWinnerSelf()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.PendingSeq = 1;
        context.Session.PendingCoordinate = new Coordinate(9, 9);
        await context.DispatchAsync(ProtocolMessage.Result(1, ShotOutcome.GAME_OVER, ShipKind.CARRIER));
        Assert.Equal(Side.Self, context.Session.Winner);
        Assert.Equal("GameOver", context.Phase.ToString());
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.QUIT);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
    }

    [Fact]
    public async Task RecvChat_BeforeHandshake_LoggedNotRendered()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        await context.DispatchAsync(ProtocolMessage.Chat("hello"));
        Assert.Empty(context.Session.Chat);
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR);
    }

    [Fact]
    public async Task RecvChat_NeverChangesTurnSeqOrBoards()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        var turn = context.Session.Turn;
        var seq = context.Session.NextSeq;
        var ships = context.Session.OwnBoard.Ships.Count;
        var shots = context.Session.Target.Shots.Count;
        await context.DispatchAsync(ProtocolMessage.Chat("hello there"));
        Assert.Equal(turn, context.Session.Turn);
        Assert.Equal(seq, context.Session.NextSeq);
        Assert.Equal(ships, context.Session.OwnBoard.Ships.Count);
        Assert.Equal(shots, context.Session.Target.Shots.Count);
        Assert.Contains(context.Session.Chat, entry => entry.Side == Side.Peer && entry.Text == "hello there");
    }

    [Fact]
    public async Task RecvChat_RenderedAsTextNodeNeverMarkup()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Chat("<script>alert(1)</script>"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", context.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecvQuit_NoWinner_LocalWinsByForfeit()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Quit());
        Assert.Equal(Side.Self, context.Session.Winner);
        Assert.Equal("peer_left", context.Session.EndReason);
        Assert.Equal("GameOver", context.Phase.ToString());
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
    }

    [Fact]
    public async Task RecvQuit_WinnerDeclared_WinnerKept()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Winner = Side.Peer;
        context.Session.EndReason = "fleet_sunk";
        await context.DispatchAsync(ProtocolMessage.Quit());
        Assert.Equal(Side.Peer, context.Session.Winner);
        Assert.Equal("fleet_sunk", context.Session.EndReason);
    }

    [Fact]
    public async Task RecvQuit_ClosesWith1000AndDisposes()
    {
        var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Quit());
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
        await context.Page.DisposeAsync();
        Assert.Equal(1, context.Transport.DisposeCount);
        context.Harness.Dispose();
        context.Environment.Dispose();
    }

    [Fact]
    public async Task RecvError_VersionMismatchOrFatal_IsFatal()
    {
        await using var version = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await version.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.VERSION_MISMATCH, "reload"));
        Assert.Equal("VALIDATION_FAILED", version.ErrorCode);
        Assert.Equal("Error", version.Phase.ToString());

        await using var fatal = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await fatal.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.FATAL, "down"));
        Assert.Equal("INTERNAL", fatal.ErrorCode);
        Assert.Equal("Error", fatal.Phase.ToString());
    }

    [Fact]
    public async Task RecvError_BeforeHandshakeNonRateLimited_IsFatal()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.FATAL, "down"));
        Assert.Equal("UPSTREAM_UNAVAILABLE", context.ErrorCode);
        Assert.Equal("Error", context.Phase.ToString());
    }

    [Fact]
    public async Task RecvError_RateLimited_NeverFatal()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.RATE_LIMITED, "slow down"));
        Assert.Null(context.ErrorCode);
        Assert.Equal("Connecting", context.Phase.ToString());
        Assert.Contains("rate limiting", context.Notice!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecvError_ValidationOrConflict_ClearsPendingSeqKeepsTurn()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        context.Session.PendingSeq = 2;
        context.Session.PendingCoordinate = new Coordinate(9, 9);
        context.Session.NextSeq = 3;
        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.VALIDATION, "bad result"));
        Assert.Null(context.Session.PendingSeq);
        Assert.Equal(Side.Self, context.Session.Turn);
        Assert.Equal(2, context.Session.NextSeq);
    }

    [Fact]
    public async Task RecvError_DetailRenderedSanitizedAsText()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.VALIDATION, "<b>bad</b>\u202E"));
        Assert.Equal("<b>bad</b>", context.Notice);
        Assert.Contains("&lt;b&gt;bad&lt;/b&gt;", context.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>bad</b>", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecvError_VersionMismatch_ShowsReloadInstruction()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Error(ProtocolErrorCode.VERSION_MISMATCH, "Protocol version mismatch. Reload the page."));
        Assert.Contains("Reload", context.ErrorDetail!, StringComparison.Ordinal);
        Assert.Contains("Reload", context.Html, StringComparison.Ordinal);
    }
}

public sealed class GameOverRuleTests
{
    [Fact]
    public async Task WinnerSendsQuitThenCloses1000()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await TestComponent.CallAsync(context.Page, "FinishAsWinnerAsync");
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.QUIT);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
    }

    [Fact]
    public async Task LoserWaitsHandshakeTimeoutForQuitThenCloses1000()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(
            isHost: true,
            settings: new Dictionary<string, string?> { ["Battleship:HandshakeTimeoutSeconds"] = "0" });
        context.Session.Winner = Side.Peer;
        context.Session.EndReason = "fleet_sunk";
        TestComponent.SetEnum(context.Page, "_phase", "GameOver");
        await TestComponent.CallAsync(context.Page, "WatchQuitTimeoutAsync");
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
        Assert.DoesNotContain(context.Transport.Sent, message => message.Type == MessageType.QUIT);
    }
}

public sealed class ConnectionLostTests
{
    [Fact]
    public async Task Close4001_NoWinner_LocalWinsByForfeit()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)4001, "left"));
        Assert.Equal(Side.Self, context.Session.Winner);
        Assert.Equal("peer_left", context.Session.EndReason);
    }

    [Fact]
    public async Task Close4001_WinnerDeclared_WinnerKept()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Winner = Side.Peer;
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)4001, "left"));
        Assert.Equal(Side.Peer, context.Session.Winner);
    }

    [Fact]
    public async Task OtherCode_EndsWithNoWinnerAndReason()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1006, "lost"));
        Assert.Null(context.Session.Winner);
        Assert.Equal("connection_lost", context.Session.EndReason);
        Assert.Equal("GameOver", context.Phase.ToString());
    }

    [Fact]
    public async Task AfterQuit_NotTriggered()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Quit());
        var notice = context.Notice;
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1006, "lost"));
        Assert.Equal("peer_left", context.Session.EndReason);
        Assert.Equal(notice, context.Notice);
    }

    [Fact]
    public async Task ShowsReturnToMenu()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1006, "lost"));
        context.Harness.Refresh();
        Assert.Contains("Return to menu", context.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsOncePerSocket()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)1006, "lost"));
        var notice = context.Notice;
        var reason = context.Session.EndReason;
        TestComponent.Call(context.Page, "OnSocketClosed", WsFrame.Close((WebSocketCloseStatus)4001, "left"));
        Assert.Equal(notice, context.Notice);
        Assert.Equal(reason, context.Session.EndReason);
    }
}

public sealed class SessionIntegrationTests
{
    [Fact]
    public async Task OutgoingShots_SeqStrictlyIncreasing()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await TestComponent.CallAsync(context.Page, "FireAsync", new Coordinate(9, 9));
        await context.DispatchAsync(ProtocolMessage.Result(1, ShotOutcome.MISS));
        context.Session.Turn = Side.Self;
        await TestComponent.CallAsync(context.Page, "FireAsync", new Coordinate(8, 8));
        var sequences = context.Transport.Sent.Where(message => message.Type == MessageType.SHOT).Select(message => message.Seq!.Value).ToArray();
        Assert.Equal(new[] { 1, 2 }, sequences);
    }

    [Fact]
    public async Task IncomingResult_MismatchedSeq_Rejected()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.PendingSeq = 1;
        context.Session.PendingCoordinate = new Coordinate(9, 9);
        await context.DispatchAsync(ProtocolMessage.Result(2, ShotOutcome.HIT));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
        Assert.Equal(1, context.Session.PendingSeq);
    }

    [Fact]
    public async Task IncomingResult_WithoutPendingShot_Rejected()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Result(1, ShotOutcome.HIT));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
    }

    [Fact]
    public async Task PeerShotOutOfTurn_RespondsError()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.DispatchAsync(ProtocolMessage.Shot(1, "E5"));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
    }

    [Fact]
    public async Task PeerShotOnTriedCell_RespondsError()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Peer;
        context.Session.OwnBoard.RecordShot(new Coordinate(4, 4), ShotOutcome.MISS);
        await context.DispatchAsync(ProtocolMessage.Shot(1, "E5"));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
    }

    [Fact]
    public async Task ShotBeforeHandshakeOrReady_Rejected()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        TestComponent.SetEnum(context.Page, "_phase", "Placement");
        context.Session.Turn = Side.Peer;
        await context.DispatchAsync(ProtocolMessage.Shot(1, "E5"));
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.ERROR && message.Code == ProtocolErrorCode.CONFLICT);
    }

    [Fact]
    public async Task OutgoingFrames_NeverContainOwnUnhitShipCoordinates()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await TestComponent.CallAsync(context.Page, "FireAsync", new Coordinate(9, 9));
        await context.DispatchAsync(ProtocolMessage.Chat("hello"));
        var codec = new ProtocolCodec();
        foreach (var message in context.Transport.Sent)
        {
            using var document = JsonDocument.Parse(codec.Serialize(message));
            var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.DoesNotContain("cells", properties);
            Assert.DoesNotContain("ships", properties);
            Assert.DoesNotContain("fleet", properties);
        }
    }

    [Fact]
    public async Task ChatSend_SanitizedAndTurnUnchanged()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        context.Session.Turn = Side.Self;
        await TestComponent.CallAsync(context.Page, "SendChatAsync", " <b>hi</b>\u202E ");
        var chat = Assert.Single(context.Transport.Sent, message => message.Type == MessageType.CHAT);
        Assert.Equal("<b>hi</b>", chat.Text);
        Assert.Equal(Side.Self, context.Session.Turn);
        Assert.Contains(context.Session.Chat, entry => entry.Side == Side.Self && entry.Text == "<b>hi</b>");
    }

    [Fact]
    public async Task ReadTimeoutAwaitingPeer_EndsWithInternal()
    {
        await using var context = await MultiplayerContext.PrepareCombatAsync(isHost: false);
        TestComponent.Set(context.Page, "_options", new GameOptions { ReadTimeoutSeconds = 0 });
        TestComponent.Set(context.Page, "_peerTurnWatchVersion", 1);
        await TestComponent.CallAsync(context.Page, "WatchPeerTurnTimeoutAsync", 1);
        Assert.Equal("INTERNAL", context.ErrorCode);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1011);
    }

    [Fact]
    public async Task Dispose_SendsQuitThenCloses()
    {
        var context = await MultiplayerContext.PrepareCombatAsync(isHost: true);
        await context.Page.DisposeAsync();
        Assert.Contains(context.Transport.Sent, message => message.Type == MessageType.QUIT);
        Assert.Contains(context.Transport.Closes, close => close.Code == 1000);
        context.Harness.Dispose();
        context.Environment.Dispose();
    }
}
