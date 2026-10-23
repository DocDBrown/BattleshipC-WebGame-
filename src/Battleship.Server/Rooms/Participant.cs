using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Battleship.Server.RateLimiting;
using Battleship.Server.Realtime;

namespace Battleship.Server.Rooms;

public sealed class Participant : IAsyncDisposable
{
    private readonly Channel<Outbound> _queue =
        Channel.CreateUnbounded<Outbound>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly CancellationTokenSource _cts = new();
    private readonly int _sendTimeoutSeconds;
    private Task? _writer;
    private int _completed;

    public Participant(WebSocket socket, RoomRole role, int sendTimeoutSeconds, int messageBurst, int messagesPerSecond)
    {
        Socket = socket;
        Role = role;
        _sendTimeoutSeconds = Math.Max(1, sendTimeoutSeconds);
        Messages = new TokenBucket(messageBurst, messagesPerSecond);
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public WebSocket Socket { get; }
    public RoomRole Role { get; }
    public TokenBucket Messages { get; }

    public void Start(ILogger logger) => _writer ??= Task.Run(() => WriteLoopAsync(logger));

    public bool TryConsumeMessage() => Messages.TryTake();

    public bool EnqueueText(string json) =>
        Volatile.Read(ref _completed) == 0 && _queue.Writer.TryWrite(Outbound.Message(json));

    public bool EnqueueClose(int closeCode, string? reason) =>
        _queue.Writer.TryWrite(Outbound.Close(closeCode, reason));

    public async Task CompleteAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            return;
        }

        _queue.Writer.TryComplete();
        if (_writer is not null)
        {
            try
            {
                await _writer.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                _cts.Cancel();
                try
                {
                    await _writer;
                }
                catch
                {
                    // The writer is stopped; nothing else to flush.
                }
            }
            catch
            {
                // A failed writer has already aborted the socket.
            }
        }

        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch
        {
            // The peer may already be gone.
        }

        Socket.Dispose();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync() =>
        await CompleteAsync(TimeSpan.FromSeconds(_sendTimeoutSeconds + 1));

    private async Task WriteLoopAsync(ILogger logger)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(_sendTimeoutSeconds));
                try
                {
                    if (item.CloseCode is not null)
                    {
                        if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        {
                            await Socket.CloseOutputAsync(
                                (WebSocketCloseStatus)item.CloseCode.Value,
                                item.Reason,
                                timeout.Token);
                        }

                        break;
                    }

                    if (Socket.State != WebSocketState.Open)
                    {
                        break;
                    }

                    var bytes = Encoding.UTF8.GetBytes(item.Text!);
                    await Socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    Socket.Abort();
                    break;
                }
                catch (WebSocketException)
                {
                    Socket.Abort();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "participant_writer_failed participant={ParticipantId}", Id);
            Socket.Abort();
        }
    }
}
