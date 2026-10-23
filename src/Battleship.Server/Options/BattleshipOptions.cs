namespace Battleship.Server.Options;

public sealed class BattleshipOptions
{
    public const string SectionName = "Battleship";

    public int MaxRooms { get; set; } = 500;
    public int RoomCodeAttempts { get; set; } = 64;
    public int CreateRoomPermitsPerMinute { get; set; } = 30;
    public int ConnectPermitsPerMinute { get; set; } = 120;
    public int KeepAliveSeconds { get; set; } = 30;
    public int SendTimeoutSeconds { get; set; } = 5;
    public int MessagesPerSecond { get; set; } = 10;
    public int MessageBurst { get; set; } = 20;
    public int ReaperIntervalSeconds { get; set; } = 30;
    public int RoomIdleTimeoutSeconds { get; set; } = 120;
    public int RoomMaxLifetimeMinutes { get; set; } = 60;

    public BattleshipOptions Normalized() => new()
    {
        MaxRooms = Math.Max(1, MaxRooms),
        RoomCodeAttempts = Math.Max(1, RoomCodeAttempts),
        CreateRoomPermitsPerMinute = Math.Max(1, CreateRoomPermitsPerMinute),
        ConnectPermitsPerMinute = Math.Max(1, ConnectPermitsPerMinute),
        KeepAliveSeconds = Math.Max(1, KeepAliveSeconds),
        SendTimeoutSeconds = Math.Max(1, SendTimeoutSeconds),
        MessagesPerSecond = Math.Max(1, MessagesPerSecond),
        MessageBurst = Math.Max(1, MessageBurst),
        ReaperIntervalSeconds = Math.Max(1, ReaperIntervalSeconds),
        RoomIdleTimeoutSeconds = Math.Max(1, RoomIdleTimeoutSeconds),
        RoomMaxLifetimeMinutes = Math.Max(1, RoomMaxLifetimeMinutes)
    };
}
