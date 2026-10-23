using System.Net.WebSockets;

namespace Battleship.Web.Net;

public enum WsFrameKind
{
    Text,
    Binary,
    Close,
    TooLarge
}

public readonly record struct WsFrame(
    WsFrameKind Kind,
    string? Text,
    WebSocketCloseStatus? CloseStatus,
    string? CloseDescription)
{
    public static WsFrame FromText(string text) => new(WsFrameKind.Text, text, null, null);
    public static WsFrame Binary() => new(WsFrameKind.Binary, null, null, null);
    public static WsFrame TooLarge() => new(WsFrameKind.TooLarge, null, null, null);

    public static WsFrame Close(WebSocketCloseStatus? status, string? description) =>
        new(WsFrameKind.Close, null, status, description);
}
