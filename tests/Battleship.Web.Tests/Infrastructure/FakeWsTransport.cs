using System.Threading.Channels;
using Battleship.Shared;
using Battleship.Web.Net;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class FakeWsTransport : WsTransport
{
    private readonly Channel<WsFrame> _incoming = Channel.CreateUnbounded<WsFrame>();

    public bool Open { get; private set; } = true;

    public override bool IsOpen => Open;

    public List<Uri> ConnectUris { get; } = new();

    public List<TimeSpan> ConnectTimeouts { get; } = new();

    public List<ProtocolMessage> Sent { get; } = new();

    public List<string> SentTexts { get; } = new();

    public List<(int Code, string? Reason)> Closes { get; } = new();

    public int DisposeCount { get; private set; }

    public Exception? ConnectException { get; set; }

    public Func<ProtocolMessage, Exception?>? SendFailure { get; set; }

    public bool AutoHello { get; set; }

    public void Enqueue(WsFrame frame) => _incoming.Writer.TryWrite(frame);

    public void Enqueue(ProtocolMessage message) => Enqueue(WsFrame.FromText(new ProtocolCodec().Serialize(message)));

    public override Task ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (ConnectException is not null)
        {
            return Task.FromException(ConnectException);
        }

        ConnectUris.Add(uri);
        ConnectTimeouts.Add(timeout);
        Open = true;
        if (AutoHello)
        {
            Enqueue(ProtocolMessage.Hello());
        }

        return Task.CompletedTask;
    }

    public override Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        var failure = SendFailure?.Invoke(message);
        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }

    public override Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        SentTexts.Add(text);
        return Task.CompletedTask;
    }

    public override async Task<WsFrame> ReceiveAsync(CancellationToken cancellationToken) =>
        await _incoming.Reader.ReadAsync(cancellationToken);

    public override Task CloseAsync(int closeCode, string? reason, TimeSpan? timeout = null)
    {
        Closes.Add((closeCode, reason));
        Open = false;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        Open = false;
        return ValueTask.CompletedTask;
    }
}
