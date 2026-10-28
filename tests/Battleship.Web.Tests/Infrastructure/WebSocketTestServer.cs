using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Battleship.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class WebSocketTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private WebSocketTestServer(WebApplication app, Uri baseUri)
    {
        _app = app;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public ConcurrentQueue<(WebSocketCloseStatus? Status, string? Description)> CloseFrames { get; } = new();

    public static async Task<WebSocketTestServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        WebSocketTestServer? server = null;
        app.Map("/ws/{mode}", new RequestDelegate(async context =>
        {
            var mode = (string?)context.Request.RouteValues["mode"] ?? "echo";
            if (mode == "slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(2), context.RequestAborted);
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await server!.RunAsync(mode, socket, context.RequestAborted);
        }));

        await app.StartAsync();
        var address = app.Urls.First();
        server = new WebSocketTestServer(app, new Uri(address));
        return server;
    }

    public Uri Mode(string mode) => new($"ws://{BaseUri.Authority}/ws/{mode}");

    public async ValueTask DisposeAsync()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _app.StopAsync(source.Token);
        await _app.DisposeAsync();
    }

    private async Task RunAsync(string mode, WebSocket socket, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case "binary":
                await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            case "fragmented":
                await socket.SendAsync(Encoding.UTF8.GetBytes("fragment"), WebSocketMessageType.Text, false, cancellationToken);
                await socket.SendAsync(Encoding.UTF8.GetBytes("ed-message"), WebSocketMessageType.Text, true, cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            case "close":
                await socket.CloseOutputAsync((WebSocketCloseStatus)4001, "bye", cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            case "exactly":
                await socket.SendAsync(Encoding.UTF8.GetBytes(new string('a', 4096)), WebSocketMessageType.Text, true, cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            case "oversize":
                await socket.SendAsync(Encoding.UTF8.GetBytes(new string('a', 4097)), WebSocketMessageType.Text, true, cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            case "invalid-utf8":
                await socket.SendAsync(new byte[] { 0xFF, 0xFE, 0xFD }, WebSocketMessageType.Text, true, cancellationToken);
                await DrainAsync(this, socket, cancellationToken);
                break;
            default:
                await EchoAsync(this, socket, cancellationToken);
                break;
        }
    }

    private async Task EchoAsync(WebSocketTestServer server, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ProtocolInfo.MaxMessageBytes + 1];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                server.CloseFrames.Enqueue((socket.CloseStatus, socket.CloseStatusDescription));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            await socket.SendAsync(message.ToArray(), result.MessageType, true, cancellationToken);
            message.SetLength(0);
        }
    }

    private static async Task DrainAsync(WebSocketTestServer server, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                server.CloseFrames.Enqueue((socket.CloseStatus, socket.CloseStatusDescription));
            }
        }
        catch (WebSocketException)
        {
        }
    }
}
