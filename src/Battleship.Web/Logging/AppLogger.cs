namespace Battleship.Web.Logging;

public static class AppLogger
{
    public static void Log(string eventName, string? detail = null)
    {
        Console.WriteLine(string.IsNullOrEmpty(detail)
            ? $"[battleship] {eventName}"
            : $"[battleship] {eventName} {detail}");
    }
}
