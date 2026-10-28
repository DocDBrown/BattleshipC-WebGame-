using System.Net;
using System.Net.WebSockets;
using System.Text;
using Battleship.Shared;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomsWsApiTests
{
    private static Dictionary<string, string?> Settings(int? connectPermits = null) =>
        connectPermits is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["Battleship:ConnectPermitsPerMinute"] = connectPermits.Value.ToString() };

    [Fact]
    public async Task Ws_NonUpgradeRequest_Returns400Problem()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var response = await client.GetAsync($"ws/rooms/{code}?role=host");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Ws_HostAndClientRoles_Accepted()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        using var peer = await factory.ConnectAsync(code, "client");
        Assert.Equal(WebSocketState.Open, host.State);
        Assert.Equal(WebSocketState.Open, peer.State);
    }

    [Fact]
    public async Task Ws_CloseCodes_4400_4004_4009_4029()
    {
        await using (var factory = new RealServerFactory())
        {
            var (client, _) = await factory.CreateRoomAsync();
            using var _client = client;
            using var socket = await factory.ConnectAsync("bad1", "host");
            var close = await RealServerFactory.ReceiveCloseAsync(socket);
            Assert.Equal(4400, (int)close.Status!.Value);
        }

        await using (var factory = new RealServerFactory())
        {
            using var socket = await factory.ConnectAsync("ABC234", "host");
            var close = await RealServerFactory.ReceiveCloseAsync(socket);
            Assert.Equal(4004, (int)close.Status!.Value);
        }

        await using (var factory = new RealServerFactory())
        {
            var (client, code) = await factory.CreateRoomAsync();
            using var _client = client;
            using var host = await factory.ConnectAsync(code, "host");
            using var second = await factory.ConnectAsync(code, "host");
            var close = await RealServerFactory.ReceiveCloseAsync(second);
            Assert.Equal(4009, (int)close.Status!.Value);
        }

        await using (var factory = new RealServerFactory(Settings(connectPermits: 1)))
        {
            var (client, code) = await factory.CreateRoomAsync();
            using var _client = client;
            using var host = await factory.ConnectAsync(code, "host");
            using var limited = await factory.ConnectAsync(code, "client");
            var close = await RealServerFactory.ReceiveCloseAsync(limited);
            Assert.Equal(4029, (int)close.Status!.Value);
        }
    }

    [Fact]
    public async Task Ws_MessageBeforePeerJoins_ReturnsConflictErrorFrame()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        var text = await RealServerFactory.SendAndReceiveAsync(host, host, ProtocolMessage.Hello());
        Assert.True(new ProtocolCodec().TryDeserialize(text, out var message, out var error), error);
        Assert.Equal(MessageType.ERROR, message!.Type);
        Assert.Equal(ProtocolErrorCode.CONFLICT, message.Code);
    }

    [Fact]
    public async Task Ws_MessageOver4096Bytes_Closes1009()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        await host.SendAsync(new byte[ProtocolInfo.MaxMessageBytes + 1], WebSocketMessageType.Text, true, CancellationToken.None);
        var close = await RealServerFactory.ReceiveCloseAsync(host);
        Assert.Equal(1009, (int)close.Status!.Value);
    }

    [Fact]
    public async Task Ws_BinaryFrame_Closes1003()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        await host.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
        var close = await RealServerFactory.ReceiveCloseAsync(host);
        Assert.Equal(1003, (int)close.Status!.Value);
    }

    [Fact]
    public async Task Ws_InvalidMessage_ReturnsValidationErrorFrame()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        await host.SendAsync(Encoding.UTF8.GetBytes("{bad"), WebSocketMessageType.Text, true, CancellationToken.None);
        var text = await RealServerFactory.ReceiveTextAsync(host);
        Assert.True(new ProtocolCodec().TryDeserialize(text, out var message, out var error), error);
        Assert.Equal(ProtocolErrorCode.VALIDATION, message!.Code);
    }

    [Fact]
    public async Task Ws_ErrorFrames_MatchProtocolSchema()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var host = await factory.ConnectAsync(code, "host");
        await host.SendAsync(Encoding.UTF8.GetBytes("{bad"), WebSocketMessageType.Text, true, CancellationToken.None);
        var text = await RealServerFactory.ReceiveTextAsync(host);
        Assert.True(new ProtocolCodec().TryDeserialize(text, out var message, out var error), error);
        Assert.True(ProtocolCodec.Validate(message!, out var validation));
        Assert.Equal(string.Empty, validation);
        Assert.Equal(ProtocolInfo.Version, message!.V);
        Assert.Equal(MessageType.ERROR, message.Type);
        Assert.NotNull(message.Code);
        Assert.True(message.Detail!.Length <= ProtocolInfo.ErrorDetailMaxLength);
    }
}
