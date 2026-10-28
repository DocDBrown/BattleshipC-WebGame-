using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battleship.Shared;
using Battleship.Web.Game;
using Battleship.Web.Net;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

[Collection("real-server")]
public sealed class ClientServerTests
{
    private static readonly TimeSpan SocketTimeout = TimeSpan.FromSeconds(10);
    private readonly RealServerFixture _fixture;

    public ClientServerTests(RealServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RoomApiClient_CreateRoom_AgainstRealServer()
    {
        using var http = _fixture.Server.CreateClient();
        var rooms = new RoomApiClient(http);

        var first = await rooms.CreateRoomAsync(CancellationToken.None);
        var second = await rooms.CreateRoomAsync(CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(ProtocolInfo.IsValidRoomCode(first.Value));
        Assert.True(ProtocolInfo.IsValidRoomCode(second.Value));
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public async Task RoomApiClient_GetRoom_AgainstRealServer()
    {
        using var http = _fixture.Server.CreateClient();
        var rooms = new RoomApiClient(http);
        var code = (await rooms.CreateRoomAsync(CancellationToken.None)).Value!;

        var missing = await rooms.GetRoomAsync("ZZZZZZ", CancellationToken.None);
        Assert.False(missing.Success);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);

        var empty = await rooms.GetRoomAsync(code, CancellationToken.None);
        Assert.True(empty.Success);
        Assert.Equal(0, empty.Value);

        await using var host = new RecordingWsTransport();
        await host.ConnectAsync(_fixture.Server.WsUri(code, "host"), SocketTimeout, CancellationToken.None);
        await WaitForAsync(async () => (await rooms.GetRoomAsync(code, CancellationToken.None)).Value == 1, "host was not counted");
        await using var client = new RecordingWsTransport();
        await client.ConnectAsync(_fixture.Server.WsUri(code, "client"), SocketTimeout, CancellationToken.None);
        await WaitForAsync(async () => (await rooms.GetRoomAsync(code, CancellationToken.None)).Value == 2, "client was not counted");
    }

    [Fact]
    public async Task Join_RetriesUntilHostConnected_WithinRetryCount()
    {
        using var http = _fixture.Server.CreateClient();
        var rooms = new RoomApiClient(http);
        var code = (await rooms.CreateRoomAsync(CancellationToken.None)).Value!;
        await using var context = await RealMultiplayerContext.CreateAsync(
            _fixture.Server,
            isHost: false,
            code,
            new Dictionary<string, string?>
            {
                ["Battleship:HttpTimeoutSeconds"] = "5",
                ["Battleship:JoinRetryCount"] = "10",
                ["Battleship:JoinRetryDelayMs"] = "100"
            });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var lookup = TestComponent.CallAsync<ApiResult<int>>(context.Page, "LookupRoomAsync", code);
        await Task.Delay(250);
        await using var host = new RecordingWsTransport();
        await host.ConnectAsync(_fixture.Server.WsUri(code, "host"), SocketTimeout, CancellationToken.None);
        var result = await lookup;
        watch.Stop();

        Assert.True(result.Success);
        Assert.Equal(1, result.Value);
        Assert.True(watch.ElapsedMilliseconds >= 200, "the lookup did not retry");
    }

    [Fact]
    public async Task Join_HostNeverArrives_FailsAfterJoinTimeout()
    {
        await using var host = await RealMultiplayerContext.CreateAsync(
            _fixture.Server,
            isHost: true,
            settings: new Dictionary<string, string?> { ["Battleship:JoinTimeoutSeconds"] = "1" });

        await host.StartAsync();

        Assert.Equal("Error", host.Phase);
        Assert.Equal("INTERNAL", host.ErrorCode);
        Assert.Contains("Timed out", host.ErrorDetail!, StringComparison.Ordinal);
        Assert.Contains("opponent", host.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoTransports_HelloHandshake_Completes()
    {
        using var http = _fixture.Server.CreateClient();
        var code = (await new RoomApiClient(http).CreateRoomAsync(CancellationToken.None)).Value!;
        await using var host = new RecordingWsTransport();
        await using var client = new RecordingWsTransport();
        await host.ConnectAsync(_fixture.Server.WsUri(code, "host"), SocketTimeout, CancellationToken.None);
        await client.ConnectAsync(_fixture.Server.WsUri(code, "client"), SocketTimeout, CancellationToken.None);
        var codec = new ProtocolCodec();

        await client.SendAsync(ProtocolMessage.Hello(), CancellationToken.None);
        var hostFrame = await ReceiveAsync(host);
        Assert.True(codec.TryDeserialize(hostFrame.Text!, out var hostHello, out var hostError), hostError);
        Assert.Equal(MessageType.HELLO, hostHello!.Type);

        await host.SendAsync(ProtocolMessage.Hello(), CancellationToken.None);
        var clientFrame = await ReceiveAsync(client);
        Assert.True(codec.TryDeserialize(clientFrame.Text!, out var clientHello, out var clientError), clientError);
        Assert.Equal(MessageType.HELLO, clientHello!.Type);
    }

    [Fact]
    public async Task TwoSessions_PlayFullGame_BothAgreeOnWinner()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await RealGameDriver.PlayAsync(host, client);

        Assert.Equal("GameOver", host.Phase);
        Assert.Equal("GameOver", client.Phase);
        Assert.Equal(Side.Self, host.Session.Winner);
        Assert.Equal(Side.Peer, client.Session.Winner);
        Assert.Equal("fleet_sunk", host.Session.EndReason);
        Assert.Equal("fleet_sunk", client.Session.EndReason);
    }

    [Fact]
    public async Task TwoSessions_GameOverQuitRule_BothCloseWith1000()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await RealGameDriver.PlayAsync(host, client);
        await RealGameDriver.WaitUntilAsync(
            () => host.Transport.Closes.Any(close => close.Code == 1000)
                  && client.Transport.Closes.Any(close => close.Code == 1000),
            "both sessions did not close with 1000");

        Assert.Contains(host.Transport.SentTexts, text => text.Contains("\"QUIT\"", StringComparison.Ordinal));
        Assert.Contains(host.Transport.Closes, close => close.Code == 1000);
        Assert.Contains(client.Transport.Closes, close => close.Code == 1000);
    }

    [Fact]
    public async Task FullGame_NoFrameContainsUnhitShipCoordinates()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await RealGameDriver.PlayAsync(host, client);

        AssertDoesNotLeakOwnFleet(host, RealGameDriver.HostLayout, RealGameDriver.ClientLayout);
        AssertDoesNotLeakOwnFleet(client, RealGameDriver.ClientLayout, RealGameDriver.HostLayout);
    }

    [Fact]
    public async Task PreHandshakeServerCloses_MappedToPageErrors()
    {
        using var http = _fixture.Server.CreateClient();
        var rooms = new RoomApiClient(http);
        var code = (await rooms.CreateRoomAsync(CancellationToken.None)).Value!;
        var notFound = await ReadCloseAsync(_fixture.Server.WsUri("ZZZZZZ", "client"));
        var invalidRole = await ReadCloseAsync(_fixture.Server.WsUri(code, "spectator"));

        var fullCode = (await rooms.CreateRoomAsync(CancellationToken.None)).Value!;
        await using var fullHost = new RecordingWsTransport();
        await using var fullClient = new RecordingWsTransport();
        await fullHost.ConnectAsync(_fixture.Server.WsUri(fullCode, "host"), SocketTimeout, CancellationToken.None);
        await fullClient.ConnectAsync(_fixture.Server.WsUri(fullCode, "client"), SocketTimeout, CancellationToken.None);
        var full = await ReadCloseAsync(_fixture.Server.WsUri(fullCode, "client"));

        await using var tight = await RealBattleshipServer.StartAsync(new Dictionary<string, string?>
        {
            ["ConnectPermitsPerMinute"] = "1"
        });
        using var tightHttp = tight.CreateClient();
        var tightCode = (await new RoomApiClient(tightHttp).CreateRoomAsync(CancellationToken.None)).Value!;
        await using var firstConnection = new RecordingWsTransport();
        await firstConnection.ConnectAsync(tight.WsUri(tightCode, "host"), SocketTimeout, CancellationToken.None);
        var rateLimited = await ReadCloseAsync(tight.WsUri(tightCode, "client"));

        var cases = new (WsFrame Frame, int Code, string Error)[]
        {
            (notFound, 4004, "NOT_FOUND"),
            (invalidRole, 4400, "VALIDATION_FAILED"),
            (full, 4009, "CONFLICT"),
            (rateLimited, 4029, "RATE_LIMITED")
        };

        await using var page = await RealMultiplayerContext.CreateAsync(_fixture.Server, isHost: true);
        foreach (var testCase in cases)
        {
            Assert.Equal(testCase.Code, (int)testCase.Frame.CloseStatus!.Value);
            TestComponent.SetEnum(page.Page, "_phase", "Connecting");
            TestComponent.Call(page.Page, "OnSocketClosed", testCase.Frame);
            Assert.Equal(testCase.Error, page.ErrorCode);
        }
    }

    [Fact]
    public async Task PeerDisconnect_SessionEndsWithReason()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await client.Transport.CloseAsync(1000, "leaving");
        await RealGameDriver.WaitUntilAsync(() => host.Phase == "GameOver", "the host did not observe the disconnect");
        await RealGameDriver.WaitForHtmlAsync(host, "opponent left", "the disconnect notice was not rendered");

        Assert.Equal("peer_left", host.Session.EndReason);
        Assert.Equal(Side.Self, host.Session.Winner);
        Assert.Contains("opponent left", host.Notice!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionMismatch_SessionEntersError()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;
        var text = new ProtocolCodec().Serialize(new ProtocolMessage
        {
            V = ProtocolInfo.Version + 1,
            Type = MessageType.CHAT,
            Text = "version probe"
        });

        await client.Transport.SendTextAsync(text, CancellationToken.None);
        await RealGameDriver.WaitUntilAsync(() => client.Phase == "Error", "the version mismatch was not surfaced");

        Assert.Equal("VALIDATION_FAILED", client.ErrorCode);
        Assert.Contains("version", client.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
        await RealGameDriver.WaitForHtmlAsync(client, "Reload", "the reload message was not rendered");
        Assert.NotEqual("Error", host.Phase);
    }

    [Fact]
    public async Task RateLimitedError_SurfacedToSession()
    {
        await using var tight = await RealBattleshipServer.StartAsync(new Dictionary<string, string?>
        {
            ["MessagesPerSecond"] = "1",
            ["MessageBurst"] = "2"
        });
        var pair = await RealGameDriver.ConnectAsync(tight);
        await using var host = pair.Host;
        await using var client = pair.Client;
        var codec = new ProtocolCodec();

        for (var index = 0; index < 8; index++)
        {
            await client.Transport.SendTextAsync(codec.Serialize(ProtocolMessage.Chat($"message {index}")), CancellationToken.None);
        }

        await RealGameDriver.WaitUntilAsync(
            () => client.Notice?.Contains("rate limiting", StringComparison.OrdinalIgnoreCase) == true,
            "the rate limit notice was not surfaced");
        Assert.NotEqual("Error", client.Phase);
        await RealGameDriver.WaitForHtmlAsync(client, "Slow down", "the rate limit notice was not rendered");
    }

    [Fact]
    public async Task HandshakeTimeout_SessionEntersError()
    {
        using var http = _fixture.Server.CreateClient();
        var code = (await new RoomApiClient(http).CreateRoomAsync(CancellationToken.None)).Value!;
        await using var silentHost = new RecordingWsTransport();
        await silentHost.ConnectAsync(_fixture.Server.WsUri(code, "host"), SocketTimeout, CancellationToken.None);
        await using var client = await RealMultiplayerContext.CreateAsync(
            _fixture.Server,
            isHost: false,
            code,
            new Dictionary<string, string?> { ["Battleship:HandshakeTimeoutSeconds"] = "1" });

        await client.StartAsync();

        Assert.Equal("Error", client.Phase);
        Assert.Equal("INTERNAL", client.ErrorCode);
        Assert.Contains("handshake", client.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadTimeout_SessionEntersError()
    {
        await using var host = await RealMultiplayerContext.CreateAsync(
            _fixture.Server,
            isHost: true,
            settings: new Dictionary<string, string?>
            {
                ["Battleship:ReadTimeoutSeconds"] = "1",
                ["Battleship:JoinTimeoutSeconds"] = "10"
            });
        var hostStart = host.StartAsync();
        await RealGameDriver.WaitUntilAsync(
            () => host.RoomCode is not null && host.Phase == "WaitingForPeer",
            "the host did not open the room");

        await using var silentClient = new RecordingWsTransport();
        await silentClient.ConnectAsync(_fixture.Server.WsUri(host.RoomCode!, "client"), SocketTimeout, CancellationToken.None);
        await silentClient.SendAsync(ProtocolMessage.Hello(), CancellationToken.None);
        await hostStart;
        TestComponent.SetEnum(host.Page, "_phase", "Combat");
        host.Session.Turn = Side.Self;

        await host.FireAsync(new Coordinate(9, 9));
        await RealGameDriver.WaitUntilAsync(() => host.Phase == "Error", "the result timeout was not surfaced", 5000);

        Assert.Equal("INTERNAL", host.ErrorCode);
        Assert.Contains("Timed out", host.ErrorDetail!, StringComparison.Ordinal);
        Assert.Contains("result", host.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_SanitizedEndToEnd()
    {
        var pair = await RealGameDriver.ConnectAsync(_fixture.Server);
        await using var host = pair.Host;
        await using var client = pair.Client;

        await client.SendChatAsync("<b>hi</b>\u202E");
        await RealGameDriver.WaitUntilAsync(() => host.Session.Chat.Count > 0, "the chat was not delivered");
        var entry = Assert.Single(host.Session.Chat);
        Assert.Equal("<b>hi</b>", entry.Text);
        Assert.DoesNotContain('\u202E', entry.Text);
        Assert.All(client.Transport.SentTexts, text => Assert.DoesNotContain("\u202E", text, StringComparison.Ordinal));
        await RealGameDriver.WaitForHtmlAsync(host, "&lt;b&gt;hi&lt;/b&gt;", "the chat HTML was not rendered as text");
        Assert.DoesNotContain("<b>hi</b>", host.Html, StringComparison.Ordinal);
    }

    private static void AssertDoesNotLeakOwnFleet(
        RealMultiplayerContext context,
        (ShipKind Kind, Coordinate Start, bool Horizontal)[] ownLayout,
        (ShipKind Kind, Coordinate Start, bool Horizontal)[] opponentLayout)
    {
        var own = Labels(ownLayout);
        var opponent = Labels(opponentLayout);
        var coordinateCount = 0;
        foreach (var frame in context.Transport.SentTexts)
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.TryGetProperty("coord", out var coord) && coord.ValueKind == JsonValueKind.String)
            {
                coordinateCount++;
                var value = coord.GetString()!;
                Assert.Contains(value, opponent);
                Assert.DoesNotContain(value, own);
            }

            foreach (var name in new[] { "text", "detail" })
            {
                if (!root.TryGetProperty(name, out var text) || text.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = text.GetString()!;
                Assert.All(own, label => Assert.DoesNotContain(label, value, StringComparison.OrdinalIgnoreCase));
            }
        }

        Assert.True(coordinateCount > 0, "no shot coordinates were sent");
    }

    private static HashSet<string> Labels((ShipKind Kind, Coordinate Start, bool Horizontal)[] layout)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (kind, start, _) in layout)
        {
            for (var offset = 0; offset < Fleet.SizeOf(kind); offset++)
            {
                labels.Add(new Coordinate(start.Row, start.Col + offset).Label);
            }
        }

        return labels;
    }

    private async Task<WsFrame> ReadCloseAsync(Uri uri)
    {
        await using var transport = new RecordingWsTransport();
        await transport.ConnectAsync(uri, SocketTimeout, CancellationToken.None);
        return await ReceiveAsync(transport);
    }

    private static async Task<WsFrame> ReceiveAsync(WsTransport transport)
    {
        using var source = new CancellationTokenSource(SocketTimeout);
        return await transport.ReceiveAsync(source.Token);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string message, int timeoutMs = 10000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(message);
    }
}
