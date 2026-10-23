using System.Globalization;
using System.Text.RegularExpressions;

namespace Battleship.Shared;

public static class ProtocolInfo
{
    public const int Version = 1;
    public const int MaxMessageBytes = 4096;
    public const int ChatMaxLength = 200;
    public const int ErrorDetailMaxLength = 200;
    public const int RoomCodeLength = 6;
    public const string RoomCodePattern = @"\A[A-HJ-NP-Z2-9]{6}\z";
    public const string CoordinatePattern = @"\A[A-J](10|[1-9])\z";
    public const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly Regex RoomCodeRegex =
        new(RoomCodePattern, RegexOptions.CultureInvariant);

    private static readonly Regex CoordinateRegex =
        new(CoordinatePattern, RegexOptions.CultureInvariant);

    public static bool IsValidRoomCode(string? code) =>
        code is not null && RoomCodeRegex.IsMatch(code);

    public static bool IsValidCoordinate(string? coordinate) =>
        coordinate is not null && CoordinateRegex.IsMatch(coordinate);

    public static bool IsValidChatText(string? text)
    {
        if (text is null || text.Length is < 1 or > ChatMaxLength)
        {
            return false;
        }

        if (HasInvalidSurrogates(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (char.IsControl(ch) || IsBidiOverride(ch))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasInvalidSurrogates(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBidiOverride(char ch) => ch switch
    {
        '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' => true,
        '\u2066' or '\u2067' or '\u2068' or '\u2069' => true,
        '\u200E' or '\u200F' or '\u061C' => true,
        _ => false
    };

    public static string FormatCoordinate(int row, int col) =>
        string.Create(CultureInfo.InvariantCulture, $"{(char)('A' + row)}{col + 1}");

    public static string? TruncateDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return null;
        }

        if (detail.Length <= ErrorDetailMaxLength)
        {
            return detail;
        }

        var length = ErrorDetailMaxLength;
        if (char.IsHighSurrogate(detail[length - 1]))
        {
            length--;
        }

        return detail[..length];
    }
}

public enum MessageType
{
    HELLO,
    READY,
    SHOT,
    RESULT,
    CHAT,
    QUIT,
    ERROR
}

public enum ShotOutcome
{
    HIT,
    MISS,
    SUNK,
    GAME_OVER
}

public enum ShipKind
{
    CARRIER,
    BATTLESHIP,
    CRUISER,
    SUBMARINE,
    PATROL_BOAT
}

public enum ProtocolErrorCode
{
    VALIDATION,
    CONFLICT,
    VERSION_MISMATCH,
    RATE_LIMITED,
    FATAL
}