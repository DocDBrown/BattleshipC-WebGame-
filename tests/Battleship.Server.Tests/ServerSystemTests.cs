using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Battleship.Server.Options;
using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class ServerSystemTests
{
    private static async Task<(HttpClient Client, string Code, WebSocket Host, WebSocket Peer)> PairAsync(RealServerFactory factory)
    {
        var (client, code) = await factory.CreateRoomAsync();
        var host = await factory.ConnectAsync(code, "host");
        var peer = await factory.ConnectAsync(code, "client");
        return (client, code, host, peer);
    }

    [Fact]
    public async Task TwoClients_CreateJoinHelloReady_RelayedEndToEnd()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        Assert.Contains("\"HELLO\"", await RealServerFactory.SendAndReceiveAsync(host, peer, ProtocolMessage.Hello()), StringComparison.Ordinal);
        Assert.Contains("\"READY\"", await RealServerFactory.SendAndReceiveAsync(peer, host, ProtocolMessage.Ready()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShotAndResult_RelayedWithSeqPreserved()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        Assert.True(new ProtocolCodec().TryDeserialize(await RealServerFactory.SendAndReceiveAsync(host, peer, ProtocolMessage.Shot(9, "C7")), out var receivedShot, out _));
        Assert.Equal(9, receivedShot!.Seq);
        Assert.Equal("C7", receivedShot.Coord);
        Assert.True(new ProtocolCodec().TryDeserialize(await RealServerFactory.SendAndReceiveAsync(peer, host, ProtocolMessage.Result(9, ShotOutcome.MISS)), out var receivedResult, out _));
        Assert.Equal(9, receivedResult!.Seq);
        Assert.Equal(ShotOutcome.MISS, receivedResult.Outcome);
    }

    [Fact]
    public async Task Chat_RelayedToPeerOnly()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        Assert.Contains("\"CHAT\"", await RealServerFactory.SendAndReceiveAsync(host, peer, ProtocolMessage.Chat("hello")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quit_RelayedToPeer()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        Assert.Contains("\"QUIT\"", await RealServerFactory.SendAndReceiveAsync(host, peer, ProtocolMessage.Quit()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameOverQuit_DeliveredBeforePeerClose4001()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        await host.SendAsync(Encoding.UTF8.GetBytes(new ProtocolCodec().Serialize(ProtocolMessage.Quit())), WebSocketMessageType.Text, true, CancellationToken.None);
        await host.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        Assert.Contains("\"QUIT\"", await RealServerFactory.ReceiveTextAsync(peer), StringComparison.Ordinal);
        var close = await RealServerFactory.ReceiveCloseAsync(peer);
        Assert.Equal(4001, (int)close.Status!.Value);
    }

    [Fact]
    public async Task PeerDisconnect_RemainingReceives4001()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        var close = await RealServerFactory.ReceiveCloseAsync(host);
        Assert.Equal(4001, (int)close.Status!.Value);
    }

    [Fact]
    public async Task ThirdConnection_Rejected4009()
    {
        await using var factory = new RealServerFactory();
        var (client, code, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        using var third = await factory.ConnectAsync(code, "host");
        var close = await RealServerFactory.ReceiveCloseAsync(third);
        Assert.Equal(4009, (int)close.Status!.Value);
    }

    [Fact]
    public async Task HostDrop_ClientClosed4001AndRoomDisposed_NoReconnect()
    {
        await using var factory = new RealServerFactory();
        var (client, code, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        await host.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        Assert.Equal(4001, (int)(await RealServerFactory.ReceiveCloseAsync(peer)).Status!.Value);
        await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            using var response = await client.GetAsync($"api/rooms/{code}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Room was not disposed.");
            }

            await Task.Delay(50);
        }

        using var reconnect = await factory.ConnectAsync(code, "host");
        Assert.Equal(4004, (int)(await RealServerFactory.ReceiveCloseAsync(reconnect)).Status!.Value);
    }

    [Fact]
    public async Task TwoRooms_MessagesNeverCrossRooms()
    {
        await using var factory = new RealServerFactory();
        var (client1, code1) = await factory.CreateRoomAsync();
        var (client2, code2) = await factory.CreateRoomAsync();
        using var _ = client1;
        using var __ = client2;
        using var host1 = await factory.ConnectAsync(code1, "host");
        using var peer1 = await factory.ConnectAsync(code1, "client");
        using var host2 = await factory.ConnectAsync(code2, "host");
        using var peer2 = await factory.ConnectAsync(code2, "client");
        Assert.Contains("\"HELLO\"", await RealServerFactory.SendAndReceiveAsync(host1, peer1, ProtocolMessage.Hello()), StringComparison.Ordinal);
        await AssertNoTextAsync(peer2, 300);
        await AssertNoTextAsync(host2, 50);
    }

    [Fact]
    public async Task IdleEmptyRoom_ExpiresAfterTimeout()
    {
        await using var factory = new RealServerFactory(new Dictionary<string, string?>
        {
            ["Battleship:ReaperIntervalSeconds"] = "1",
            ["Battleship:RoomIdleTimeoutSeconds"] = "1"
        });
        var (client, code) = await factory.CreateRoomAsync();
        using var http = client;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(6);
        while (true)
        {
            using var response = await client.GetAsync($"api/rooms/{code}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Idle room did not expire.");
            }

            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task OccupiedRoom_ClosedWith1001AtMaxLifetime()
    {
        await using var factory = new RealServerFactory(new Dictionary<string, string?>
        {
            ["Battleship:ReaperIntervalSeconds"] = "1",
            ["Battleship:RoomMaxLifetimeMinutes"] = "1"
        });
        _ = factory.CreateClient();
        var registry = factory.Services.GetRequiredService<RoomRegistry>();
        registry.TryAdd(new Room("ZZZZZZ", DateTimeOffset.UtcNow.AddMinutes(-2)));
        using var host = await factory.ConnectAsync("ZZZZZZ", "host");
        var close = await RealServerFactory.ReceiveCloseAsync(host);
        Assert.Equal(1001, (int)close.Status!.Value);
    }

    [Fact]
    public async Task VersionMismatch_ErrorThenClose1002()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        var message = ProtocolMessage.Hello();
        message.V = 2;
        await host.SendAsync(Encoding.UTF8.GetBytes(new ProtocolCodec().Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Contains("VERSION_MISMATCH", await RealServerFactory.ReceiveTextAsync(host), StringComparison.Ordinal);
        Assert.Equal(1002, (int)(await RealServerFactory.ReceiveCloseAsync(host)).Status!.Value);
    }

    [Fact]
    public async Task MessageFlood_ReceivesRateLimitedErrors()
    {
        await using var factory = new RealServerFactory(new Dictionary<string, string?>
        {
            ["Battleship:MessageBurst"] = "1",
            ["Battleship:MessagesPerSecond"] = "1"
        });
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        var json = new ProtocolCodec().Serialize(ProtocolMessage.Hello());
        for (var i = 0; i < 5; i++)
        {
            await host.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        var found = false;
        for (var i = 0; i < 5 && !found; i++)
        {
            found = (await RealServerFactory.ReceiveTextAsync(host)).Contains("RATE_LIMITED", StringComparison.Ordinal);
        }

        Assert.True(found);
    }

    [Fact]
    public async Task NonReadingPeer_DoesNotBlockSender()
    {
        await using var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        var json = new ProtocolCodec().Serialize(ProtocolMessage.Hello());
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            await host.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MaxRoomsReached_FurtherCreatesRejected()
    {
        await using var factory = new RealServerFactory(new Dictionary<string, string?>
        {
            ["Battleship:MaxRooms"] = "1"
        });
        using var client = factory.CreateClient();
        using var first = await client.PostAsync("api/rooms", null);
        using var second = await client.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task KeepAlive_PingsAtConfiguredInterval()
    {
        await using var factory = new RealServerFactory(new Dictionary<string, string?>
        {
            ["Battleship:KeepAliveSeconds"] = "1"
        });
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        await Task.Delay(1500);
        Assert.Equal(WebSocketState.Open, host.State);
        Assert.Equal(WebSocketState.Open, peer.State);
    }

    [Fact]
    public void Logs_NeverContainChatTextOrPayloads()
    {
        var logger = new TestLogger<MessageRelay>();
        var relay = new MessageRelay(new ProtocolCodec(), logger);
        var room = TestData.Room();
        var host = TestData.Participant(RoomRole.Host);
        var client = TestData.Participant(RoomRole.Client);
        room.Assign(host);
        room.Assign(client);
        relay.Relay(room, host, ProtocolMessage.Chat("do-not-log-me"));
        relay.Relay(room, host, ProtocolMessage.Shot(1, "A1"));
        Assert.DoesNotContain(logger.Messages, entry => entry.Contains("do-not-log-me", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, entry => entry.Contains("A1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SpaRoute_FallsBackToIndexHtml()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("single/hard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Battleship", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404NotIndex()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<div id=\"app\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownWsRoute_Returns404NotIndex()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("ws/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<div id=\"app\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_ContainsNoInlineScriptOrHandlers()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.DoesNotMatch(new Regex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex("\\son\\w+\\s*=", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task FrameworkWasm_ServedWithWasmContentTypeAndCompressed()
    {
        await using var factory = new RealServerFactory();
        _ = factory.CreateClient();
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var wasm = environment.WebRootFileProvider.GetDirectoryContents("_framework").First(file => file.Name.EndsWith(".wasm", StringComparison.Ordinal));
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost/_framework/{wasm.Name}");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        using var invoker = new HttpMessageInvoker(factory.Server.CreateHandler());
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/wasm", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnSuccessErrorAndStaticResponses()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/", "/api/rooms/BAD", "/css/app.css" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GracefulShutdown_ClosesOpenSocketsWith1001()
    {
        var factory = new RealServerFactory();
        var (client, _, host, peer) = await PairAsync(factory);
        using var http = client;
        using var h = host;
        using var p = peer;
        var closeTask = RealServerFactory.ReceiveCloseAsync(host);
        await factory.DisposeAsync();
        var close = await closeTask;
        Assert.Equal(1001, (int)close.Status!.Value);
    }

    private static async Task AssertNoTextAsync(WebSocket socket, int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        var buffer = new byte[2048];
        try
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            Assert.Fail("Unexpected frame received.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
