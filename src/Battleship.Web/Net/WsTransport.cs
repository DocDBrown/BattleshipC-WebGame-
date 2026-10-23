using System.Net.WebSockets;
using System.Text;
using Battleship.Shared;

namespace Battleship.Web.Net;

public sealed class WsTransport : IAsyncDisposable
{
    private static readonly ProtocolCodec Codec = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private int _disposed;

    public ClientWebSocket Socket => _socket ?? throw new InvalidOperationException("The socket is not connected.");

    public bool IsOpen => _socket is { State: WebSocketState.Open };

    public async Task ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_socket is not null)
        {
            throw new InvalidOperationException("The transport is already connected.");
        }

        var socket = new ClientWebSocket();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await socket.ConnectAsync(uri, timeoutSource.Token);
            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken) =>
        SendTextAsync(Codec.Serialize(message), cancellationToken);

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > ProtocolInfo.MaxMessageBytes)
        {
            throw new InvalidOperationException("The message exceeds the protocol size limit.");
        }

        var socket = Socket;
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<WsFrame> ReceiveAsync(CancellationToken cancellationToken)
    {
        var socket = Socket;
        var buffer = new byte[ProtocolInfo.MaxMessageBytes + 1];
        using var message = new MemoryStream(ProtocolInfo.MaxMessageBytes);

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return WsFrame.Close(socket.CloseStatus, socket.CloseStatusDescription);
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                return WsFrame.Binary();
            }

            if (message.Length + result.Count > ProtocolInfo.MaxMessageBytes)
            {
                return WsFrame.TooLarge();
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            return WsFrame.FromText(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    public async Task CloseAsync(int closeCode, string? reason, TimeSpan? timeout = null)
    {
        var socket = _socket;
        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var source = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
                await socket.CloseOutputAsync((WebSocketCloseStatus)closeCode, reason, source.Token);
            }
        }
        catch
        {
            // The socket is already closing.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await CloseAsync(1000, "page closed");
        _socket?.Dispose();
        _socket = null;
        _sendLock.Dispose();
    }
}
