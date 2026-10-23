namespace Battleship.Shared;

public sealed class ProtocolMessage
{
    public int? V { get; set; }
    public MessageType? Type { get; set; }
    public int? Seq { get; set; }
    public string? Coord { get; set; }
    public ShotOutcome? Outcome { get; set; }
    public ShipKind? Ship { get; set; }
    public string? Text { get; set; }
    public ProtocolErrorCode? Code { get; set; }
    public string? Detail { get; set; }

    public static ProtocolMessage Hello() => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.HELLO
    };

    public static ProtocolMessage Ready() => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.READY
    };

    public static ProtocolMessage Shot(int seq, string coordinate) => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.SHOT,
        Seq = seq,
        Coord = coordinate
    };

    public static ProtocolMessage Result(int seq, ShotOutcome outcome, ShipKind? ship = null) => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.RESULT,
        Seq = seq,
        Outcome = outcome,
        Ship = ship
    };

    public static ProtocolMessage Chat(string text) => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.CHAT,
        Text = text
    };

    public static ProtocolMessage Quit() => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.QUIT
    };

    public static ProtocolMessage Error(ProtocolErrorCode code, string? detail = null) => new()
    {
        V = ProtocolInfo.Version,
        Type = MessageType.ERROR,
        Code = code,
        Detail = ProtocolInfo.TruncateDetail(detail)
    };
}
