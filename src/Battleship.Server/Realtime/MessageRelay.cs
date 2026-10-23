using Battleship.Server.Rooms;
using Battleship.Shared;

namespace Battleship.Server.Realtime;

public sealed class MessageRelay
{
    private readonly ProtocolCodec _codec;
    private readonly ILogger<MessageRelay> _logger;

    public MessageRelay(ProtocolCodec codec, ILogger<MessageRelay> logger)
    {
        _codec = codec;
        _logger = logger;
    }

    public bool Relay(Room room, Participant sender, ProtocolMessage message)
    {
        if (message.V != ProtocolInfo.Version)
        {
            _logger.LogWarning(
                "message_rejected room={RoomCode} role={Role} reason=version",
                room.Code,
                sender.Role);
            sender.EnqueueText(_codec.Serialize(ProtocolMessage.Error(
                ProtocolErrorCode.VERSION_MISMATCH,
                "Protocol version mismatch. Reload the page.")));
            sender.EnqueueClose(1002, "version mismatch");
            return false;
        }

        var peer = room.PeerOf(sender);
        if (peer is null)
        {
            _logger.LogInformation(
                "message_rejected room={RoomCode} role={Role} reason=no_peer",
                room.Code,
                sender.Role);
            sender.EnqueueText(_codec.Serialize(ProtocolMessage.Error(
                ProtocolErrorCode.CONFLICT,
                "No opponent is connected.")));
            return true;
        }

        room.Touch();
        if (!peer.EnqueueText(_codec.Serialize(message)))
        {
            _logger.LogWarning(
                "message_rejected room={RoomCode} role={Role} reason=peer_unavailable",
                room.Code,
                sender.Role);
            sender.EnqueueText(_codec.Serialize(ProtocolMessage.Error(
                ProtocolErrorCode.FATAL,
                "The opponent connection is unavailable.")));
            return false;
        }

        _logger.LogInformation(
            "message_relayed room={RoomCode} type={Type} seq={Seq}",
            room.Code,
            message.Type,
            message.Seq);
        return true;
    }
}
