namespace Battleship.Server.Realtime;

public readonly struct Outbound
{
    private Outbound(string? text, int? closeCode, string? reason)
    {
        Text = text;
        CloseCode = closeCode;
        Reason = reason;
    }

    public string? Text { get; }
    public int? CloseCode { get; }
    public string? Reason { get; }

    public static Outbound Message(string text) => new(text, null, null);
    public static Outbound Close(int code, string? reason) => new(null, code, reason);
}
