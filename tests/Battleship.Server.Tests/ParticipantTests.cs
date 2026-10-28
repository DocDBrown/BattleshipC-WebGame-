using System.Diagnostics;
using System.Net.WebSockets;
using Battleship.Server.Rooms;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class ParticipantTests
{
    [Fact]
    public void Id_IsUnique32Hex()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => TestData.Participant().Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
    }

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.Start(new TestLogger<Participant>());
        Assert.True(participant.EnqueueText("one"));
        await WaitUntilAsync(() => socket.TextFrames.Count == 1);
        Assert.Single(socket.TextFrames);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TryConsumeMessage_UsesTokenBucket()
    {
        var participant = TestData.Participant(burst: 2, rate: 0);
        Assert.True(participant.TryConsumeMessage());
        Assert.True(participant.TryConsumeMessage());
        Assert.False(participant.TryConsumeMessage());
    }

    [Fact]
    public async Task EnqueueText_AfterComplete_ReturnsFalse()
    {
        var participant = TestData.Participant();
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
        Assert.False(participant.EnqueueText("late"));
    }

    [Fact]
    public async Task EnqueueClose_AfterComplete_ReturnsFalse()
    {
        var participant = TestData.Participant();
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
        Assert.False(participant.EnqueueClose(1000, "late"));
    }

    [Fact]
    public async Task WriteLoop_SendsTextInEnqueueOrder()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        Assert.True(participant.EnqueueText("one"));
        Assert.True(participant.EnqueueText("two"));
        Assert.True(participant.EnqueueText("three"));
        await WaitUntilAsync(() => socket.TextFrames.Count == 3);
        Assert.Equal(new[] { "one", "two", "three" }, socket.TextFrames);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_CloseItem_SendsCloseAndStops()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        Assert.True(participant.EnqueueClose(4001, "bye"));
        Assert.True(participant.EnqueueText("after"));
        await WaitUntilAsync(() => socket.Closes.Count == 1);
        Assert.Equal(4001, (int)socket.Closes[0].Status);
        Assert.Empty(socket.TextFrames);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_ItemsAfterClose_NotSent()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.EnqueueText("before");
        participant.EnqueueClose(1000, "done");
        participant.EnqueueText("after");
        await WaitUntilAsync(() => socket.Closes.Count == 1);
        Assert.Equal(new[] { "before" }, socket.TextFrames);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_SocketNotOpen_Stops()
    {
        var socket = new FakeWebSocket();
        socket.SetState(WebSocketState.Closed);
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        Assert.True(participant.EnqueueText("ignored"));
        await Task.Delay(100);
        Assert.Empty(socket.TextFrames);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_SendTimeout_AbortsSocket()
    {
        var socket = new FakeWebSocket
        {
            SendHandler = async cancellationToken => await Task.Delay(Timeout.Infinite, cancellationToken)
        };
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.EnqueueText("slow");
        await WaitUntilAsync(() => socket.Aborted, TimeSpan.FromSeconds(5));
        Assert.True(socket.Aborted);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_WebSocketException_AbortsSocket()
    {
        var socket = new FakeWebSocket { SendHandler = _ => throw new WebSocketException("boom") };
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.EnqueueText("x");
        await WaitUntilAsync(() => socket.Aborted);
        Assert.True(socket.Aborted);
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLoop_UnexpectedException_LogsWarningAndAborts()
    {
        var socket = new FakeWebSocket { SendHandler = _ => throw new InvalidOperationException("boom") };
        var logger = new TestLogger<Participant>();
        var participant = TestData.Participant(socket: socket);
        participant.Start(logger);
        participant.EnqueueText("x");
        await WaitUntilAsync(() => socket.Aborted);
        Assert.Contains(logger.Messages, message => message.Contains("participant_writer_failed", StringComparison.Ordinal));
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CompleteAsync_IsIdempotent()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        await participant.CompleteAsync(TimeSpan.FromSeconds(1));
        await participant.CompleteAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task CompleteAsync_FlushesQueueThenClosesNormal()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.EnqueueText("queued");
        await participant.CompleteAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("queued", socket.TextFrames);
        Assert.Single(socket.Closes);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closes[0].Status);
    }

    [Fact]
    public async Task CompleteAsync_WriterTimeout_CancelsWriter()
    {
        var socket = new FakeWebSocket
        {
            SendHandler = async cancellationToken => await Task.Delay(Timeout.Infinite, cancellationToken)
        };
        var participant = TestData.Participant(socket: socket);
        participant.Start(new TestLogger<Participant>());
        participant.EnqueueText("blocked");
        var watch = Stopwatch.StartNew();
        await participant.CompleteAsync(TimeSpan.FromMilliseconds(50));
        watch.Stop();
        Assert.True(socket.Aborted);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task CompleteAsync_DisposesSocket()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        await participant.CompleteAsync(TimeSpan.FromSeconds(1));
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_InvokesComplete()
    {
        var socket = new FakeWebSocket();
        var participant = TestData.Participant(socket: socket);
        await participant.DisposeAsync();
        Assert.True(socket.Disposed);
    }

    [Fact]
    public void SlowPeer_OutboundBacklog_IsBounded()
    {
        var participant = TestData.Participant();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(participant.EnqueueText($"message-{i}"));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var until = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > until)
            {
                throw new TimeoutException("Condition was not met.");
            }

            await Task.Delay(10);
        }
    }
}
