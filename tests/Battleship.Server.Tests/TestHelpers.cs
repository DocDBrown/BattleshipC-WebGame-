using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Battleship.Server.Options;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Battleship.Server.Tests;

internal sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class FakeWebSocket : WebSocket
{
    private readonly Channel<ReceiveItem> _receive = Channel.CreateUnbounded<ReceiveItem>();
    private readonly List<SentFrame> _sent = new();
    private readonly List<(WebSocketCloseStatus Status, string? Description)> _closes = new();
    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;

    public bool Disposed { get; private set; }
    public bool Aborted { get; private set; }
    public Func<CancellationToken, Task>? SendHandler { get; set; }
    public int SendCount { get; private set; }
    public int DisposeCount { get; private set; }

    public IReadOnlyList<SentFrame> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    public IReadOnlyList<(WebSocketCloseStatus Status, string? Description)> Closes
    {
        get
        {
            lock (_sent)
            {
                return _closes.ToArray();
            }
        }
    }

    public IReadOnlyList<string> TextFrames => Sent.Where(f => f.Type == WebSocketMessageType.Text).Select(f => Encoding.UTF8.GetString(f.Data)).ToArray();

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeDescription;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;

    public void SetState(WebSocketState state) => _state = state;

    public void QueueText(string text, bool endOfMessage = true) =>
        _receive.Writer.TryWrite(new ReceiveItem(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage, null));

    public void QueueBytes(byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Text, bool endOfMessage = true) =>
        _receive.Writer.TryWrite(new ReceiveItem(bytes, type, endOfMessage, null));

    public void QueueClose() =>
        _receive.Writer.TryWrite(new ReceiveItem(Array.Empty<byte>(), WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure));

    public void QueueException(Exception exception) =>
        _receive.Writer.TryWrite(new ReceiveItem(Array.Empty<byte>(), WebSocketMessageType.Text, true, null, exception));

    public override void Abort()
    {
        Aborted = true;
        _state = WebSocketState.Aborted;
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _closeStatus = closeStatus;
        _closeDescription = statusDescription;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _closes.Add((closeStatus, statusDescription));
        }

        _closeStatus = closeStatus;
        _closeDescription = statusDescription;
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        DisposeCount++;
        Disposed = true;
        _state = WebSocketState.Closed;
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var result = await ReceiveAsync(buffer.AsMemory(), cancellationToken);
        return new WebSocketReceiveResult(result.Count, result.MessageType, result.EndOfMessage, _closeStatus, _closeDescription);
    }

    public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var item = await _receive.Reader.ReadAsync(cancellationToken);
        if (item.Exception is not null)
        {
            throw item.Exception;
        }

        if (item.Type == WebSocketMessageType.Close)
        {
            _closeStatus = item.CloseStatus;
            _closeDescription = "closed";
            return new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        var count = Math.Min(buffer.Length, item.Data.Length);
        item.Data.AsMemory(0, count).CopyTo(buffer);
        return new ValueWebSocketReceiveResult(count, item.Type, item.EndOfMessage);
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        SendCount++;
        if (SendHandler is not null)
        {
            return SendHandler(cancellationToken);
        }

        lock (_sent)
        {
            _sent.Add(new SentFrame(messageType, buffer.ToArray(), endOfMessage));
        }

        return Task.CompletedTask;
    }

    public sealed record SentFrame(WebSocketMessageType Type, byte[] Data, bool EndOfMessage);
    private sealed record ReceiveItem(byte[] Data, WebSocketMessageType Type, bool EndOfMessage, WebSocketCloseStatus? CloseStatus, Exception? Exception = null);
}

internal sealed class FakeWebSocketFeature : IHttpWebSocketFeature
{
    public bool IsWebSocketRequest { get; set; } = true;
    public WebSocket? Socket { get; set; }
    public bool AcceptThrows { get; set; }
    public int AcceptCount { get; private set; }

    public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
    {
        AcceptCount++;
        if (AcceptThrows)
        {
            throw new InvalidOperationException("upgrade failed");
        }

        return Task.FromResult(Socket ?? throw new InvalidOperationException("No socket configured."));
    }
}

internal static class TestData
{
    public static Participant Participant(RoomRole role = RoomRole.Host, FakeWebSocket? socket = null, int burst = 20, int rate = 10)
    {
        socket ??= new FakeWebSocket();
        return new Participant(socket, role, 1, burst, rate);
    }

    public static Room Room(DateTimeOffset? createdAt = null)
    {
        var code = "ABC234";
        return new Battleship.Server.Rooms.Room(code, createdAt ?? DateTimeOffset.UtcNow);
    }

    public static OptionsProvider Options(BattleshipOptions? options = null)
    {
        var monitor = new TestOptionsMonitor<BattleshipOptions>(options ?? new BattleshipOptions());
        return new OptionsProvider(monitor);
    }

    public static DefaultHttpContext HttpContext(int permits = 1000, string? remoteIp = "127.0.0.1")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { });
        context.RequestServices = services.BuildServiceProvider();
        if (remoteIp is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        return context;
    }

    public static (DefaultHttpContext Context, FakeWebSocketFeature Feature) WebSocketContext(
        FakeWebSocket socket,
        string roomCode,
        string? role,
        string remoteIp = "127.0.0.1")
    {
        var context = HttpContext(remoteIp: remoteIp);
        context.Request.Method = "GET";
        context.Request.Path = $"/ws/rooms/{roomCode}";
        if (role is not null)
        {
            context.Request.QueryString = new QueryString($"?role={role}");
        }

        var feature = new FakeWebSocketFeature { Socket = socket };
        context.Features.Set<IHttpWebSocketFeature>(feature);
        return (context, feature);
    }

    public static string Body(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
