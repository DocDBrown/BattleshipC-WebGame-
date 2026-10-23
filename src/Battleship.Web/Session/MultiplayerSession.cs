using Battleship.Shared;
using Battleship.Web.Game;

namespace Battleship.Web.Session;

public enum Side
{
    Self,
    Peer
}

public enum MultiplayerPhase
{
    Connecting,
    WaitingForPeer,
    Placement,
    WaitingForReady,
    Combat,
    GameOver,
    Error
}

public sealed record ChatEntry(Side Side, string Text);

public sealed class MultiplayerSession
{
    public string Role { get; init; } = "host";
    public string? RoomCode { get; set; }
    public Board OwnBoard { get; } = new();
    public TargetBoard Target { get; } = new();
    public bool HandshakeComplete { get; set; }
    public bool ReadySent { get; set; }
    public bool PeerReady { get; set; }
    public int NextSeq { get; set; } = 1;
    public int? PendingSeq { get; set; }
    public Coordinate? PendingCoordinate { get; set; }
    public int PeerLastSeq { get; set; }
    public Side Turn { get; set; } = Side.Peer;
    public Side? Winner { get; set; }
    public string? EndReason { get; set; }
    public List<ChatEntry> Chat { get; } = new();
    public HashSet<ShipKind> EnemySunk { get; } = new();
}
