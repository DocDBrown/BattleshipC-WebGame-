using System.Net.WebSockets;
using System.Text;
using Battleship.Shared;
using Battleship.Web.Game;
using Battleship.Web.Net;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class WsTransportTests
{
    [Fact]
    public void Socket_BeforeConnect_Throws()
    {
        var transport = new WsTransport();
        Assert.Throws<InvalidOperationException>(() => transport.Socket);
    }

    [Fact]
    public async Task Connect_Twice_Throws()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None));
    }

    [Fact]
    public async Task Connect_Timeout_ThrowsAndDisposesSocket()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.ConnectAsync(server.Mode("slow"), TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.False(transport.IsOpen);
        Assert.Throws<InvalidOperationException>(() => transport.Socket);
    }

    [Fact]
    public async Task Send_OverMaxBytes_Throws()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendTextAsync(new string('a', ProtocolInfo.MaxMessageBytes + 1), CancellationToken.None));
    }

    [Fact]
    public async Task Send_SerializesWithCodec()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var message = ProtocolMessage.Shot(7, "C4");
        await transport.SendAsync(message, CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Text, frame.Kind);
        Assert.Equal(new ProtocolCodec().Serialize(message), frame.Text);
    }

    [Fact]
    public async Task Send_Concurrent_FramesNotInterleaved()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var messages = Enumerable.Range(1, 20).Select(seq => ProtocolMessage.Shot(seq, new Coordinate(seq % 10, seq / 10).Label)).ToArray();
        await Task.WhenAll(messages.Select(message => transport.SendAsync(message, CancellationToken.None)));
        var codec = new ProtocolCodec();
        var received = new HashSet<int>();
        for (var index = 0; index < messages.Length; index++)
        {
            var frame = await transport.ReceiveAsync(WithTimeout());
            Assert.Equal(WsFrameKind.Text, frame.Kind);
            Assert.True(codec.TryDeserialize(frame.Text!, out var parsed, out var error), error);
            received.Add(parsed!.Seq!.Value);
        }

        Assert.Equal(messages.Select(message => message.Seq!.Value).ToHashSet(), received);
    }

    [Fact]
    public async Task Receive_Text_ReturnsText()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        await transport.SendTextAsync("hello", CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Text, frame.Kind);
        Assert.Equal("hello", frame.Text);
    }

    [Fact]
    public async Task Receive_Exactly4096Bytes_ReturnsText()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("exactly"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Text, frame.Kind);
        Assert.Equal(4096, frame.Text!.Length);
    }

    [Fact]
    public async Task Receive_Fragmented_Reassembled()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("fragmented"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Text, frame.Kind);
        Assert.Equal("fragmented-message", frame.Text);
    }

    [Fact]
    public async Task Receive_Binary_ReturnsBinary()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("binary"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Binary, frame.Kind);
    }

    [Fact]
    public async Task Receive_Close_ReturnsStatusAndDescription()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("close"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.Close, frame.Kind);
        Assert.Equal((WebSocketCloseStatus)4001, frame.CloseStatus);
        Assert.Equal("bye", frame.CloseDescription);
    }

    [Fact]
    public async Task Receive_OverMaxBytes_ReturnsTooLarge()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("oversize"), TimeSpan.FromSeconds(2), CancellationToken.None);
        var frame = await transport.ReceiveAsync(WithTimeout());
        Assert.Equal(WsFrameKind.TooLarge, frame.Kind);
    }

    [Fact]
    public async Task Receive_InvalidUtf8_RejectedNotReplaced()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("invalid-utf8"), TimeSpan.FromSeconds(2), CancellationToken.None);
        await Assert.ThrowsAsync<WebSocketException>(() => transport.ReceiveAsync(WithTimeout()));
    }

    [Fact]
    public async Task Close_NotConnected_NoOp()
    {
        await using var transport = new WsTransport();
        await transport.CloseAsync(1000, "nothing");
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndCloses1000()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        var transport = new WsTransport();
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        var close = await WaitForCloseAsync(server);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, close.Status);
    }

    [Fact]
    public async Task IsOpen_ReflectsSocketState()
    {
        await using var server = await WebSocketTestServer.StartAsync();
        await using var transport = new WsTransport();
        Assert.False(transport.IsOpen);
        await transport.ConnectAsync(server.Mode("echo"), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(transport.IsOpen);
        await transport.CloseAsync(1000, "done");
        Assert.False(transport.IsOpen);
    }

    private static CancellationToken WithTimeout()
    {
        var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return source.Token;
    }

    private static async Task<(WebSocketCloseStatus? Status, string? Description)> WaitForCloseAsync(WebSocketTestServer server)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < until)
        {
            if (server.CloseFrames.TryDequeue(out var close))
            {
                return close;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The server did not observe a close frame.");
    }
}
