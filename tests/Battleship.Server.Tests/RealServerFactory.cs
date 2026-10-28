using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Battleship.Server.Api;
using Battleship.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Battleship.Server.Tests;

internal sealed class RealServerFactory : WebApplicationFactory<RoomApi>
{
    private readonly Dictionary<string, string?> _settings;

    public RealServerFactory(Dictionary<string, string?>? settings = null) =>
        _settings = settings ?? new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_settings));
    }

    public async Task<(HttpClient Client, string Code)> CreateRoomAsync()
    {
        var client = CreateClient();
        using var response = await client.PostAsync("api/rooms", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return (client, body!["roomCode"]);
    }

    public async Task<WebSocket> ConnectAsync(string code, string role, CancellationToken cancellationToken = default)
    {
        var client = Server.CreateWebSocketClient();
        return await client.ConnectAsync(new Uri($"ws://localhost/ws/rooms/{code}?role={role}"), cancellationToken);
    }

    public static async Task<string> SendAndReceiveAsync(WebSocket sender, WebSocket receiver, ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        var codec = new ProtocolCodec();
        await sender.SendAsync(
            Encoding.UTF8.GetBytes(codec.Serialize(message)),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
        return await ReceiveTextAsync(receiver, cancellationToken);
    }

    public static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var buffer = new byte[ProtocolInfo.MaxMessageBytes + 64];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException($"Socket closed with {socket.CloseStatus}.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    public static async Task<(WebSocketCloseStatus? Status, string? Description)> ReceiveCloseAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (socket.CloseStatus, socket.CloseStatusDescription);
            }
        }
    }

    public static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var until = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > until)
            {
                throw new TimeoutException("Condition was not met.");
            }

            await Task.Delay(20);
        }
    }
}
