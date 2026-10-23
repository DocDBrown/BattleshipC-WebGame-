using System.Net.WebSockets;
using Battleship.Server.Hosting;
using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Rooms;
using Battleship.Shared;

namespace Battleship.Server.Realtime;

public sealed class RoomSocketHandler
{
    private readonly RoomRegistry _registry;
    private readonly OptionsProvider _options;
    private readonly IpRateLimiter _ipLimiter;
    private readonly MessageRelay _relay;
    private readonly ProtocolCodec _codec;
    private readonly ILogger<RoomSocketHandler> _logger;

    public RoomSocketHandler(
        RoomRegistry registry,
        OptionsProvider options,
        IpRateLimiter ipLimiter,
        MessageRelay relay,
        ProtocolCodec codec,
        ILogger<RoomSocketHandler> logger)
    {
        _registry = registry;
        _options = options;
        _ipLimiter = ipLimiter;
        _relay = relay;
        _codec = codec;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context, string roomCode)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await HttpProblem.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Validation Failed",
                "A WebSocket upgrade request is required.");
            return;
        }

        var options = _options.Get();
        WebSocket socket;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "websocket_upgrade_failed");
            return;
        }

        var clientKey = IpRateLimiter.ClientKey(context);
        if (!_ipLimiter.TryAcquire("connect", clientKey, options.ConnectPermitsPerMinute))
        {
            _logger.LogWarning("rate_limited phase=connection ip={ClientIp}", clientKey);
            await CloseSocketAsync(socket, 4029, "connection rate limit exceeded", options.SendTimeoutSeconds);
            return;
        }

        var code = (roomCode ?? string.Empty).ToUpperInvariant();
        var roleText = context.Request.Query["role"].ToString().ToLowerInvariant();
        var isHost = roleText == "host";
        if (!ProtocolInfo.IsValidRoomCode(code) || (roleText != "host" && roleText != "client"))
        {
            await CloseSocketAsync(socket, 4400, "invalid room code or role", options.SendTimeoutSeconds);
            return;
        }

        var room = _registry.Get(code);
        if (room is null)
        {
            await CloseSocketAsync(socket, 4004, "room not found", options.SendTimeoutSeconds);
            return;
        }

        var role = isHost ? RoomRole.Host : RoomRole.Client;
        await using var participant = new Participant(
            socket,
            role,
            options.SendTimeoutSeconds,
            options.MessageBurst,
            options.MessagesPerSecond);

        var assignment = room.Assign(participant);
        switch (assignment)
        {
            case RoleAssignResult.Ok:
                break;
            case RoleAssignResult.HostNotConnected:
            case RoleAssignResult.RoomClosed:
                await CloseSocketAsync(socket, 4004, "room not found", options.SendTimeoutSeconds);
                return;
            default:
                _logger.LogInformation(
                    "room_full_rejected room={RoomCode} role={Role}",
                    room.Code,
                    role);
                await CloseSocketAsync(socket, 4009, "room slot is already taken", options.SendTimeoutSeconds);
                return;
        }

        _logger.LogInformation(
            "room_joined room={RoomCode} role={Role} participant={ParticipantId}",
            room.Code,
            role,
            participant.Id);

        participant.Start(_logger);
        try
        {
            await ReceiveLoopAsync(room, participant, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The request was aborted.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "socket_failed room={RoomCode} participant={ParticipantId}", room.Code, participant.Id);
            participant.EnqueueClose(1011, "internal error");
        }
        finally
        {
            await DisconnectAsync(room, participant, options);
        }
    }

    private async Task ReceiveLoopAsync(Room room, Participant participant, CancellationToken cancellationToken)
    {
        var buffer = new byte[ProtocolInfo.MaxMessageBytes + 1];
        using var messageBuffer = new MemoryStream(ProtocolInfo.MaxMessageBytes);

        while (participant.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await participant.Socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                _logger.LogInformation("message_rejected room={RoomCode} reason=binary", room.Code);
                participant.EnqueueClose(1003, "binary frames are not supported");
                break;
            }

            if (messageBuffer.Length + result.Count > ProtocolInfo.MaxMessageBytes)
            {
                _logger.LogInformation("message_rejected room={RoomCode} reason=too_large", room.Code);
                participant.EnqueueClose(1009, "message too large");
                break;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var payload = messageBuffer.ToArray();
            messageBuffer.SetLength(0);

            if (!participant.TryConsumeMessage())
            {
                _logger.LogInformation(
                    "rate_limited room={RoomCode} participant={ParticipantId}",
                    room.Code,
                    participant.Id);
                participant.EnqueueText(_codec.Serialize(ProtocolMessage.Error(
                    ProtocolErrorCode.RATE_LIMITED,
                    "Too many messages; slow down.")));
                continue;
            }

            if (!_codec.TryDeserializeUtf8(payload, out var message, out var error) || message is null)
            {
                _logger.LogInformation(
                    "message_rejected room={RoomCode} participant={ParticipantId} reason=schema",
                    room.Code,
                    participant.Id);
                participant.EnqueueText(_codec.Serialize(ProtocolMessage.Error(
                    ProtocolErrorCode.VALIDATION,
                    error)));
                continue;
            }

            if (!_relay.Relay(room, participant, message))
            {
                break;
            }
        }
    }

    private async Task DisconnectAsync(Room room, Participant participant, BattleshipOptions options)
    {
        var (removed, peer) = room.Remove(participant);
        if (!removed)
        {
            return;
        }

        _logger.LogInformation(
            "participant_disconnected room={RoomCode} role={Role} participant={ParticipantId}",
            room.Code,
            participant.Role,
            participant.Id);

        await participant.CompleteAsync(TimeSpan.FromSeconds(options.SendTimeoutSeconds));

        if (peer is not null)
        {
            peer.EnqueueClose(4001, "peer disconnected");
        }

        if (room.TryCloseIfEmpty() && _registry.TryRemove(room))
        {
            _logger.LogInformation("room_disposed room={RoomCode}", room.Code);
        }
    }

    private static async Task CloseSocketAsync(WebSocket socket, int closeCode, string reason, int timeoutSeconds)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                await socket.CloseOutputAsync(
                    (WebSocketCloseStatus)closeCode,
                    reason,
                    timeout.Token);
            }
        }
        catch
        {
            // The client may already have gone away.
        }
        finally
        {
            socket.Dispose();
        }
    }
}
